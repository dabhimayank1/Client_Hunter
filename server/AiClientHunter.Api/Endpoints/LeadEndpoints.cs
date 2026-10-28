using AiClientHunter.Api.Ai;
using AiClientHunter.Api.Data;
using AiClientHunter.Api.Domain;
using AiClientHunter.Api.Services;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace AiClientHunter.Api.Endpoints;

public static class LeadEndpoints
{
    public static readonly HashSet<string> Statuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "new", "needs_review", "qualified", "draft_ready",
        "approved", "contacted", "replied", "follow_up_due", "won", "lost", "do_not_contact"
    };

    public static void MapLeadEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/leads").RequireAuthorization();

        // 1. GET /api/leads/meta (Dynamic filter options and counts)
        g.MapGet("meta", async (AppDbContext db) =>
        {
            var cities = await db.Leads
                .Where(l => !string.IsNullOrEmpty(l.City))
                .Select(l => l.City!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            var categories = await db.Leads
                .Where(l => !string.IsNullOrEmpty(l.Category))
                .Select(l => l.Category!)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            var platforms = await db.SourceRecords
                .Select(s => s.SourceName.ToLower())
                .Distinct()
                .OrderBy(p => p)
                .ToListAsync();

            var todayUtc = DateTime.UtcNow.Date;
            var todayCount = await db.Leads.CountAsync(l => l.CreatedAt >= todayUtc);

            return Results.Ok(new { cities, categories, platforms, todayCount });
        });

        // 2. GET /api/leads (Filterable by Date, City, Category, Platform, Priority, Status, Sortable, Paginated)
        g.MapGet("", async (AppDbContext db, string? priority, string? status, string? city, string? category, string? platform, string? dateRange, string? sortBy, string? q, int page = 1, int pageSize = 25) =>
        {
            if (page < 1) page = 1;
            if (pageSize is < 1 or > 200) pageSize = 25;

            var query = db.Leads.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(priority))
                query = query.Where(l => l.LeadType == priority);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(l => l.Status == status);

            if (!string.IsNullOrWhiteSpace(city))
            {
                var c = city.Trim().ToLowerInvariant();
                query = query.Where(l => l.City != null && l.City.ToLower() == c);
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim().ToLowerInvariant();
                query = query.Where(l => l.Category != null && l.Category.ToLower() == cat);
            }

            if (!string.IsNullOrWhiteSpace(platform))
            {
                var p = platform.Trim().ToLowerInvariant();
                if (p == "freelancer" || p == "upwork" || p == "reddit" || p == "linkedin")
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == p)
                                          || (l.DedupeKey != null && l.DedupeKey.ToLower().StartsWith(p + "|")));
                }
                else if (p == "zaubacorp")
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == "zaubacorp")
                                          || (l.DedupeKey != null && l.DedupeKey.ToLower().StartsWith("zaubacorp|")));
                }
                else if (p == "google")
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == "google")
                                          || (!l.Evidence.Any(e => e.SourceRecord != null && (e.SourceRecord.SourceName.ToLower() == "justdial"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "zaubacorp"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "freelancer"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "upwork"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "reddit"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "linkedin"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "osm"))
                                              && (l.DedupeKey == null || (!l.DedupeKey.ToLower().StartsWith("freelancer|")
                                                                       && !l.DedupeKey.ToLower().StartsWith("upwork|")
                                                                       && !l.DedupeKey.ToLower().StartsWith("reddit|")
                                                                       && !l.DedupeKey.ToLower().StartsWith("linkedin|")
                                                                       && !l.DedupeKey.ToLower().StartsWith("zaubacorp|")))));
                }
                else if (p == "osm")
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == "osm"));
                }
                else
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == p));
                }
            }

            if (!string.IsNullOrWhiteSpace(dateRange))
            {
                var nowUtc = DateTime.UtcNow;
                if (dateRange.Equals("today", StringComparison.OrdinalIgnoreCase))
                {
                    var todayUtc = nowUtc.Date;
                    query = query.Where(l => l.CreatedAt >= todayUtc);
                }
                else if (dateRange.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
                {
                    var yesterdayStart = nowUtc.Date.AddDays(-1);
                    var todayStart = nowUtc.Date;
                    query = query.Where(l => l.CreatedAt >= yesterdayStart && l.CreatedAt < todayStart);
                }
                else if (dateRange.Equals("week", StringComparison.OrdinalIgnoreCase))
                {
                    var weekStart = nowUtc.Date.AddDays(-7);
                    query = query.Where(l => l.CreatedAt >= weekStart);
                }
            }

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                query = query.Where(l => l.Name.Contains(term) || (l.City != null && l.City.Contains(term)) || (l.Category != null && l.Category.Contains(term)));
            }

            var total = await query.CountAsync();

            // Default to newest first so freshly fetched data is immediately visible at the top!
            var sort = sortBy?.Trim().ToLowerInvariant() ?? "newest";
            IQueryable<Lead> orderedQuery = sort switch
            {
                "score" => query.OrderByDescending(l => l.Score).ThenByDescending(l => l.CreatedAt),
                "oldest" => query.OrderBy(l => l.CreatedAt),
                "name" => query.OrderBy(l => l.Name),
                _ => query.OrderByDescending(l => l.CreatedAt) // "newest" default
            };

            var items = await orderedQuery
                                   .Include(l => l.Evidence)
                                   .ThenInclude(e => e.SourceRecord)
                                   .Skip((page - 1) * pageSize)
                                   .Take(pageSize)
                                   .ToListAsync();

            var projectedItems = items.Select(l =>
            {
                var p = l.Evidence.FirstOrDefault()?.SourceRecord?.SourceName?.ToLowerInvariant();
                if (string.IsNullOrEmpty(p))
                {
                    if (l.DedupeKey?.StartsWith("freelancer|", StringComparison.OrdinalIgnoreCase) == true) p = "freelancer";
                    else if (l.DedupeKey?.StartsWith("upwork|", StringComparison.OrdinalIgnoreCase) == true) p = "upwork";
                    else if (l.DedupeKey?.StartsWith("reddit|", StringComparison.OrdinalIgnoreCase) == true) p = "reddit";
                    else if (l.DedupeKey?.StartsWith("linkedin|", StringComparison.OrdinalIgnoreCase) == true) p = "linkedin";
                    else if (l.DedupeKey?.StartsWith("zaubacorp|", StringComparison.OrdinalIgnoreCase) == true) p = "zaubacorp";
                    else p = "google";
                }

                return new
                {
                    l.Id,
                    l.Name,
                    l.Category,
                    l.City,
                    l.State,
                    l.Country,
                    l.WebsiteUrl,
                    l.WebsiteStatus,
                    l.WebsiteCheckedAt,
                    l.Email,
                    l.Phone,
                    l.LeadType,
                    l.Score,
                    l.Status,
                    l.DedupeKey,
                    l.FollowUpDueAt,
                    CreatedAt = DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc),
                    UpdatedAt = DateTime.SpecifyKind(l.UpdatedAt, DateTimeKind.Utc),
                    Platform = p
                };
            });

            return Results.Ok(new { total, page, pageSize, items = projectedItems });
        });

        // 3. GET /api/leads/export (CSV export with UTF-8 BOM for Excel support)
        g.MapGet("export", async (AppDbContext db, string? priority, string? status, string? city, string? category, string? platform, string? dateRange, string? sortBy) =>
        {
            var query = db.Leads.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(priority))
                query = query.Where(l => l.LeadType == priority);

            if (!string.IsNullOrWhiteSpace(status))
                query = query.Where(l => l.Status == status);

            if (!string.IsNullOrWhiteSpace(city))
            {
                var c = city.Trim().ToLowerInvariant();
                query = query.Where(l => l.City != null && l.City.ToLower() == c);
            }

            if (!string.IsNullOrWhiteSpace(category))
            {
                var cat = category.Trim().ToLowerInvariant();
                query = query.Where(l => l.Category != null && l.Category.ToLower() == cat);
            }

            if (!string.IsNullOrWhiteSpace(platform))
            {
                var p = platform.Trim().ToLowerInvariant();
                if (p == "freelancer" || p == "upwork" || p == "reddit" || p == "linkedin")
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == p)
                                          || (l.DedupeKey != null && l.DedupeKey.ToLower().StartsWith(p + "|")));
                }
                else if (p == "zaubacorp")
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == "zaubacorp")
                                          || (l.DedupeKey != null && l.DedupeKey.ToLower().StartsWith("zaubacorp|")));
                }
                else if (p == "google")
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == "google")
                                          || (!l.Evidence.Any(e => e.SourceRecord != null && (e.SourceRecord.SourceName.ToLower() == "justdial"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "zaubacorp"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "freelancer"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "upwork"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "reddit"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "linkedin"
                                                                                           || e.SourceRecord.SourceName.ToLower() == "osm"))
                                              && (l.DedupeKey == null || (!l.DedupeKey.ToLower().StartsWith("freelancer|")
                                                                       && !l.DedupeKey.ToLower().StartsWith("upwork|")
                                                                       && !l.DedupeKey.ToLower().StartsWith("reddit|")
                                                                       && !l.DedupeKey.ToLower().StartsWith("linkedin|")
                                                                       && !l.DedupeKey.ToLower().StartsWith("zaubacorp|")))));
                }
                else if (p == "osm")
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == "osm"));
                }
                else
                {
                    query = query.Where(l => l.Evidence.Any(e => e.SourceRecord != null && e.SourceRecord.SourceName.ToLower() == p));
                }
            }

            if (!string.IsNullOrWhiteSpace(dateRange))
            {
                var nowUtc = DateTime.UtcNow;
                if (dateRange.Equals("today", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => l.CreatedAt >= nowUtc.Date);
                else if (dateRange.Equals("yesterday", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => l.CreatedAt >= nowUtc.Date.AddDays(-1) && l.CreatedAt < nowUtc.Date);
                else if (dateRange.Equals("week", StringComparison.OrdinalIgnoreCase))
                    query = query.Where(l => l.CreatedAt >= nowUtc.Date.AddDays(-7));
            }

            var sort = sortBy?.Trim().ToLowerInvariant() ?? "newest";
            IQueryable<Lead> orderedQuery = sort switch
            {
                "score" => query.OrderByDescending(l => l.Score).ThenByDescending(l => l.CreatedAt),
                "oldest" => query.OrderBy(l => l.CreatedAt),
                "name" => query.OrderBy(l => l.Name),
                _ => query.OrderByDescending(l => l.CreatedAt)
            };

            var rawRows = await orderedQuery
                .Include(l => l.Evidence)
                .ThenInclude(e => e.SourceRecord)
                .ToListAsync();

            var rows = rawRows.Select(l =>
            {
                var p = l.Evidence.FirstOrDefault()?.SourceRecord?.SourceName?.ToLowerInvariant();
                if (string.IsNullOrEmpty(p))
                {
                    if (l.DedupeKey?.StartsWith("freelancer|", StringComparison.OrdinalIgnoreCase) == true) p = "freelancer";
                    else if (l.DedupeKey?.StartsWith("upwork|", StringComparison.OrdinalIgnoreCase) == true) p = "upwork";
                    else if (l.DedupeKey?.StartsWith("reddit|", StringComparison.OrdinalIgnoreCase) == true) p = "reddit";
                    else if (l.DedupeKey?.StartsWith("linkedin|", StringComparison.OrdinalIgnoreCase) == true) p = "linkedin";
                    else if (l.DedupeKey?.StartsWith("zaubacorp|", StringComparison.OrdinalIgnoreCase) == true) p = "zaubacorp";
                    else p = "google";
                }

                var istTime = Normalizer.ToIndianTime(l.CreatedAt);
                var formattedPhone = Normalizer.FormatPhoneForDisplay(l.Phone);

                return new
                {
                    l.Id,
                    Platform = p?.ToUpperInvariant() ?? "GOOGLE",
                    l.Name,
                    l.Category,
                    l.City,
                    l.State,
                    l.Email,
                    Phone = formattedPhone ?? "",
                    l.WebsiteUrl,
                    l.WebsiteStatus,
                    l.LeadType,
                    l.Score,
                    l.Status,
                    CreatedAt = istTime.ToString("dd-MM-yyyy hh:mm tt")
                };
            }).ToList();

            using var sw = new StringWriter();
            using (var csv = new CsvHelper.CsvWriter(sw, CultureInfo.InvariantCulture))
            {
                csv.WriteRecords(rows);
            }

            var bom = new UTF8Encoding(true).GetPreamble();
            var bytes = bom.Concat(Encoding.UTF8.GetBytes(sw.ToString())).ToArray();

            return Results.File(bytes, "text/csv", "leads.csv");
        });

        // 3. GET /api/leads/{id:long} (Single Lead with Evidence & Drafts)
        g.MapGet("{id:long}", async (long id, AppDbContext db) =>
        {
            var lead = await db.Leads
                .Include(l => l.Evidence)
                .ThenInclude(e => e.SourceRecord)
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.Id == id);

            if (lead is null) return Results.NotFound();

            var drafts = await db.MessageDrafts
                .Where(m => m.LeadId == id)
                .OrderByDescending(m => m.Version)
                .AsNoTracking()
                .ToListAsync();

            var scores = await db.LeadScores
                .Where(s => s.LeadId == id)
                .OrderByDescending(s => s.CreatedAt)
                .AsNoTracking()
                .ToListAsync();

            return Results.Ok(new
            {
                lead,
                drafts,
                scores
            });
        });

        // 4. POST /api/leads (Manual lead creation with provenance)
        g.MapPost("", async (CreateManualLeadRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { error = "Lead name is required" });

            var dedupeKey = !string.IsNullOrWhiteSpace(req.WebsiteUrl)
                ? req.WebsiteUrl.ToLowerInvariant().Replace("https://", "").Replace("http://", "").TrimEnd('/')
                : $"{req.Name.ToLowerInvariant().Trim()}|{(req.City ?? "").ToLowerInvariant().Trim()}";

            var lead = new Lead
            {
                Name = req.Name.Trim(),
                Category = req.Category?.Trim(),
                City = req.City?.Trim(),
                State = req.State?.Trim(),
                Country = req.Country?.Trim() ?? "India",
                Email = req.Email?.Trim(),
                Phone = req.Phone?.Trim(),
                WebsiteUrl = req.WebsiteUrl?.Trim(),
                WebsiteStatus = !string.IsNullOrWhiteSpace(req.WebsiteUrl) ? "verified" : "unknown",
                LeadType = !string.IsNullOrWhiteSpace(req.LeadType) ? req.LeadType.ToLowerInvariant() : "warm",
                Score = req.Score > 0 ? req.Score : (req.LeadType == "hot" ? 60 : 30),
                Status = "new",
                DedupeKey = dedupeKey,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Leads.Add(lead);
            await db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(req.SourceNote))
            {
                var manualSource = new SourceRecord
                {
                    SourceName = "manual",
                    SourceId = $"manual/{lead.Id}_{Guid.NewGuid():N}",
                    SourceUrl = "local://manual-entry",
                    RawJson = "{}"
                };
                db.SourceRecords.Add(manualSource);
                await db.SaveChangesAsync();

                var evidence = new LeadEvidence
                {
                    LeadId = lead.Id,
                    SourceRecordId = manualSource.Id,
                    Note = req.SourceNote.Trim(),
                    CreatedAt = DateTime.UtcNow
                };
                db.EvidenceLinks.Add(evidence);
                await db.SaveChangesAsync();
            }

            return Results.Created($"/api/leads/{lead.Id}", lead);
        });

        // 5. PATCH /api/leads/{id:long}/status (Status transitions)
        g.MapPatch("{id:long}/status", async (long id, StatusUpdate body, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(body.Status) || !Statuses.Contains(body.Status.Trim()))
                return Results.BadRequest(new { error = $"Unknown status. Valid statuses: {string.Join(", ", Statuses)}" });

            var lead = await db.Leads.FindAsync(id);
            if (lead is null) return Results.NotFound();

            lead.Status = body.Status.Trim().ToLowerInvariant();
            lead.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync();
            return Results.Ok(lead);
        });

        // 6. POST /api/leads/{id:long}/do-not-contact (Suppression)
        g.MapPost("{id:long}/do-not-contact", async (long id, AppDbContext db) =>
        {
            var lead = await db.Leads.FindAsync(id);
            if (lead is null) return Results.NotFound();

            lead.Status = "do_not_contact";
            lead.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(lead.Phone))
            {
                var phoneVal = lead.Phone.Trim();
                if (!await db.DoNotContactList.AnyAsync(d => d.Value == phoneVal))
                {
                    db.DoNotContactList.Add(new DoNotContact
                    {
                        Value = phoneVal,
                        Kind = "phone",
                        Reason = $"Marked from lead #{lead.Id} ({lead.Name})"
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(lead.Email))
            {
                var emailVal = lead.Email.Trim().ToLowerInvariant();
                if (!await db.DoNotContactList.AnyAsync(d => d.Value == emailVal))
                {
                    db.DoNotContactList.Add(new DoNotContact
                    {
                        Value = emailVal,
                        Kind = "email",
                        Reason = $"Marked from lead #{lead.Id} ({lead.Name})"
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(lead.WebsiteUrl))
            {
                var domainVal = lead.WebsiteUrl.Trim();
                if (!await db.DoNotContactList.AnyAsync(d => d.Value == domainVal))
                {
                    db.DoNotContactList.Add(new DoNotContact
                    {
                        Value = domainVal,
                        Kind = "domain",
                        Reason = $"Marked from lead #{lead.Id} ({lead.Name})"
                    });
                }
            }

            await db.SaveChangesAsync();
            return Results.Ok(new { message = $"Lead #{id} marked as do_not_contact and suppressed from future outreach", lead });
        });

        // 7. POST /api/leads/{id:long}/analyze (Phase 7: AI Analysis & Outreach Draft)
        g.MapPost("{id:long}/analyze", async (long id, AppDbContext db, IAiProvider ai, CancellationToken ct) =>
        {
            var lead = await db.Leads
                .Include(l => l.Evidence)
                .ThenInclude(e => e.SourceRecord)
                .FirstOrDefaultAsync(l => l.Id == id, ct);

            if (lead is null) return Results.NotFound();

            if (lead.Status == "do_not_contact")
                return Results.BadRequest(new { error = "Lead is on the do-not-contact suppression list. Outreach generation is blocked." });

            var evidenceNotes = lead.Evidence.Select(e => e.Note).Where(n => !string.IsNullOrWhiteSpace(n));
            var evidenceText =
                $"Business: {lead.Name}\n" +
                $"Category: {lead.Category ?? "general"}\n" +
                $"City: {lead.City ?? "unknown"}, {lead.State ?? "Gujarat"}\n" +
                $"Website status: {lead.WebsiteStatus} (URL: {lead.WebsiteUrl ?? "none"})\n" +
                $"Email available: {(!string.IsNullOrWhiteSpace(lead.Email) ? "yes (" + lead.Email + ")" : "no")}\n" +
                $"Phone available: {(!string.IsNullOrWhiteSpace(lead.Phone) ? "yes (" + lead.Phone + ")" : "no")}\n" +
                $"Lead source evidence: {string.Join("; ", evidenceNotes)}\n" +
                $"Current lead type: {lead.LeadType}";

            var analysis = await ai.AnalyzeAsync(evidenceText, ct);

            var version = await db.MessageDrafts.Where(m => m.LeadId == lead.Id).CountAsync(ct) + 1;
            var draft = new MessageDraft
            {
                LeadId = lead.Id,
                Version = version,
                Channel = "whatsapp",
                Subject = $"Website Opportunity for {lead.Name}",
                Body = analysis.DraftMessage,
                Status = "draft",
                CreatedAt = DateTime.UtcNow
            };

            db.MessageDrafts.Add(draft);
            lead.Status = "draft_ready";
            lead.UpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);

            return Results.Ok(new
            {
                analysis,
                draft,
                leadStatus = lead.Status
            });
        });
    }
}

public record StatusUpdate(string Status);
public record CreateManualLeadRequest(
    string Name,
    string? Category,
    string? City,
    string? State,
    string? Country,
    string? Phone,
    string? Email,
    string? WebsiteUrl,
    string? LeadType,
    int Score,
    string? SourceNote
);
