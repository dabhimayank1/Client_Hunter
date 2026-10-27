namespace AiClientHunter.Api.Ai;

public record AiAnalysis(
    string Summary,
    string RecommendedService,
    string DraftMessage,
    string[] MissingInfo
);

public interface IAiProvider
{
    Task<AiAnalysis> AnalyzeAsync(string evidenceText, CancellationToken ct);
}
