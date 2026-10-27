using System.Diagnostics;
using System.Text.Json;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class LinkedInSourceAdapter(ILogger<LinkedInSourceAdapter> log) : ISourceAdapter
{
    public string Name => "linkedin";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        var records = new List<RawRecord>();
        var rawCat = string.IsNullOrWhiteSpace(req.Category) ? "website developer" : req.Category.Trim();
        var query = rawCat.Contains('&') ? rawCat.Split('&')[0].Trim() : rawCat;

        log.LogInformation("Launching Headless Scraper for LinkedIn hiring posts & jobs with query '{Query}' (Limit: {Limit})...", query, req.Limit);

        try
        {
            var scraperDir = ScraperPathHelper.GetScraperDirectory();

            var scriptPath = Path.Combine(scraperDir, "scrape-linkedin.js");
            if (!File.Exists(scriptPath))
            {
                log.LogWarning("LinkedIn scraper script not found at {Path}", scriptPath);
                return records;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{scriptPath}\" \"{query}\" {Math.Max(req.Limit, 10)}",
                WorkingDirectory = scraperDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.Start();

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);

            await proc.WaitForExitAsync(ct);

            var output = await stdoutTask;
            if (!string.IsNullOrWhiteSpace(output))
            {
                using var doc = JsonDocument.Parse(output);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        if (records.Count >= req.Limit) break;

                        var id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                        var title = el.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(title)) continue;

                        var company = el.TryGetProperty("company", out var cProp) ? cProp.GetString() ?? "Hiring Organization" : "Hiring Organization";
                        var location = el.TryGetProperty("location", out var lProp) ? lProp.GetString() ?? "Remote" : "Remote";
                        var url = el.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "https://www.linkedin.com/jobs" : "https://www.linkedin.com/jobs";
                        var desc = el.TryGetProperty("description", out var dProp) ? dProp.GetString() ?? "" : "";
                        var posted = el.TryGetProperty("posted", out var pProp) ? pProp.GetString() ?? "Recently" : "Recently";
                        var budget = el.TryGetProperty("budget", out var bProp) ? bProp.GetString() ?? "See LinkedIn Posting" : "See LinkedIn Posting";

                        // Extract contact details if client typed any
                        var extractedEmail = Normalizer.ExtractEmailFromText(desc) ?? Normalizer.ExtractEmailFromText(title);
                        var extractedPhone = Normalizer.ExtractPhoneFromText(desc) ?? Normalizer.ExtractPhoneFromText(title);
                        var extractedWebsite = Normalizer.ExtractUrlFromText(desc) ?? Normalizer.ExtractUrlFromText(title);

                        var displayName = $"{title} — {company}";

                        var rawJson = JsonSerializer.Serialize(new
                        {
                            id,
                            title,
                            company,
                            location,
                            description = desc,
                            budget,
                            posted,
                            url,
                            source = "linkedin",
                            query,
                            contactMethod = "Direct LinkedIn Application / InMail",
                            extractedEmail,
                            extractedPhone,
                            extractedWebsite
                        });

                        records.Add(new RawRecord(
                            SourceName: "linkedin",
                            SourceId: $"linkedin/{id}",
                            SourceUrl: url,
                            Name: displayName,
                            Category: req.Category,
                            City: location,
                            Phone: extractedPhone,
                            Website: extractedWebsite,
                            RawJson: rawJson,
                            RetrievedAt: DateTime.UtcNow,
                            Email: extractedEmail
                        ));
                    }
                }
            }

            log.LogInformation("LinkedIn Scraper returned {Count} hiring posts for query '{Query}'.", records.Count, query);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to scrape LinkedIn jobs for query '{Query}'", query);
        }

        return records;
    }
}
