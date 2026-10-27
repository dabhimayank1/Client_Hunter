namespace AiClientHunter.Api.Sources;

public record SearchRequest(string City, string Category, int Limit);

public record RawRecord(
    string SourceName,
    string SourceId,
    string SourceUrl,
    string Name,
    string? Category,
    string? City,
    string? Phone,
    string? Website,
    string RawJson,
    DateTime RetrievedAt,
    string? Email = null
);

public interface ISourceAdapter
{
    string Name { get; }
    Task<IReadOnlyList<RawRecord>> FetchAsync(SearchRequest req, CancellationToken ct);
}
