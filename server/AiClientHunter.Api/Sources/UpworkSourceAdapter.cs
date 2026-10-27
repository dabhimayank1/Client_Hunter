using System.Diagnostics;
using System.Text.Json;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class UpworkSourceAdapter(ILogger<UpworkSourceAdapter> log) : ISourceAdapter
{
    public string Name => "upwork";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        var records = new List<RawRecord>();
        var rawCat = string.IsNullOrWhiteSpace(req.Category) ? "website developer" : req.Category.Trim();
        var query = rawCat.Contains('&') ? rawCat.Split('&')[0].Trim() : rawCat;

        log.LogInformation("Launching Headless Scraper for Upwork jobs with query '{Query}' (Limit: {Limit})...", query, req.Limit);

        try
        {
            var scraperDir = ScraperPathHelper.GetScraperDirectory();

            var scriptPath = Path.Combine(scraperDir, "scrape-upwork.js");
            if (!File.Exists(scriptPath))
            {
                log.LogWarning("Upwork scraper script not found at {Path}", scriptPath);
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

                        var url = el.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "https://www.upwork.com" : "https://www.upwork.com";
                        var desc = el.TryGetProperty("description", out var dProp) ? dProp.GetString() ?? "" : "";
                        var budget = el.TryGetProperty("budget", out var bProp) ? bProp.GetString() ?? "Not specified" : "Not specified";
                        var jobType = el.TryGetProperty("jobType", out var jtProp) ? jtProp.GetString() ?? "Contract" : "Contract";
                        var posted = el.TryGetProperty("posted", out var pProp) ? pProp.GetString() ?? "Recently" : "Recently";

                        // Extract contact details if client mentioned them inside title or description
                        var extractedEmail = Normalizer.ExtractEmailFromText(desc) ?? Normalizer.ExtractEmailFromText(title);
                        var extractedPhone = Normalizer.ExtractPhoneFromText(desc) ?? Normalizer.ExtractPhoneFromText(title);
                        var extractedWebsite = Normalizer.ExtractUrlFromText(desc);

                        var rawJson = JsonSerializer.Serialize(new
                        {
                            id,
                            title,
                            description = desc,
                            budget,
                            jobType,
                            posted,
                            url,
                            source = "upwork",
                            query,
                            contactMethod = "Direct Upwork Job Proposal",
                            extractedEmail,
                            extractedPhone,
                            extractedWebsite
                        });

                        records.Add(new RawRecord(
                            SourceName: "upwork",
                            SourceId: $"upwork/{id}",
                            SourceUrl: url,
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
            }

            log.LogInformation("Upwork Scraper returned {Count} jobs for query '{Query}'.", records.Count, query);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to scrape Upwork jobs for query '{Query}'", query);
        }

        return records;
    }
}
