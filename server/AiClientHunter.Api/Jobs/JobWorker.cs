using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Services;
using AiClientHunter.Api.Sources;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AiClientHunter.Api.Jobs;

public class JobWorker(
    IServiceScopeFactory scopes,
    JobQueue queue,
    ILogger<JobWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("JobWorker background service started.");

        // 1. Re-queue unfinished jobs after restart so the database remains the source of truth
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var ids = await db.SearchJobs
                .Where(j => j.Status == "queued" || j.Status == "running")
                .Select(j => j.Id)
                .ToListAsync(ct);

            foreach (var id in ids)
            {
                log.LogInformation("Re-queuing unfinished job #{Id} found in database.", id);
                await queue.EnqueueAsync(id);
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to re-queue unfinished jobs on startup.");
        }

        // 2. Process jobs one at a time sequentially from the channel
        await foreach (var id in queue.ReadAllAsync(ct))
        {
            try
            {
                await RunAsync(id, ct);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Search Job #{Id} crashed unhandled.", id);
            }
        }
    }

    private async Task RunAsync(long id, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var job = await db.SearchJobs.FindAsync(new object[] { id }, ct);
        if (job is null) return;

        var adapters = scope.ServiceProvider.GetServices<ISourceAdapter>();
        var adapter = adapters.FirstOrDefault(a => a.Name.Equals(job.Source, StringComparison.OrdinalIgnoreCase))
                      ?? adapters.FirstOrDefault(a => a.Name == "google")
                      ?? adapters.First();

        var websiteChecker = scope.ServiceProvider.GetRequiredService<WebsiteChecker>();
        var dedupeService = scope.ServiceProvider.GetRequiredService<DedupeService>();
        var enrichmentService = scope.ServiceProvider.GetRequiredService<ContactEnrichmentService>();

        job.Status = "running";
        job.StartedAt = DateTime.UtcNow;
        job.Error = null;
        await db.SaveChangesAsync(ct);

        log.LogInformation("Processing Search Job #{Id} using {Source}: {Category} in {City} (LeadType: {LeadType}, Limit: {Limit}).",
            job.Id, adapter.Name, job.Category, job.City, job.LeadType, job.ResultLimit);

        try
        {
            IReadOnlyList<RawRecord> records;

            // 1. 24-hour cache check in SourceRecords (only for OSM)
            var cacheCutoff = DateTime.UtcNow.AddHours(-24);
            var cachedRecords = adapter.Name == "osm" ? await db.SourceRecords
                .Where(s => s.SourceName == "osm" && s.RetrievedAt >= cacheCutoff)
                .ToListAsync(ct) : new List<SourceRecord>();

            var relevantCache = cachedRecords
                .Where(s => s.RawJson.Contains($"\"{job.City}\"", StringComparison.OrdinalIgnoreCase) ||
                            s.RawJson.Contains(job.Category, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (relevantCache.Count >= job.ResultLimit && relevantCache.Count > 0)
            {
                log.LogInformation("Using 24-hour cached data ({Count} records) for {Category} in {City}.", relevantCache.Count, job.Category, job.City);
                records = relevantCache.Select(c => new RawRecord(
                    SourceName: c.SourceName,
                    SourceId: c.SourceId,
                    SourceUrl: c.SourceUrl,
                    Name: ExtractNameFromRaw(c.RawJson) ?? "Business Listing",
                    Category: job.Category,
                    City: job.City,
                    Phone: ExtractPhoneFromRaw(c.RawJson),
                    Website: ExtractWebsiteFromRaw(c.RawJson),
                    RawJson: c.RawJson,
                    RetrievedAt: c.RetrievedAt,
                    Email: ExtractEmailFromRaw(c.RawJson)
                )).Take(job.ResultLimit).ToList();
            }
            else
            {
                records = await adapter.FetchAsync(new SearchRequest(job.City, job.Category, job.ResultLimit), ct);
            }

            job.RecordsFound = records.Count;

            // 2. Candidate Selection & Website Filtering
            bool isPlatformJobSource = job.Source == "freelancer" || job.Source == "upwork" || job.Source == "reddit" || job.Source == "linkedin";
            var candidateRecords = records.AsEnumerable();
            if (!isPlatformJobSource)
            {
                // Strict no-website requirement: Discard raw records that already have a website listed
                candidateRecords = records.Where(r => string.IsNullOrWhiteSpace(r.Website));
            }

            // Take extra candidate cushion so after Google enrichment discards records with existing websites,
            // we still achieve the user's requested ResultLimit
            var candidateList = candidateRecords.Take(Math.Max(job.ResultLimit * 2, 40)).ToList();

            // Google Contact Details Enrichment:
            // Check candidate businesses on Google Maps/Search to find missing Phone, Website, Email, and Address
            var enrichmentCandidates = candidateList
                .Where(r => r.SourceName != "freelancer" && r.SourceName != "upwork" && r.SourceName != "reddit" && r.SourceName != "linkedin")
                .Where(r => string.IsNullOrWhiteSpace(r.Phone) || string.IsNullOrWhiteSpace(r.Website) || string.IsNullOrWhiteSpace(r.Email))
                .Select(r => (r.Name, r.City))
                .ToList();

            IReadOnlyDictionary<string, EnrichedContact> enrichedMap = new Dictionary<string, EnrichedContact>();
            if (enrichmentCandidates.Count > 0)
            {
                log.LogInformation("Looking up {Count} businesses on Google for verified contact numbers and website links...", enrichmentCandidates.Count);
                try
                {
                    enrichedMap = await enrichmentService.EnrichBatchAsync(enrichmentCandidates, ct);
                }
                catch (Exception ex)
                {
                    log.LogWarning(ex, "Google contact enrichment step completed with warnings.");
                }
            }

            int newLeadsCount = 0;
            int duplicatesCount = 0;

            foreach (var r in candidateList)
            {
                bool isPlatformJob = r.SourceName == "freelancer" || r.SourceName == "upwork" || r.SourceName == "reddit" || r.SourceName == "linkedin";

                // Check if Google returned enriched contact details
                EnrichedContact? enriched = null;
                if (enrichedMap.TryGetValue(r.Name.Trim(), out var ec) && ec.Enriched)
                {
                    enriched = ec;
                }

                var currentPhone = r.Phone;
                var currentWebsite = r.Website;
                var currentEmail = r.Email;
                var currentAddress = ExtractAddressFromRaw(r.RawJson);

                if (enriched != null)
                {
                    if (string.IsNullOrWhiteSpace(currentPhone) && !string.IsNullOrWhiteSpace(enriched.Phone))
                        currentPhone = enriched.Phone;
                    if (string.IsNullOrWhiteSpace(currentWebsite) && !string.IsNullOrWhiteSpace(enriched.Website))
                        currentWebsite = enriched.Website;
                    if (string.IsNullOrWhiteSpace(currentEmail) && !string.IsNullOrWhiteSpace(enriched.Email))
                        currentEmail = enriched.Email;
                    if (string.IsNullOrWhiteSpace(currentAddress) && !string.IsNullOrWhiteSpace(enriched.Address))
                        currentAddress = enriched.Address;
                }

                // STRICT NO-WEBSITE FILTER (For all directory/registry sources: Justdial, Zauba Corp, Google, OSM):
                // If the business already has an active website (discovered via source or Google enrichment), DISCARD!
                if (!isPlatformJob)
                {
                    if (!string.IsNullOrWhiteSpace(currentWebsite) || (enriched != null && !string.IsNullOrWhiteSpace(enriched.Website)))
                    {
                        log.LogInformation("Strict Filter: Discarding '{Name}' because an active website was found ({Url}).", r.Name, currentWebsite ?? enriched?.Website);
                        continue;
                    }
                }

                // Phase 6 Step 1: Normalize phone, email, domain, and dedupe key
                var phone = Normalizer.Phone(currentPhone);
                var email = Normalizer.Email(currentEmail);
                var domain = Normalizer.Domain(currentWebsite);
                var dedupeKey = (r.SourceName == "zaubacorp")
                    ? $"zaubacorp|{r.SourceId}"
                    : (isPlatformJob
                        ? $"{r.SourceName}|{r.SourceId}"
                        : (domain ?? $"{Normalizer.NameKey(r.Name)}|{Normalizer.NameKey(r.City ?? "")}"));

                // Ensure SourceRecord is persisted
                var sourceRecord = await db.SourceRecords
                    .FirstOrDefaultAsync(s => s.SourceName == r.SourceName && s.SourceId == r.SourceId, ct);

                if (sourceRecord is null)
                {
                    sourceRecord = new SourceRecord
                    {
                        SourceName = r.SourceName,
                        SourceId = r.SourceId,
                        SourceUrl = r.SourceUrl,
                        RetrievedAt = r.RetrievedAt,
                        RawJson = r.RawJson
                    };
                    db.SourceRecords.Add(sourceRecord);
                    await db.SaveChangesAsync(ct);
                }

                // Phase 6 Step 2: Deduplicate
                var (existingLead, isDuplicate) = await dedupeService.FindExistingAsync(db, r, domain, dedupeKey, ct);

                if (isDuplicate && existingLead != null)
                {
                    // Strict filter: If existing lead already has an active website, discard it for directory searches
                    if (!isPlatformJob && (!string.IsNullOrWhiteSpace(existingLead.WebsiteUrl) || existingLead.WebsiteStatus == "verified"))
                    {
                        log.LogInformation("Strict Filter: Discarding duplicate '{Name}' because existing lead already has an active website.", existingLead.Name);
                        continue;
                    }

                    // Existing lead: enrich contact details if previously missing
                    if (existingLead.Email == null && email != null) existingLead.Email = email;
                    if (existingLead.Phone == null && phone != null) existingLead.Phone = phone;
                    if (existingLead.WebsiteUrl == null && currentWebsite != null) existingLead.WebsiteUrl = currentWebsite;

                    // Update evidence and bump timestamp
                    var alreadyLinked = await db.EvidenceLinks
                        .AnyAsync(e => e.LeadId == existingLead.Id && e.SourceRecordId == sourceRecord.Id, ct);

                    if (!alreadyLinked)
                    {
                        string reDiscoverNote = isPlatformJob
                            ? $"Re-discovered via {r.SourceName.ToUpperInvariant()} search for '{job.Category}'."
                            : (r.SourceName == "justdial" ? "Re-discovered via Justdial local business directory." :
                               (r.SourceName == "zaubacorp" ? "Re-discovered via Zauba Corp company registry." : "Re-discovered via OpenStreetMap / Web search."));

                        if (enriched != null && enriched.Enriched)
                        {
                            reDiscoverNote += $" | ✨ Enriched via Google: Phone: {phone ?? "N/A"}, Website: {currentWebsite ?? "None"}, Email: {email ?? "N/A"}";
                        }

                        if (reDiscoverNote.Length > 950) reDiscoverNote = reDiscoverNote[..950] + "...";

                        db.EvidenceLinks.Add(new LeadEvidence
                        {
                            LeadId = existingLead.Id,
                            SourceRecordId = sourceRecord.Id,
                            Note = reDiscoverNote,
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    existingLead.UpdatedAt = DateTime.UtcNow;
                    duplicatesCount++;
                }
                else
                {
                    // Phase 6 Step 3: Check Website honestly
                    WebsiteCheckResult webResult;
                    if (isPlatformJob)
                    {
                        webResult = new WebsiteCheckResult("missing", null, $"Client brief on {r.SourceName}");
                    }
                    else if (r.SourceName == "zaubacorp")
                    {
                        webResult = currentWebsite != null
                            ? new WebsiteCheckResult("verified", currentWebsite, "Website discovered and verified via Google search.")
                            : new WebsiteCheckResult("missing", null, "Newly incorporated entity registered on MCA/Zauba Corp without an official website.");
                    }
                    else if (r.SourceName == "justdial")
                    {
                        webResult = currentWebsite != null
                            ? new WebsiteCheckResult("verified", currentWebsite, "Website discovered and verified via Google search.")
                            : new WebsiteCheckResult("missing", null, "Verified Indian business on Justdial without an official website.");
                    }
                    else if (!string.IsNullOrWhiteSpace(domain))
                    {
                        webResult = await websiteChecker.CheckListedAsync(currentWebsite!, ct);
                    }
                    else
                    {
                        webResult = await websiteChecker.FindCandidateAsync(r.Name, ct);
                    }

                    // STRICT NO-WEBSITE FILTER (Applies to all non-platform sources):
                    // If website checker found a verified active website or any website exists, discard!
                    if (!isPlatformJob && (webResult.Status == "verified" || !string.IsNullOrWhiteSpace(webResult.Url) || !string.IsNullOrWhiteSpace(currentWebsite)))
                    {
                        log.LogInformation("Strict Filter: Discarding '{Name}' because an active website was verified ({Url}).", r.Name, webResult.Url ?? currentWebsite);
                        continue;
                    }

                    // Phase 6 Step 4: Evaluate signals and score
                    bool isRecent7Days = isPlatformJob || (r.SourceName == "zaubacorp") || (r.SourceName == "justdial") || CheckIfRecent(r.RawJson, 7);
                    bool isNewBusiness = (r.SourceName == "zaubacorp") || (!isPlatformJob && CheckIfNewListing(r.RawJson));
                    bool websiteFieldMissing = string.IsNullOrWhiteSpace(currentWebsite);
                    bool noLiveSiteFound = currentWebsite == null && (webResult.Status == "not_found" || webResult.Status == "missing");
                    bool hasPhone = phone != null;
                    bool inTargetArea = true;

                    var signals = new LeadSignals(
                        ExplicitRequest: isPlatformJob,
                        RequestDetails: isPlatformJob,
                        Recent7Days: isRecent7Days,
                        NewBusinessSignal: isNewBusiness,
                        WebsiteFieldMissing: websiteFieldMissing,
                        NoLiveSiteFound: noLiveSiteFound,
                        HasPhone: hasPhone,
                        InTargetArea: inTargetArea
                    );

                    var scoreResult = Scorer.Score(signals);

                    // Create new Lead
                    var lead = new Lead
                    {
                        Name = r.Name,
                        Category = r.Category,
                        City = r.City ?? "Remote",
                        State = isPlatformJob ? "Global" : ResolveState(r.City, r.RawJson),
                        Country = isPlatformJob ? "Remote / Global" : "India",
                        Email = email,
                        Phone = phone,
                        WebsiteUrl = isPlatformJob ? null : (webResult.Status == "verified" ? webResult.Url : null),
                        WebsiteStatus = isPlatformJob ? "missing" : (webResult.Status == "verified" ? "verified" : "missing"),
                        WebsiteCheckedAt = DateTime.UtcNow,
                        LeadType = isPlatformJob ? "hot" : (r.SourceName == "zaubacorp" ? "warm" : "potential"),
                        Score = scoreResult.Points,
                        Status = "new",
                        DedupeKey = dedupeKey,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    db.Leads.Add(lead);
                    await db.SaveChangesAsync(ct);

                    // Persist LeadEvidence
                    string snippet = ExtractSnippetFromRaw(r.RawJson);
                    if (snippet.Length > 300) snippet = snippet[..300] + "...";
                    string budget = ExtractBudgetFromRaw(r.RawJson);

                    string evidenceNote;
                    if (r.SourceName == "freelancer")
                    {
                        evidenceNote = $"Discovered on Freelancer.com for '{job.Category}'. Budget: {budget}. Project Brief: {snippet}. Direct Link: {r.SourceUrl}";
                    }
                    else if (r.SourceName == "upwork")
                    {
                        evidenceNote = $"Discovered on Upwork for '{job.Category}'. Budget: {budget}. Project Brief: {snippet}. Direct Link: {r.SourceUrl}";
                    }
                    else if (r.SourceName == "reddit")
                    {
                        evidenceNote = $"Discovered on Reddit r/forhire client post for '{job.Category}'. Budget: {budget}. Post Brief: {snippet}. Direct Link: {r.SourceUrl}";
                    }
                    else if (r.SourceName == "linkedin")
                    {
                        evidenceNote = $"Discovered on LinkedIn hiring posting for '{job.Category}'. Budget: {budget}. Role Details: {snippet}. Direct Link: {r.SourceUrl}";
                    }
                    else if (r.SourceName == "justdial")
                    {
                        var addr = currentAddress ?? ExtractAddressFromRaw(r.RawJson);
                        evidenceNote = $"Discovered on Justdial for '{job.Category}' in {job.City}. Direct Verified Phone: {phone ?? "Available"}. Address: {addr}. Listing: {r.SourceUrl}";
                        if (enriched != null && enriched.Enriched)
                        {
                            evidenceNote += $" | ✨ Enriched via Google: Website: {currentWebsite ?? "None"}, Email: {email ?? "N/A"}";
                        }
                    }
                    else if (r.SourceName == "zaubacorp")
                    {
                        var addr = currentAddress ?? ExtractAddressFromRaw(r.RawJson);
                        evidenceNote = $"Discovered on Zauba Corp (Newly Registered Company) in {job.City}. CIN: {r.SourceId}. Address: {addr}. Profile: {r.SourceUrl}";
                        if (enriched != null && enriched.Enriched)
                        {
                            evidenceNote += $" | ✨ Enriched via Google: Phone: {phone ?? "N/A"}, Website: {currentWebsite ?? "None"}, Email: {email ?? "N/A"}";
                        }
                    }
                    else if (r.SourceName == "google")
                    {
                        evidenceNote = $"Discovered via Google Web Search for {job.Category} in {job.City}. Snippet: {snippet}. Web check: {webResult.Status} ({webResult.Note}).";
                        if (enriched != null && enriched.Enriched)
                        {
                            evidenceNote += $" | ✨ Enriched via Google: Phone: {phone ?? "N/A"}, Website: {currentWebsite ?? "None"}, Email: {email ?? "N/A"}";
                        }
                    }
                    else
                    {
                        evidenceNote = $"Discovered via OpenStreetMap for {job.Category} in {job.City}. Web check: {webResult.Status} ({webResult.Note}).";
                        if (enriched != null && enriched.Enriched)
                        {
                            evidenceNote += $" | ✨ Enriched via Google: Phone: {phone ?? "N/A"}, Website: {currentWebsite ?? "None"}, Email: {email ?? "N/A"}";
                        }
                    }

                    if (evidenceNote.Length > 950) evidenceNote = evidenceNote[..950] + "...";

                    db.EvidenceLinks.Add(new LeadEvidence
                    {
                        LeadId = lead.Id,
                        SourceRecordId = sourceRecord.Id,
                        Note = evidenceNote,
                        CreatedAt = DateTime.UtcNow
                    });

                    // Persist LeadScore with reasons
                    db.LeadScores.Add(new LeadScore
                    {
                        LeadId = lead.Id,
                        Points = scoreResult.Points,
                        Priority = scoreResult.Priority,
                        Reasons = string.Join(" | ", scoreResult.Reasons),
                        RuleVersion = "v1",
                        CreatedAt = DateTime.UtcNow
                    });

                    newLeadsCount++;
                    if (newLeadsCount >= job.ResultLimit)
                    {
                        log.LogInformation("Job #{Id} reached requested limit of {Limit} leads. Concluding job processing.", job.Id, job.ResultLimit);
                        break;
                    }
                }
            }

            await db.SaveChangesAsync(ct);

            job.NewLeads = newLeadsCount;
            job.Duplicates = duplicatesCount;
            job.Status = "completed";

            log.LogInformation("Job #{Id} completed: {Found} found, {New} new leads, {Dupes} duplicates.",
                job.Id, job.RecordsFound, job.NewLeads, job.Duplicates);
        }
        catch (Exception ex)
        {
            job.Status = "failed";
            job.Error = ex.Message;
            log.LogWarning(ex, "Job #{Id} failed: {Error}", job.Id, ex.Message);
        }
        finally
        {
            job.FinishedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private static string? ExtractNameFromRaw(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("tags", out var tags) &&
                tags.TryGetProperty("name", out var name))
            {
                return name.GetString();
            }
        }
        catch { }
        return null;
    }

    private static string? ExtractPhoneFromRaw(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("tags", out var tags))
            {
                if (tags.TryGetProperty("phone", out var p1)) return p1.GetString();
                if (tags.TryGetProperty("contact:phone", out var p2)) return p2.GetString();
                if (tags.TryGetProperty("contact:mobile", out var p3)) return p3.GetString();
            }
        }
        catch { }
        return null;
    }

    private static string? ExtractWebsiteFromRaw(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("tags", out var tags))
            {
                if (tags.TryGetProperty("website", out var w1)) return w1.GetString();
                if (tags.TryGetProperty("contact:website", out var w2)) return w2.GetString();
                if (tags.TryGetProperty("url", out var w3)) return w3.GetString();
            }
        }
        catch { }
        return null;
    }

    private static bool CheckIfRecent(string rawJson, int days)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("timestamp", out var ts) &&
                ts.TryGetDateTime(out var dt))
            {
                return dt >= DateTime.UtcNow.AddDays(-days);
            }
        }
        catch { }
        return false;
    }

    private static bool CheckIfNewListing(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("version", out var v) && v.GetInt32() == 1)
            {
                return CheckIfRecent(rawJson, 90);
            }
        }
        catch { }
        return false;
    }

    private static string? ExtractEmailFromRaw(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("tags", out var tags))
            {
                if (tags.TryGetProperty("email", out var e1)) return e1.GetString();
                if (tags.TryGetProperty("contact:email", out var e2)) return e2.GetString();
                if (tags.TryGetProperty("contact:e-mail", out var e3)) return e3.GetString();
                if (tags.TryGetProperty("operator:email", out var e4)) return e4.GetString();
            }
        }
        catch { }
        return null;
    }

    private static string ExtractSnippetFromRaw(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("description", out var d)) return d.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("snippet", out var s)) return s.GetString() ?? "";
        }
        catch { }
        return "";
    }

    private static string ExtractBudgetFromRaw(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("budget", out var b)) return b.GetString() ?? "";
        }
        catch { }
        return "Flexible / Open";
    }

    private static string ResolveState(string? city, string? rawJson)
    {
        if (!string.IsNullOrWhiteSpace(rawJson))
        {
            string[] indianStates = [
                "Maharashtra", "Gujarat", "Karnataka", "Delhi", "Rajasthan",
                "Telangana", "West Bengal", "Uttar Pradesh", "Madhya Pradesh",
                "Punjab", "Haryana", "Bihar", "Tamil Nadu", "Kerala", "Goa", "Odisha", "Assam"
            ];
            foreach (var s in indianStates)
            {
                if (rawJson.Contains(s, StringComparison.OrdinalIgnoreCase))
                    return s;
            }
        }

        if (string.IsNullOrWhiteSpace(city)) return "India";

        var c = city.Trim().ToLowerInvariant();
        return c switch
        {
            "mumbai" or "pune" or "nagpur" or "nashik" or "aurangabad" or "thane" => "Maharashtra",
            "delhi" or "new delhi" or "noida" or "gurugram" or "faridabad" or "ghaziabad" => "Delhi NCR",
            "bengaluru" or "bangalore" or "mysuru" or "hubli" => "Karnataka",
            "hyderabad" or "warangal" => "Telangana",
            "jaipur" or "jodhpur" or "udaipur" or "kota" => "Rajasthan",
            "kolkata" or "howrah" => "West Bengal",
            "lucknow" or "kanpur" or "agra" or "varanasi" or "meerut" or "prayagraj" => "Uttar Pradesh",
            "indore" or "bhopal" or "gwalior" or "jabalpur" => "Madhya Pradesh",
            "chandigarh" or "amritsar" or "ludhiana" or "jalandhar" => "Punjab",
            "patna" or "gaya" => "Bihar",
            "chennai" or "coimbatore" or "madurai" => "Tamil Nadu",
            "ahmedabad" or "surat" or "vadodara" or "rajkot" or "gandhinagar" or "bhavnagar" or "jamnagar" => "Gujarat",
            _ => "India"
        };
    }

    private static string ExtractAddressFromRaw(string rawJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            if (doc.RootElement.TryGetProperty("address", out var a)) return a.GetString() ?? "";
        }
        catch { }
        return "";
    }
}
