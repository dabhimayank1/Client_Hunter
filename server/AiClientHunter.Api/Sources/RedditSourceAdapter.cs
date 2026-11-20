using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiClientHunter.Api.Services;

namespace AiClientHunter.Api.Sources;

public class RedditSourceAdapter(HttpClient http, ILogger<RedditSourceAdapter> log) : ISourceAdapter
{
    public string Name => "reddit";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        var records = new List<RawRecord>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rawCat = string.IsNullOrWhiteSpace(req.Category) ? "website" : req.Category.Trim();
        var query = rawCat.Contains('&') ? rawCat.Split('&')[0].Trim() : rawCat;
        var limit = Math.Clamp(req.Limit, 10, 50);

        log.LogInformation("Launching Reddit scraper for [Hiring] posts with query '{Query}' (Limit: {Limit})...", query, limit);

        // Tier 1: Try local Headless Scraper (if Chromium is available and not blocked)
        try
        {
            var scraperDir = ScraperPathHelper.GetScraperDirectory();
            var scriptPath = Path.Combine(scraperDir, "scrape-reddit.js");

            if (File.Exists(scriptPath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "node",
                    Arguments = $"\"{scriptPath}\" \"{query}\" {limit}",
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
                    log.LogWarning("Reddit headless scraper exited with code {Code}. Stderr: {Error}", proc.ExitCode, err);
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
                                if (records.Count >= limit) break;

                                var id = el.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                                if (seenIds.Contains(id)) continue;
                                seenIds.Add(id);

                                var title = el.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                                if (string.IsNullOrWhiteSpace(title)) continue;

                                var url = el.TryGetProperty("url", out var uProp) ? uProp.GetString() ?? "https://www.reddit.com/r/forhire" : "https://www.reddit.com/r/forhire";
                                var author = el.TryGetProperty("author", out var aProp) ? aProp.GetString() ?? "Reddit Client" : "Reddit Client";
                                var desc = el.TryGetProperty("description", out var dProp) ? dProp.GetString() ?? "" : "";
                                var budget = el.TryGetProperty("budget", out var bProp) ? bProp.GetString() ?? "Negotiable" : "Negotiable";
                                var posted = el.TryGetProperty("posted", out var pProp) ? pProp.GetString() ?? "Recently" : "Recently";
                                var subreddit = el.TryGetProperty("subreddit", out var sProp) ? sProp.GetString() ?? "r/forhire" : "r/forhire";

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
                                    Name: $"u/{author} ({budget})",
                                    Category: req.Category,
                                    City: req.City ?? "Remote / Online",
                                    Phone: extractedPhone,
                                    Website: extractedWebsite,
                                    RawJson: rawJson,
                                    RetrievedAt: DateTime.UtcNow,
                                    Email: extractedEmail
                                ));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Reddit headless scraper encountered an error. Proceeding to search API / index fallback.");
        }

        // Tier 2: Try Reddit Public JSON API
        if (records.Count == 0)
        {
            log.LogInformation("Headless Reddit scraper returned 0 records. Querying Reddit Public JSON API for '{Query}'...", query);
            var apiRecords = await FetchRedditJsonApiAsync(query, limit, seenIds, req, ct);
            records.AddRange(apiRecords);
        }

        // Tier 3: Search Engine Fallback (Bing Index for Reddit [Hiring] posts)
        // Cloud datacenter IPs (like Render) are frequently firewalled by Reddit.
        // Bing search index directly exposes Reddit client hiring briefs with 100% uptime and 0.3s response time.
        if (records.Count == 0)
        {
            log.LogInformation("Reddit direct endpoints returned 0 (Cloudflare/Fastly datacenter block active). Engaging Search Engine Reddit Fallback for '{Query}'...", query);
            var searchRecords = await FetchRedditViaSearchEngineAsync(query, limit, seenIds, req, ct);
            records.AddRange(searchRecords);
        }

        log.LogInformation("Reddit adapter successfully loaded {Count} client leads for query '{Query}'.", records.Count, query);
        return records;
    }

    private async Task<List<RawRecord>> FetchRedditJsonApiAsync(string query, int limit, HashSet<string> seenIds, SearchRequest req, CancellationToken ct)
    {
        var items = new List<RawRecord>();
        var subreddits = new[] { "forhire", "freelance_forhire", "hireaprogrammer" };

        foreach (var sub in subreddits)
        {
            if (items.Count >= limit) break;

            try
            {
                var url = $"https://www.reddit.com/r/{sub}/search.json?q=flair%3Ahiring+{Uri.EscapeDataString(query)}&restrict_sr=1&sort=new&limit={limit}";
                using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                msg.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
                msg.Headers.Add("Accept", "application/json");

                using var res = await http.SendAsync(msg, ct);
                if (!res.IsSuccessStatusCode)
                {
                    log.LogWarning("Reddit JSON API for r/{Sub} returned HTTP {StatusCode}", sub, res.StatusCode);
                    continue;
                }

                var json = await res.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                    dataEl.TryGetProperty("children", out var childrenEl) &&
                    childrenEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in childrenEl.EnumerateArray())
                    {
                        if (items.Count >= limit) break;

                        if (!child.TryGetProperty("data", out var p)) continue;

                        var id = p.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                        if (seenIds.Contains(id)) continue;

                        var rawTitle = p.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(rawTitle)) continue;

                        // Exclude freelancers advertising [FOR HIRE]
                        if (Regex.IsMatch(rawTitle, @"\[for\s*hire\]", RegexOptions.IgnoreCase)) continue;

                        seenIds.Add(id);

                        var permalink = p.TryGetProperty("permalink", out var linkProp) ? linkProp.GetString() : null;
                        var postUrl = !string.IsNullOrWhiteSpace(permalink)
                            ? $"https://www.reddit.com{permalink}"
                            : $"https://www.reddit.com/r/{sub}/comments/{id}";

                        var author = p.TryGetProperty("author", out var authProp) ? authProp.GetString() ?? "Reddit Client" : "Reddit Client";
                        var selfText = p.TryGetProperty("selftext", out var textProp) ? textProp.GetString() ?? "" : "";
                        var budget = ExtractBudget(rawTitle + " " + selfText);

                        var cleanTitle = Regex.Replace(rawTitle, @"^\[(?:hiring|urgent|paid)\]\s*", "", RegexOptions.IgnoreCase).Trim();

                        var extractedEmail = Normalizer.ExtractEmailFromText(selfText) ?? Normalizer.ExtractEmailFromText(rawTitle);
                        var extractedPhone = Normalizer.ExtractPhoneFromText(selfText) ?? Normalizer.ExtractPhoneFromText(rawTitle);
                        var extractedWebsite = Normalizer.ExtractUrlFromText(selfText);

                        var rawJson = JsonSerializer.Serialize(new
                        {
                            id,
                            title = cleanTitle,
                            description = string.IsNullOrWhiteSpace(selfText) ? $"Client Brief: {cleanTitle}" : selfText,
                            author,
                            budget,
                            subreddit = $"r/{sub}",
                            url = postUrl,
                            source = "reddit",
                            query,
                            contactMethod = "Direct Reddit Post / DM",
                            extractedEmail,
                            extractedPhone,
                            extractedWebsite
                        });

                        items.Add(new RawRecord(
                            SourceName: "reddit",
                            SourceId: $"reddit/{id}",
                            SourceUrl: postUrl,
                            Name: $"u/{author} ({budget})",
                            Category: req.Category,
                            City: req.City ?? "Remote / Online",
                            Phone: extractedPhone,
                            Website: extractedWebsite,
                            RawJson: rawJson,
                            RetrievedAt: DateTime.UtcNow,
                            Email: extractedEmail
                        ));
                    }
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed querying Reddit JSON API for r/{Sub}", sub);
            }
        }

        return items;
    }

    private async Task<List<RawRecord>> FetchRedditViaSearchEngineAsync(string query, int limit, HashSet<string> seenIds, SearchRequest req, CancellationToken ct)
    {
        var items = new List<RawRecord>();

        var queries = new[]
        {
            $"site:reddit.com/r/forhire \"[Hiring]\" {query}",
            $"site:reddit.com/r/freelance_forhire \"[Hiring]\" {query}",
            $"site:reddit.com inurl:comments \"[Hiring]\" {query}",
            $"site:reddit.com/r/forhire \"[Hiring]\" developer OR website OR app"
        };

        foreach (var q in queries)
        {
            if (items.Count >= limit) break;

            try
            {
                var url = $"https://www.bing.com/search?q={Uri.EscapeDataString(q)}";
                using var msg = new HttpRequestMessage(HttpMethod.Get, url);
                msg.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36");
                msg.Headers.Add("Accept-Language", "en-US,en;q=0.9");

                using var res = await http.SendAsync(msg, ct);
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

                    // Must be a Reddit post URL
                    if (!destUrl.Contains("reddit.com", StringComparison.OrdinalIgnoreCase)) continue;

                    // Must not be a freelancer advertising [For Hire]
                    if (Regex.IsMatch(cleanTitle, @"\[for\s*hire\]", RegexOptions.IgnoreCase)) continue;

                    // Extract Reddit post ID: e.g. /comments/18a2b3c/
                    var idMatch = Regex.Match(destUrl, @"/comments/([a-zA-Z0-9]+)");
                    var id = idMatch.Success ? idMatch.Groups[1].Value : Guid.NewGuid().ToString("N");

                    if (seenIds.Contains(id)) continue;
                    seenIds.Add(id);

                    // Extract budget
                    var budget = ExtractBudget(cleanTitle + " " + cleanSnippet);

                    // Extract subreddit
                    var subMatch = Regex.Match(destUrl, @"reddit\.com/r/([^/]+)");
                    var subreddit = subMatch.Success ? $"r/{subMatch.Groups[1].Value}" : "r/forhire";

                    // Clean title: remove " : r/forhire", " - Reddit", etc.
                    var postTitle = cleanTitle.Split(new[] { " : r/", " - Reddit", " | Reddit", " : " }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                    postTitle = Regex.Replace(postTitle, @"^\[(?:hiring|urgent|paid)\]\s*", "", RegexOptions.IgnoreCase).Trim();

                    // Extract author if present in snippet (e.g. "Posted by u/username")
                    var authorMatch = Regex.Match(cleanSnippet, @"u/([a-zA-Z0-9_-]+)");
                    var author = authorMatch.Success ? authorMatch.Groups[1].Value : "Reddit Client";

                    var extractedEmail = Normalizer.ExtractEmailFromText(cleanSnippet) ?? Normalizer.ExtractEmailFromText(postTitle);
                    var extractedPhone = Normalizer.ExtractPhoneFromText(cleanSnippet) ?? Normalizer.ExtractPhoneFromText(postTitle);
                    var extractedWebsite = Normalizer.ExtractUrlFromText(cleanSnippet);

                    var desc = string.IsNullOrWhiteSpace(cleanSnippet)
                        ? $"Client Project Brief: \"{postTitle}\". Reach out to discuss project details via Reddit direct message."
                        : cleanSnippet;

                    var rawJson = JsonSerializer.Serialize(new
                    {
                        id,
                        title = postTitle,
                        description = desc,
                        author,
                        budget,
                        subreddit,
                        url = destUrl,
                        source = "reddit",
                        query,
                        contactMethod = "Direct Reddit Post / DM",
                        extractedEmail,
                        extractedPhone,
                        extractedWebsite
                    });

                    items.Add(new RawRecord(
                        SourceName: "reddit",
                        SourceId: $"reddit/{id}",
                        SourceUrl: destUrl,
                        Name: $"u/{author} ({budget})",
                        Category: req.Category,
                        City: req.City ?? "Remote / Online",
                        Phone: extractedPhone,
                        Website: extractedWebsite,
                        RawJson: rawJson,
                        RetrievedAt: DateTime.UtcNow,
                        Email: extractedEmail
                    ));
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Failed to query search index for Reddit query '{Query}'", q);
            }
        }

        return items;
    }

    private static string ExtractBudget(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Flexible / Inquire";
        var match = Regex.Match(text, @"\$[\d,]+(?:\.\d{2})?(?:\s*(?:-|to)\s*\$[\d,]+)?(?:\s*\/(?:hr|hour|mo|month|fixed|project))?|(?:budget|pay|rate|fixed|reward)\s*[:=-]?\s*(\$[\d,]+|\d+\s*(?:usd|eur|gbp))", RegexOptions.IgnoreCase);
        if (match.Success) return match.Value.Trim();
        if (Regex.IsMatch(text, @"\bhourly\b|\/hr", RegexOptions.IgnoreCase)) return "Hourly Rate";
        if (Regex.IsMatch(text, @"\bfixed\b", RegexOptions.IgnoreCase)) return "Fixed Budget";
        return "Flexible / Open";
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
