using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class GoogleWebSearchAdapter(HttpClient http, ILogger<GoogleWebSearchAdapter> log) : ISourceAdapter
{
    public string Name => "google";

    // Known aggregator & directory domains where businesses have profiles but NOT their own website
    private static readonly HashSet<string> DirectoryDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "justdial.com", "practo.com", "indiamart.com", "sulekha.com", "facebook.com",
        "instagram.com", "linkedin.com", "yellowpages.in", "tradeindia.com", "youtube.com",
        "twitter.com", "x.com", "wikipedia.org", "mapquest.com", "tripadvisor.com"
    };

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        log.LogInformation("Starting Web search without API for {Category} in {City}...", req.Category, req.City);

        var records = new List<RawRecord>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var fetchLimit = Math.Clamp(req.Limit * 3, 30, 90);
        // 1. Primary engine: Headless Chrome Google Maps Scraper (Extracts real contact numbers & physical addresses)
        var searchItems = await FetchHeadlessMapsAsync(req.City, req.Category, fetchLimit, ct);

        // 2. Secondary engine: Supplement with OpenStreetMap (Nominatim) only if Maps found fewer than requested limit
        if (searchItems.Count < req.Limit)
        {
            var osmItems = await FetchNominatimAsync(req.City, req.Category, req.Limit - searchItems.Count, ct);
            searchItems.AddRange(osmItems);
        }

        // 3. Fallback engine: Bing search with strict city verification only if still needed
        if (searchItems.Count < req.Limit)
        {
            var bingResults = await FetchBingAsync(req.City, req.Category, ct);
            searchItems.AddRange(bingResults);
        }

        foreach (var item in searchItems)
        {
            if (records.Count >= fetchLimit) break;

            if (IsAggregatorListing(item.Title, item.DestinationUrl)) continue;

            var cleanName = CleanBusinessName(item.Title, req.City, req.Category);
            if (string.IsNullOrWhiteSpace(cleanName)) continue;

            if (seenNames.Contains(cleanName)) continue;
            seenNames.Add(cleanName);

            // Extract phone number
            var rawPhone = item.Phone ?? ExtractPhone(item.Snippet) ?? ExtractPhone(item.Title);
            var phone = Normalizer.Phone(rawPhone);

            // Extract email
            var rawEmail = item.Email ?? ExtractEmail(item.Snippet);
            var email = Normalizer.Email(rawEmail);

            // Check if destination URL is official website or a directory
            string? officialWebsite = null;
            if (!string.IsNullOrWhiteSpace(item.DestinationUrl))
            {
                var domain = Normalizer.Domain(item.DestinationUrl);
                if (domain != null && !IsDirectoryDomain(domain))
                {
                    officialWebsite = item.DestinationUrl;
                }
            }

            var record = new RawRecord(
                SourceName: "google",
                SourceId: $"web/{Guid.NewGuid():N}",
                SourceUrl: item.DestinationUrl ?? "https://www.google.com",
                Name: cleanName,
                Category: req.Category,
                City: req.City,
                Phone: phone,
                Website: officialWebsite, // null if no own site
                RawJson: JsonSerializer.Serialize(new
                {
                    title = item.Title,
                    snippet = item.Snippet,
                    address = item.Address, // Only set real physical address (e.g. from Nominatim/Maps)
                    url = item.DestinationUrl,
                    city = req.City,
                    category = req.Category
                }),
                RetrievedAt: DateTime.UtcNow,
                Email: email
            );

            records.Add(record);
        }

        log.LogInformation("Web Search returned {Count} businesses for {Category} in {City}.", records.Count, req.Category, req.City);
        return records;
    }

    private async Task<List<SearchItem>> FetchHeadlessMapsAsync(string city, string category, int limit, CancellationToken ct)
    {
        var items = new List<SearchItem>();
        try
        {
            var scraperDir = ScraperPathHelper.GetScraperDirectory();

            var scriptPath = Path.Combine(scraperDir, "scrape-maps.js");
            if (!File.Exists(scriptPath))
            {
                log.LogWarning("Headless scraper script not found at {Path}", scriptPath);
                return items;
            }

            log.LogInformation("Launching Headless Chrome Scraper for {Category} in {City}...", category, city);

            var psi = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"\"{scriptPath}\" \"{city}\" \"{category}\" {Math.Max(limit, 10)}",
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

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(90));
            try
            {
                await proc.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                log.LogWarning("Google Maps scraper timed out after 90 seconds, terminating process.");
                try { proc.Kill(true); } catch { }
            }

            var output = await stdoutTask;
            var err = await stderrTask;
            if (proc.HasExited && (proc.ExitCode != 0 || !string.IsNullOrWhiteSpace(err)))
            {
                log.LogWarning("Google Maps scraper exited with code {Code}. Stderr: {Error}", proc.ExitCode, err);
                if (items.Count == 0 && proc.ExitCode != 0)
                {
                    var cleanErr = string.IsNullOrWhiteSpace(err) ? $"Process exited with code {proc.ExitCode}" : err.Trim();
                    if (cleanErr.Length > 250) cleanErr = cleanErr[..250] + "...";
                    throw new InvalidOperationException($"Google Maps scraper failed: {cleanErr}");
                }
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
                            var name = el.GetProperty("name").GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(name)) continue;

                            var phone = el.TryGetProperty("phone", out var p) ? p.GetString() : null;
                            var address = el.TryGetProperty("address", out var a) ? a.GetString() : null;
                            var hasWebsite = el.TryGetProperty("hasWebsite", out var hw) && hw.GetBoolean();
                            var website = el.TryGetProperty("website", out var w) ? w.GetString() : null;
                            if (hasWebsite && string.IsNullOrWhiteSpace(website))
                            {
                                website = "http://website-listed-on-maps";
                            }

                            items.Add(new SearchItem(
                                Title: name,
                                Snippet: address ?? $"{city}, India",
                                DestinationUrl: website,
                                Phone: phone,
                                Email: null,
                                Address: address
                            ));
                        }
                    }
                }
            }

            log.LogInformation("Headless Chrome Scraper returned {Count} places for {Category} in {City}.", items.Count, category, city);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Headless Chrome scraper could not complete. Continuing with directory extraction.");
            if (ex is InvalidOperationException) throw;
        }

        return items;
    }

    private async Task<List<SearchItem>> FetchNominatimAsync(string city, string category, int limit, CancellationToken ct)
    {
        var items = new List<SearchItem>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var queries = new List<string> { $"{category} in {city}" };
        var lowerCat = category.ToLowerInvariant();
        if (lowerCat.Contains("dent")) queries.Add($"dentist in {city}");
        else if (lowerCat.Contains("clinic") || lowerCat.Contains("doctor")) queries.Add($"hospital in {city}");
        else if (lowerCat.Contains("interior")) queries.Add($"architect in {city}");
        else if (lowerCat.Contains("restaurant")) queries.Add($"cafe in {city}");
        else if (lowerCat.Contains("salon")) queries.Add($"beauty in {city}");
        else if (lowerCat.Contains("gym")) queries.Add($"fitness in {city}");

        foreach (var q in queries)
        {
            if (items.Count >= limit) break;
            try
            {
                var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(q)}&format=json&addressdetails=1&extratags=1&limit={Math.Max(limit, 20)}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "AiClientHunterApp/1.0 (dev@aiclienthunter.local)");
                req.Headers.Add("Accept", "application/json");

                using var res = await http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) continue;

                var json = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in doc.RootElement.EnumerateArray())
                    {
                        string name = "";
                        if (el.TryGetProperty("name", out var nameProp) && !string.IsNullOrWhiteSpace(nameProp.GetString()))
                        {
                            name = nameProp.GetString()!;
                        }

                        string display = el.TryGetProperty("display_name", out var dProp) ? dProp.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            var parts = display.Split(',');
                            name = parts.Length > 0 ? parts[0].Trim() : "";
                        }

                        if (string.IsNullOrWhiteSpace(name) || name.Length < 3) continue;
                        if (seenNames.Contains(name)) continue;
                        seenNames.Add(name);

                        string? website = null;
                        string? phone = null;
                        string? email = null;

                        if (el.TryGetProperty("extratags", out var extras) && extras.ValueKind == JsonValueKind.Object)
                        {
                            if (extras.TryGetProperty("website", out var w)) website = w.GetString();
                            if (extras.TryGetProperty("contact:website", out var cw)) website ??= cw.GetString();
                            if (extras.TryGetProperty("phone", out var p)) phone = p.GetString();
                            if (extras.TryGetProperty("contact:phone", out var cp)) phone ??= cp.GetString();
                            if (extras.TryGetProperty("contact:mobile", out var cm)) phone ??= cm.GetString();
                            if (extras.TryGetProperty("email", out var e)) email = e.GetString();
                            if (extras.TryGetProperty("contact:email", out var ce)) email ??= ce.GetString();
                        }

                        items.Add(new SearchItem(name, display, website, phone, email, Address: display));
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Nominatim local search failed for query: {Query}", q);
            }
        }

        return items;
    }

    private async Task<List<SearchItem>> FetchBingAsync(string city, string category, CancellationToken ct)
    {
        var items = new List<SearchItem>();
        try
        {
            var query = $"{category} in {city} phone contact";
            var url = $"https://www.bing.com/search?q={Uri.EscapeDataString(query)}";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
            req.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return items;

            var html = await res.Content.ReadAsStringAsync(ct);

            var regex = new Regex(@"<li class=""b_algo""[^>]*>[\s\S]*?<h2><a[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)<\/a><\/h2>(?:[\s\S]*?<div class=""b_caption""><p[^>]*>([\s\S]*?)<\/p>)?", RegexOptions.IgnoreCase);
            var matches = regex.Matches(html);

            foreach (Match m in matches)
            {
                var clickUrl = m.Groups[1].Value;
                var rawTitle = m.Groups[2].Value;
                var rawSnippet = m.Groups[3].Success ? m.Groups[3].Value : "";

                var cleanTitle = StripHtml(rawTitle);
                var cleanSnippet = StripHtml(rawSnippet);

                // STRICT CITY VALIDATION: Must mention the target city, otherwise Bing returned other cities!
                if (!cleanTitle.Contains(city, StringComparison.OrdinalIgnoreCase) &&
                    !cleanSnippet.Contains(city, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string? destUrl = DecodeBingUrl(clickUrl);

                if (!string.IsNullOrWhiteSpace(cleanTitle))
                {
                    items.Add(new SearchItem(cleanTitle, cleanSnippet, destUrl));
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Bing web search attempt failed.");
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

        return clickUrl.Contains("bing.com") ? null : clickUrl;
    }

    private async Task<List<SearchItem>> FetchDuckDuckGoAsync(string city, string category, CancellationToken ct)
    {
        var items = new List<SearchItem>();
        var queries = new[]
        {
            $"{category} in {city} phone contact address",
            $"site:justdial.com {category} {city}"
        };

        foreach (var q in queries)
        {
            try
            {
                var url = $"https://html.duckduckgo.com/html/?q={Uri.EscapeDataString(q)}";

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");

                using var res = await http.SendAsync(req, ct);
                if (!res.IsSuccessStatusCode) continue;

                var html = await res.Content.ReadAsStringAsync(ct);

                // Match DuckDuckGo search result blocks
                var regex = new Regex(@"<h2[^>]*class=""result__title""[^>]*>[\s\S]*?<a[^>]*href=""([^""]+)""[^>]*>([\s\S]*?)<\/a>[\s\S]*?<a[^>]*class=""result__snippet""[^>]*>([\s\S]*?)<\/a>", RegexOptions.IgnoreCase);
                var matches = regex.Matches(html);

                foreach (Match m in matches)
                {
                    var rawHref = m.Groups[1].Value;
                    var rawTitle = m.Groups[2].Value;
                    var rawSnippet = m.Groups[3].Value;

                    var cleanTitle = StripHtml(rawTitle);
                    var cleanSnippet = StripHtml(rawSnippet);

                    string? destUrl = null;
                    var matchUddg = Regex.Match(rawHref, @"uddg=([^&]+)");
                    if (matchUddg.Success)
                    {
                        destUrl = WebUtility.UrlDecode(matchUddg.Groups[1].Value);
                    }

                    if (!string.IsNullOrWhiteSpace(cleanTitle))
                    {
                        items.Add(new SearchItem(cleanTitle, cleanSnippet, destUrl));
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "DuckDuckGo search attempt for query '{Query}' failed.", q);
            }
        }

        return items;
    }

    private async Task<List<SearchItem>> FetchGoogleHtmlAsync(string city, string category, CancellationToken ct)
    {
        var items = new List<SearchItem>();
        try
        {
            var query = $"{category} in {city} phone contact";
            var url = $"https://www.google.com/search?q={Uri.EscapeDataString(query)}&num=20";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            req.Headers.Add("Accept-Language", "en-US,en;q=0.9");

            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return items;

            var html = await res.Content.ReadAsStringAsync(ct);

            // Regex for Google search results
            var linkRegex = new Regex(@"<a[^>]*href=""\/url\?q=([^""&]+)[^""]*""[^>]*><h3[^>]*>([\s\S]*?)<\/h3>", RegexOptions.IgnoreCase);
            var matches = linkRegex.Matches(html);

            foreach (Match m in matches)
            {
                var destUrl = WebUtility.UrlDecode(m.Groups[1].Value);
                var title = StripHtml(m.Groups[2].Value);

                if (!string.IsNullOrWhiteSpace(title) && !destUrl.Contains("google.com"))
                {
                    items.Add(new SearchItem(title, title, destUrl));
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Google search attempt failed.");
        }

        return items;
    }

    private static bool IsAggregatorListing(string title, string? url)
    {
        var lowerTitle = title.ToLowerInvariant();
        if (lowerTitle.StartsWith("best ") ||
            lowerTitle.StartsWith("top ") ||
            lowerTitle.StartsWith("find ") ||
            lowerTitle.StartsWith("list of ") ||
            lowerTitle.StartsWith("directory ") ||
            lowerTitle.StartsWith("book appointment") ||
            lowerTitle.StartsWith("explore ") ||
            lowerTitle.StartsWith("how to ") ||
            lowerTitle.StartsWith("guide to ") ||
            lowerTitle.StartsWith("tips for ") ||
            lowerTitle.Contains("top 10") ||
            lowerTitle.Contains("top 20") ||
            lowerTitle.Contains("top best") ||
            lowerTitle.Contains("near me") ||
            lowerTitle.Contains("directory | pdf") ||
            lowerTitle.Contains("yellow pages") ||
            lowerTitle.Contains("ratings & reviews") ||
            lowerTitle.Contains("reviews and ratings") ||
            lowerTitle.Contains("design ideas") ||
            lowerTitle.Contains("ideas with images") ||
            lowerTitle.Contains("ideas from") ||
            lowerTitle.Contains("from our expert") ||
            lowerTitle.Contains("trends for") ||
            lowerTitle.Contains("photo gallery") ||
            lowerTitle.Contains("price list") ||
            lowerTitle.Contains("cost in india") ||
            lowerTitle.Contains("charges in india") ||
            lowerTitle.Contains("rates in india"))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(url))
        {
            var lowerUrl = url.ToLowerInvariant();
            if (lowerUrl.Contains("/category/") ||
                lowerUrl.Contains("/blog/") ||
                lowerUrl.Contains("/article/") ||
                lowerUrl.Contains("/magazine/") ||
                lowerUrl.Contains("/guide/") ||
                lowerUrl.Contains("pdf") ||
                lowerUrl.Contains("scribd.com") ||
                lowerUrl.Contains("quora.com") ||
                lowerUrl.Contains("reddit.com") ||
                lowerUrl.Contains("wikipedia.org"))
            {
                return true;
            }
        }

        return false;
    }

    private static string? CleanBusinessName(string title, string city, string category)
    {
        if (IsAggregatorListing(title, null)) return null;

        var name = title;

        // Strip website domains or paths from title
        if (name.Contains("www.") || name.Contains(".com") || name.Contains(".in") || name.Contains(".org"))
        {
            var parts = name.Split('/', '\\');
            name = parts[0];
        }

        // Split on common SEO separators like " - ", " | ", " : ", " • "
        var separators = new[] { " | ", " - ", " : ", " – ", " — ", " • " };
        foreach (var sep in separators)
        {
            if (name.Contains(sep))
            {
                var tokens = name.Split(sep, StringSplitOptions.RemoveEmptyEntries);
                string? bestToken = null;
                foreach (var t in tokens)
                {
                    var clean = t.Trim();
                    if (clean.Contains("Justdial", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Practo", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("IndiaMART", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Lybrate", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Sulekha", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Top 10", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Top BEST", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Best ", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Near Me", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Book Instant", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Book Appointment", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Appointment", StringComparison.OrdinalIgnoreCase) ||
                        clean.Contains("Contact ", StringComparison.OrdinalIgnoreCase) ||
                        clean.Length < 3)
                    {
                        continue;
                    }

                    // Prefer concise brand names over descriptive sentences
                    if (bestToken == null || (!clean.Contains(" in ", StringComparison.OrdinalIgnoreCase) && bestToken.Contains(" in ", StringComparison.OrdinalIgnoreCase)))
                    {
                        bestToken = clean;
                    }
                }
                if (bestToken != null) name = bestToken;
            }
        }

        // If title still has comma followed by location/city (e.g. "Advanced Clinic, Ctm, Ahmedabad")
        if (name.Contains(','))
        {
            var commaTokens = name.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (commaTokens.Length > 0 && commaTokens[0].Trim().Length >= 4)
            {
                name = commaTokens[0].Trim();
            }
        }

        name = Regex.Replace(name, @"(?i)\b(in\s+" + Regex.Escape(city) + @")\b", "").Trim();
        name = Regex.Replace(name, @"[^\w\s\(\)\&\.\-]", " ").Trim();
        name = Regex.Replace(name, @"\s+", " ").Trim();

        if (name.Length < 4) return null;
        if (name.Equals(category, StringComparison.OrdinalIgnoreCase)) return null;
        if (name.Equals($"Best {category}", StringComparison.OrdinalIgnoreCase)) return null;

        return name;
    }

    private static string? ExtractPhone(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        // Match Indian phone numbers: +91 98..., 098..., 98..., or 079-XXXXXX
        var pattern = @"(?:(?:\+91|0)[ -]?)?[6-9]\d{4}[ -]?\d{5}|\b0\d{2,4}[ -]?\d{6,8}\b";
        var match = Regex.Match(text, pattern);
        return match.Success ? match.Value.Trim() : null;
    }

    private static string? ExtractEmail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var pattern = @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}";
        var match = Regex.Match(text, pattern);
        return match.Success ? match.Value.Trim() : null;
    }

    private static bool IsDirectoryDomain(string domain)
    {
        foreach (var dir in DirectoryDomains)
        {
            if (domain.Equals(dir, StringComparison.OrdinalIgnoreCase) || domain.EndsWith("." + dir, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string StripHtml(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var decoded = WebUtility.HtmlDecode(input);
        return Regex.Replace(decoded, "<[^>]+>", " ").Trim();
    }

    private record SearchItem(string Title, string Snippet, string? DestinationUrl, string? Phone = null, string? Email = null, string? Address = null);
}
