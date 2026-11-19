using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class ZaubaCorpSourceAdapter(HttpClient http, ILogger<ZaubaCorpSourceAdapter> log) : ISourceAdapter
{
    public string Name => "zaubacorp";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        var records = new List<RawRecord>();
        var city = string.IsNullOrWhiteSpace(req.City) ? "Ahmedabad" : req.City;
        var category = string.IsNullOrWhiteSpace(req.Category) ? "Software" : req.Category;
        var fetchLimit = Math.Clamp(req.Limit * 3, 30, 90);

        log.LogInformation("Launching Zauba Corp scraper for registered companies in {City} ({Category}, Limit: {Limit})...", city, category, req.Limit);

        try
        {
            var scraperDir = ScraperPathHelper.GetScraperDirectory();
            var scriptPath = Path.Combine(scraperDir, "scrape-zaubacorp.js");

            if (File.Exists(scriptPath))
            {
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
                    log.LogWarning("Zauba Corp scraper exited with code {Code}. Stderr: {Error}", proc.ExitCode, err);
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
                                    Website: null,
                                    RawJson: rawJson,
                                    RetrievedAt: DateTime.UtcNow,
                                    Email: null
                                ));
                            }
                        }
                    }
                }
            }

            // Cloudflare Datacenter IP Fallback:
            // When hosted on cloud datacenters (like Render), Zauba Corp directly blocks datacenter IPs with Cloudflare 403 / Turnstile challenge.
            // When direct scraper yields 0, query the search engine index for Zauba Corp & MCA registered companies in that city.
            if (records.Count == 0)
            {
                log.LogInformation("Direct Zauba Corp request returned 0 (Cloudflare challenge active on datacenter IP). Engaging Search Engine MCA Fallback for {City}...", city);
                var fallbackRecords = await FetchZaubaViaSearchEngineAsync(city, category, fetchLimit, ct);
                records.AddRange(fallbackRecords);
            }

            log.LogInformation("Zauba Corp adapter successfully loaded {Count} companies in {City}.", records.Count, city);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Error occurred during Zauba Corp scraping.");
            if (ex is InvalidOperationException) throw;
        }

        return records;
    }

    private async Task<List<RawRecord>> FetchZaubaViaSearchEngineAsync(string city, string category, int limit, CancellationToken ct)
    {
        var items = new List<RawRecord>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var queries = new[]
        {
            $"site:zaubacorp.com/company {city} {category}",
            $"site:thecompanycheck.com {city} {category} \"Private Limited\"",
            $"\"Private Limited\" {city} {category} MCA registration"
        };

        foreach (var q in queries)
        {
            if (items.Count >= limit) break;

            try
            {
                var url = $"https://www.bing.com/search?q={Uri.EscapeDataString(q)}";
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
                req.Headers.Add("Accept-Language", "en-US,en;q=0.9");

                using var res = await http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) continue;

                var html = await res.Content.ReadAsStringAsync(ct);
                var regex = new Regex(@"<li class=""b_algo""[^>]*>[\s\S]*?<h2><a[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)<\/a><\/h2>(?:[\s\S]*?<div class=""b_caption""><p[^>]*>([\s\S]*?)<\/p>)?", RegexOptions.IgnoreCase);
                var matches = regex.Matches(html);

                foreach (Match m in matches)
                {
                    if (items.Count >= limit) break;

                    var clickUrl = m.Groups[1].Value;
                    var rawTitle = m.Groups[2].Value;
                    var rawSnippet = m.Groups[3].Success ? m.Groups[3].Value : "";

                    var destUrl = DecodeBingUrl(clickUrl) ?? clickUrl;
                    var cleanTitle = StripHtml(rawTitle);
                    var cleanSnippet = StripHtml(rawSnippet);

                    // Extract Clean Company Name (remove " - Company, directors and ...", " | Zauba Corp", etc.)
                    var nameParts = cleanTitle.Split(new[] { " - ", " | ", " – " }, StringSplitOptions.RemoveEmptyEntries);
                    var companyName = nameParts.Length > 0 ? nameParts[0].Trim() : cleanTitle;

                    // Exclude generic search directories / portals
                    if (companyName.Contains("Company List", StringComparison.OrdinalIgnoreCase) ||
                        companyName.Contains("Search Companies", StringComparison.OrdinalIgnoreCase) ||
                        companyName.Contains("Zauba Corp", StringComparison.OrdinalIgnoreCase) ||
                        companyName.Length < 4)
                    {
                        continue;
                    }

                    if (seenNames.Contains(companyName)) continue;
                    seenNames.Add(companyName);

                    // Extract Indian CIN (Corporate Identification Number: 21 digits, e.g. U72900GJ2022PTC134567)
                    var cinMatch = Regex.Match(destUrl + " " + cleanSnippet, @"\b([UL]\d{5}[A-Z]{2}\d{4}[A-Z]{3}\d{6})\b", RegexOptions.IgnoreCase);
                    var cin = cinMatch.Success ? cinMatch.Groups[1].Value.ToUpperInvariant() : Guid.NewGuid().ToString("N");

                    // Extract year of incorporation
                    var yearMatch = Regex.Match(cin, @"[A-Z]{2}(\d{4})[A-Z]{3}", RegexOptions.IgnoreCase);
                    var year = yearMatch.Success ? yearMatch.Groups[1].Value : null;

                    var rawJson = JsonSerializer.Serialize(new
                    {
                        cin,
                        name = companyName,
                        city,
                        category,
                        snippet = cleanSnippet,
                        sourceUrl = destUrl,
                        incorporationYear = year,
                        source = "zaubacorp",
                        leadType = "Newly Registered Indian Entity",
                        needsWebsite = true
                    });

                    items.Add(new RawRecord(
                        SourceName: "zaubacorp",
                        SourceId: $"zaubacorp/{cin}",
                        SourceUrl: destUrl,
                        Name: companyName,
                        Category: category,
                        City: city,
                        Phone: null,
                        Website: null,
                        RawJson: rawJson,
                        RetrievedAt: DateTime.UtcNow,
                        Email: null
                    ));
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to fetch Zauba Corp data from search index query '{Query}'", q);
            }
        }

        return items;
    }

    private static string? DecodeBingUrl(string clickUrl)
    {
        try
        {
            var matchU = Regex.Match(clickUrl, @"[?&]u=a1([a-zA-Z0-9_\-\+\/=]+)");
            if (matchU.Success)
            {
                var b64 = matchU.Groups[1].Value.Replace('-', '+').Replace('_', '/');
                switch (b64.Length % 4)
                {
                    case 2: b64 += "=="; break;
                    case 3: b64 += "="; break;
                }
                var bytes = Convert.FromBase64String(b64);
                return System.Text.Encoding.UTF8.GetString(bytes);
            }
        }
        catch { }
        return clickUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? clickUrl : null;
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        return Regex.Replace(input, @"<[^>]+>|&nbsp;|&amp;|&quot;|&#39;", " ").Trim();
    }
}
