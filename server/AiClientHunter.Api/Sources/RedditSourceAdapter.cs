using System.Diagnostics;
using System.Text.Json;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class RedditSourceAdapter(ILogger<RedditSourceAdapter> log) : ISourceAdapter
{
    public string Name => "reddit";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        var records = new List<RawRecord>();
        var rawCat = string.IsNullOrWhiteSpace(req.Category) ? "website" : req.Category.Trim();
        var query = rawCat.Contains('&') ? rawCat.Split('&')[0].Trim() : rawCat;

        log.LogInformation("Launching Headless Scraper for Reddit [Hiring] posts with query '{Query}' (Limit: {Limit})...", query, req.Limit);

        try
        {
            var scraperDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scraper"));
            if (!Directory.Exists(scraperDir))
            {
                scraperDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../scraper"));
            }
            if (!Directory.Exists(scraperDir))
            {
                scraperDir = @"c:\Users\DELL\Desktop\client hunter\scraper";
            }

            var scriptPath = Path.Combine(scraperDir, "scrape-reddit.js");
            if (!File.Exists(scriptPath))
            {
                log.LogWarning("Reddit scraper script not found at {Path}", scriptPath);
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

                        var url = el.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "https://www.reddit.com/r/forhire" : "https://www.reddit.com/r/forhire";
                        var author = el.TryGetProperty("author", out var aProp) ? aProp.GetString() ?? "Reddit Client" : "Reddit Client";
                        var desc = el.TryGetProperty("description", out var dProp) ? dProp.GetString() ?? "" : "";
                        var budget = el.TryGetProperty("budget", out var bProp) ? bProp.GetString() ?? "Negotiable" : "Negotiable";
                        var posted = el.TryGetProperty("posted", out var pProp) ? pProp.GetString() ?? "Recently" : "Recently";
                        var subreddit = el.TryGetProperty("subreddit", out var sProp) ? sProp.GetString() ?? "r/forhire" : "r/forhire";

                        // Extract contact details if client typed any in title/description
                        var extractedEmail = Normalizer.ExtractEmailFromText(desc) ?? Normalizer.ExtractEmailFromText(title);
                        var extractedPhone = Normalizer.ExtractPhoneFromText(desc) ?? Normalizer.ExtractPhoneFromText(title);
                        var extractedWebsite = Normalizer.ExtractUrlFromText(desc) ?? Normalizer.ExtractUrlFromText(title);

                        var rawJson = JsonSerializer.Serialize(new
                        {
                            id,
                            title,
                            description = desc,
                            author,
                            budget,
                            posted,
                            subreddit,
                            url,
                            source = "reddit",
                            query,
                            contactMethod = "Direct Reddit Post / DM",
                            extractedEmail,
                            extractedPhone,
                            extractedWebsite
                        });

                        records.Add(new RawRecord(
                            SourceName: "reddit",
                            SourceId: $"reddit/{id}",
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

            log.LogInformation("Reddit Scraper returned {Count} hiring posts for query '{Query}'.", records.Count, query);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to scrape Reddit posts for query '{Query}'", query);
        }

        return records;
    }
}
