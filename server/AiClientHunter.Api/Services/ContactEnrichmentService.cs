using System.Diagnostics;
using System.Text.Json;

namespace AiClientHunter.Api.Services;

public record EnrichedContact(
    string Name,
    string? City,
    string? Phone,
    string? Website,
    string? Email,
    string? Address,
    bool Enriched
);

public class ContactEnrichmentService(ILogger<ContactEnrichmentService> log)
{
    public async Task<IReadOnlyDictionary<string, EnrichedContact>> EnrichBatchAsync(
        IEnumerable<(string Name, string? City)> items,
        CancellationToken ct)
    {
        var results = new Dictionary<string, EnrichedContact>(StringComparer.OrdinalIgnoreCase);
        var itemList = items
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .DistinctBy(x => x.Name.Trim())
            .ToList();

        if (itemList.Count == 0) return results;

        var scraperDir = Sources.ScraperPathHelper.GetScraperDirectory();

        var scriptPath = Path.Combine(scraperDir, "enrich-contact.js");
        if (!File.Exists(scriptPath))
        {
            log.LogWarning("Contact enrichment script not found at {Path}", scriptPath);
            return results;
        }

        var tempFile = Path.Combine(Path.GetTempPath(), $"enrich_{Guid.NewGuid():N}.json");
        try
        {
            var payload = itemList.Select(x => new { name = x.Name.Trim(), city = x.City?.Trim() ?? "" });
            var json = JsonSerializer.Serialize(payload);
            await File.WriteAllTextAsync(tempFile, json, ct);

            log.LogInformation("Enriching {Count} items via Google Search/Maps Headless Scraper...", itemList.Count);

            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{scriptPath}\" --file \"{tempFile}\"",
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
                log.LogWarning("Contact enrichment scraper exited with code {Code}. Stderr: {Error}", proc.ExitCode, err);
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
                            var name = el.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var city = el.TryGetProperty("city", out var cProp) ? cProp.GetString() : null;
                        var phone = el.TryGetProperty("phone", out var pProp) ? pProp.GetString() : null;
                        var website = el.TryGetProperty("website", out var wProp) ? wProp.GetString() : null;
                        var email = el.TryGetProperty("email", out var eProp) ? eProp.GetString() : null;
                        var address = el.TryGetProperty("address", out var aProp) ? aProp.GetString() : null;
                        var enriched = el.TryGetProperty("enriched", out var enProp) && enProp.GetBoolean();

                            results[name.Trim()] = new EnrichedContact(name, city, phone, website, email, address, enriched);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Failed to complete Google contact enrichment batch.");
        }
        finally
        {
            try
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch { }
        }

        return results;
    }
}
