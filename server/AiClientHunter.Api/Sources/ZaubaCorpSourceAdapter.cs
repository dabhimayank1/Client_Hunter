using System.Diagnostics;
using System.Text.Json;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class ZaubaCorpSourceAdapter(ILogger<ZaubaCorpSourceAdapter> log) : ISourceAdapter
{
    public string Name => "zaubacorp";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        var records = new List<RawRecord>();
        var city = string.IsNullOrWhiteSpace(req.City) ? "Ahmedabad" : req.City;
        var category = string.IsNullOrWhiteSpace(req.Category) ? "Software" : req.Category;

        log.LogInformation("Launching Headless Scraper for Zauba Corp Registered Companies in {City} ({Category}, Limit: {Limit})...", city, category, req.Limit);

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

            var scriptPath = Path.Combine(scraperDir, "scrape-zaubacorp.js");
            if (!File.Exists(scriptPath))
            {
                log.LogWarning("Zauba Corp scraper script not found at {Path}", scriptPath);
                return records;
            }

            var fetchLimit = Math.Clamp(req.Limit * 3, 30, 90);
            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{scriptPath}\" \"{city}\" \"{category}\" {fetchLimit}",
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
                        if (records.Count >= fetchLimit) break;

                        var cin = el.TryGetProperty("cin", out var cinProp) ? cinProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                        var name = el.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var address = el.TryGetProperty("address", out var aProp) ? aProp.GetString() : null;
                        var capital = el.TryGetProperty("capital", out var capProp) ? capProp.GetString() : null;
                        var year = el.TryGetProperty("year", out var yProp) ? yProp.GetString() : null;
                        var status = el.TryGetProperty("status", out var sProp) ? sProp.GetString() ?? "Active" : "Active";
                        var sourceUrl = el.TryGetProperty("sourceUrl", out var srcProp) ? srcProp.GetString() ?? $"https://www.zaubacorp.com/company-list/city-{city}/p-1-company.html" : $"https://www.zaubacorp.com/company-list/city-{city}/p-1-company.html";

                        var rawJson = JsonSerializer.Serialize(new
                        {
                            cin,
                            name,
                            city,
                            category,
                            address,
                            capital,
                            incorporationYear = year,
                            status,
                            source = "zaubacorp",
                            sourceUrl,
                            leadType = "Newly Registered Indian Entity",
                            needsWebsite = true
                        });

                        records.Add(new RawRecord(
                            SourceName: "zaubacorp",
                            SourceId: $"zaubacorp/{cin}",
                            SourceUrl: sourceUrl,
                            Name: name,
                            Category: req.Category,
                            City: city,
                            Phone: null,
                            Website: null, // Newly registered companies need a new website built!
                            RawJson: rawJson,
                            RetrievedAt: DateTime.UtcNow,
                            Email: null
                        ));
                    }
                }
            }

            log.LogInformation("Zauba Corp scraper extracted {Count} companies in {City}.", records.Count, city);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error occurred during Zauba Corp scraping.");
        }

        return records;
    }
}
