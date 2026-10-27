using System.Text.Json;

namespace AiClientHunter.Api.Sources;

public class OsmOverpassAdapter(HttpClient http, ILogger<OsmOverpassAdapter> log) : ISourceAdapter
{
    public string Name => "osm";

    public async Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct)
    {
        if (!OsmMaps.CityBoxes.TryGetValue(req.City, out var bbox))
            throw new InvalidOperationException($"City not supported yet: {req.City}. Supported cities: {string.Join(", ", OsmMaps.CityBoxes.Keys)}");

        var normCat = OsmMaps.NormalizeCategory(req.Category);
        if (normCat == null || !OsmMaps.CategoryFilters.TryGetValue(normCat, out var filter))
        {
            log.LogInformation("Category '{Category}' is not an OSM amenity directory category. Skipping OSM query.", req.Category);
            return new List<RawRecord>();
        }

        var query = $"[out:json][timeout:60][bbox:{bbox}];nwr{filter};out center tags meta;";
        log.LogInformation("Querying OpenStreetMap Overpass API for {Category} in {City} (bbox: {Bbox}).", req.Category, req.City, bbox);

        using var body = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("data", query)
        });

        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsync("https://overpass-api.de/api/interpreter", body, ct);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException("Overpass API request timed out. The public server may be busy, please retry in a few moments.", ex);
        }

        if ((int)resp.StatusCode == 429)
        {
            throw new InvalidOperationException("Overpass API rate limited (HTTP 429). Please wait 60 seconds before making another query.");
        }

        if ((int)resp.StatusCode == 504)
        {
            throw new InvalidOperationException("Overpass API Gateway Timeout (HTTP 504). Please retry with a smaller limit or wait.");
        }

        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var results = new List<RawRecord>();

        if (!doc.RootElement.TryGetProperty("elements", out var elements))
            return results;

        foreach (var el in elements.EnumerateArray())
        {
            if (!el.TryGetProperty("tags", out var tags))
                continue;

            string? Tag(string k) => tags.TryGetProperty(k, out var v) ? v.GetString() : null;

            var name = Tag("name");
            if (string.IsNullOrWhiteSpace(name))
                continue; // Unnamed places are discarded as per blueprint

            var type = el.GetProperty("type").GetString() ?? "node";
            var id = el.GetProperty("id").GetInt64();

            var phone = Tag("phone") ?? Tag("contact:phone") ?? Tag("contact:mobile");
            var website = Tag("website") ?? Tag("contact:website") ?? Tag("url");
            var email = Tag("email") ?? Tag("contact:email") ?? Tag("contact:e-mail") ?? Tag("operator:email");

            // Strict no-website requirement: If OSM records an existing website, skip it!
            if (!string.IsNullOrWhiteSpace(website))
            {
                continue;
            }

            results.Add(new RawRecord(
                SourceName: "osm",
                SourceId: $"{type}/{id}",
                SourceUrl: $"https://www.openstreetmap.org/{type}/{id}",
                Name: name.Trim(),
                Category: req.Category,
                City: req.City,
                Phone: phone?.Trim(),
                Website: website?.Trim(),
                RawJson: el.GetRawText(),
                RetrievedAt: DateTime.UtcNow,
                Email: email?.Trim()
            ));
        }

        log.LogInformation("OpenStreetMap returned {Count} named places for {Category} in {City}.", results.Count, req.Category, req.City);
        return results;
    }
}
