using System.Text.Json;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class FreelancerSourceAdapter(HttpClient http, ILogger<FreelancerSourceAdapter> log) : ISourceAdapter
{
    public string Name => "freelancer";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        var records = new List<RawRecord>();
        var rawCat = string.IsNullOrWhiteSpace(req.Category) ? "website" : req.Category.Trim();
        var query = rawCat.Contains('&') ? rawCat.Split('&')[0].Trim() : rawCat;

        log.LogInformation("Fetching live projects from Freelancer.com for query '{Query}' (Limit: {Limit})...", query, req.Limit);

        try
        {
            var url = $"https://www.freelancer.com/api/projects/0.1/projects/active?query={Uri.EscapeDataString(query)}&compact=false&full_description=true&limit={Math.Max(req.Limit, 10)}";

            using var msg = new HttpRequestMessage(HttpMethod.Get, url);
            msg.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            msg.Headers.Add("Accept", "application/json");

            using var res = await http.SendAsync(msg, ct);
            if (!res.IsSuccessStatusCode)
            {
                log.LogWarning("Freelancer API returned HTTP {StatusCode}", res.StatusCode);
                return records;
            }

            var json = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("result", out var resultEl) &&
                resultEl.TryGetProperty("projects", out var projectsEl) &&
                projectsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in projectsEl.EnumerateArray())
                {
                    if (records.Count >= req.Limit) break;

                    var id = p.TryGetProperty("id", out var idProp) ? idProp.GetInt64().ToString() : Guid.NewGuid().ToString("N");
                    var title = p.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    var seoUrl = p.TryGetProperty("seo_url", out var sProp) ? sProp.GetString() : null;
                    var projectUrl = !string.IsNullOrWhiteSpace(seoUrl)
                        ? $"https://www.freelancer.com/projects/{seoUrl}"
                        : $"https://www.freelancer.com/projects/{id}";

                    string desc = "";
                    if (p.TryGetProperty("description", out var fullDesc) && !string.IsNullOrWhiteSpace(fullDesc.GetString()))
                    {
                        desc = fullDesc.GetString()!;
                    }
                    else if (p.TryGetProperty("preview_description", out var prevDesc) && !string.IsNullOrWhiteSpace(prevDesc.GetString()))
                    {
                        desc = prevDesc.GetString()!;
                    }

                    // Extract contact details if client mentioned them inside title or description
                    var extractedEmail = Normalizer.ExtractEmailFromText(desc) ?? Normalizer.ExtractEmailFromText(title);
                    var extractedPhone = Normalizer.ExtractPhoneFromText(desc) ?? Normalizer.ExtractPhoneFromText(title);
                    var extractedWebsite = Normalizer.ExtractUrlFromText(desc);

                    var type = p.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "fixed" : "fixed";

                    string currencySign = "$";
                    string currencyCode = "USD";
                    if (p.TryGetProperty("currency", out var currEl))
                    {
                        if (currEl.TryGetProperty("sign", out var signProp)) currencySign = signProp.GetString() ?? "$";
                        if (currEl.TryGetProperty("code", out var codeProp)) currencyCode = codeProp.GetString() ?? "USD";
                    }

                    double? minBudget = null;
                    double? maxBudget = null;
                    if (p.TryGetProperty("budget", out var bEl))
                    {
                        if (bEl.TryGetProperty("minimum", out var minProp) && minProp.TryGetDouble(out var minVal)) minBudget = minVal;
                        if (bEl.TryGetProperty("maximum", out var maxProp) && maxProp.TryGetDouble(out var maxVal)) maxBudget = maxVal;
                    }

                    string budgetDisplay = minBudget.HasValue && maxBudget.HasValue
                        ? $"{currencySign}{minBudget.Value:N0} - {currencySign}{maxBudget.Value:N0} {currencyCode}"
                        : minBudget.HasValue ? $"{currencySign}{minBudget.Value:N0} {currencyCode}" : "Flexible Budget";

                    int bidCount = 0;
                    if (p.TryGetProperty("bid_stats", out var bStats) && bStats.TryGetProperty("bid_count", out var bcProp))
                    {
                        bidCount = bcProp.GetInt32();
                    }

                    var rawJson = JsonSerializer.Serialize(new
                    {
                        id,
                        title,
                        description = desc,
                        budget = budgetDisplay,
                        budgetMin = minBudget,
                        budgetMax = maxBudget,
                        currency = currencyCode,
                        projectType = type,
                        bids = bidCount,
                        url = projectUrl,
                        source = "freelancer",
                        query,
                        contactMethod = "Direct Freelancer Project Proposal / Bid",
                        extractedEmail,
                        extractedPhone,
                        extractedWebsite
                    });

                    records.Add(new RawRecord(
                        SourceName: "freelancer",
                        SourceId: $"freelancer/{id}",
                        SourceUrl: projectUrl,
                        Name: title,
                        Category: req.Category,
                        City: "Remote",
                        Phone: extractedPhone,
                        Website: extractedWebsite,
                        RawJson: rawJson,
                        RetrievedAt: DateTime.UtcNow,
                        Email: extractedEmail
                    ));
                }
            }

            log.LogInformation("Freelancer.com returned {Count} projects for query '{Query}'.", records.Count, query);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to fetch projects from Freelancer.com");
        }

        return records;
    }
}
