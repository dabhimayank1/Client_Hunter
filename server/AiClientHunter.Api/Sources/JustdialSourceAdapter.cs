using System.Diagnostics;
using System.Text.Json;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class JustdialSourceAdapter(ILogger<JustdialSourceAdapter> log) : ISourceAdapter
{
    public string Name => "justdial";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        var records = new List<RawRecord>();
        var city = string.IsNullOrWhiteSpace(req.City) ? "Ahmedabad" : req.City;
        var category = string.IsNullOrWhiteSpace(req.Category) ? "Dental Clinics" : req.Category;

        log.LogInformation("Launching Headless Scraper for Justdial Indian Businesses ({Category} in {City}, Limit: {Limit})...", category, city, req.Limit);

        try
        {
            var scraperDir = ScraperPathHelper.GetScraperDirectory();

            var scriptPath = Path.Combine(scraperDir, "scrape-justdial.js");
            if (!File.Exists(scriptPath))
            {
                log.LogWarning("Justdial scraper script not found at {Path}", scriptPath);
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
            var err = await stderrTask;

            if (proc.ExitCode != 0 || !string.IsNullOrWhiteSpace(err))
            {
                log.LogWarning("Justdial scraper exited with code {Code}. Stderr: {Error}", proc.ExitCode, err);
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                var startIndex = output.IndexOf('[');
                var endIndex = output.LastIndexOf(']');
                if (startIndex >= 0 && endIndex > startIndex)
                {
                    var jsonArray = output.Substring(startIndex, endIndex - startIndex + 1);
                    using var doc = JsonDocument.Parse(jsonArray);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var el in doc.RootElement.EnumerateArray())
                        {
                            if (records.Count >= fetchLimit) break;

                            var id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                            var name = el.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            var phone = el.TryGetProperty("phone", out var pProp) ? pProp.GetString() : null;
                            var address = el.TryGetProperty("address", out var aProp) ? aProp.GetString() : null;
                            var rating = el.TryGetProperty("rating", out var rProp) ? rProp.GetString() : null;
                            var reviews = el.TryGetProperty("reviews", out var revProp) ? revProp.GetString() : null;
                            var sourceUrl = el.TryGetProperty("sourceUrl", out var sProp) ? sProp.GetString() ?? $"https://www.justdial.com/{city}/{category}" : $"https://www.justdial.com/{city}/{category}";
                            var website = el.TryGetProperty("website", out var wProp) && wProp.ValueKind == JsonValueKind.String ? wProp.GetString() : null;

                            var normalizedPhone = Normalizer.Phone(phone);

                        var rawJson = JsonSerializer.Serialize(new
                        {
                            id,
                            name,
                            city,
                            category,
                            phone = normalizedPhone,
                            address,
                            rating,
                            reviews,
                            website,
                            source = "justdial",
                            sourceUrl,
                            leadType = "Local Indian Business",
                            needsWebsite = string.IsNullOrWhiteSpace(website)
                        });

                        records.Add(new RawRecord(
                            SourceName: "justdial",
                            SourceId: $"justdial/{id}",
                            SourceUrl: sourceUrl,
                            Name: name,
                            Category: req.Category,
                            City: city,
                            Phone: normalizedPhone,
                            Website: website,
                            RawJson: rawJson,
                            RetrievedAt: DateTime.UtcNow,
                            Email: null
                        ));
                        }
                    }
                }
            }

            log.LogInformation("Justdial scraper extracted {Count} local businesses for {Category} in {City}.", records.Count, category, city);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error occurred during Justdial scraping.");
            if (ex is InvalidOperationException) throw;
        }

        return records;
    }
}
