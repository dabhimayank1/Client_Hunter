using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AiClientHunter.Api.Ai;

public class OllamaProvider(HttpClient http, IConfiguration cfg, ILogger<OllamaProvider> log) : IAiProvider
{
    private const string SystemPrompt =
        "You help a freelance web developer write short, polite first messages. " +
        "Use ONLY the facts in the evidence. If a fact is not there, do not mention it " +
        "and list it in missing_info. " +
        "Never invent phone numbers, prices, names, links or claims about the business. " +
        "Draft: 3 to 5 sentences, no hype, say honestly how the lead was found, " +
        "and include a polite way to opt out.";

    private static readonly object Schema = new
    {
        type = "object",
        properties = new
        {
            summary = new { type = "string" },
            recommended_service = new
            {
                type = "string",
                @enum = new[] { "website", "mobile_app", "software", "automation", "seo", "unknown" }
            },
            draft_message = new { type = "string" },
            missing_info = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "summary", "recommended_service", "draft_message", "missing_info" }
    };

    private static readonly Regex Risky = new(@"https?://\S+|\+?\d[\d\s-]{8,}\d", RegexOptions.Compiled);

    public async Task<AiAnalysis> AnalyzeAsync(string evidenceText, CancellationToken ct)
    {
        var model = cfg["Ollama:Model"] ?? "llama3.2:3b";
        var baseUrl = cfg["Ollama:BaseUrl"] ?? "http://localhost:11434";

        var body = new
        {
            model = model,
            stream = false,
            format = Schema, // Forces Ollama to strictly follow JSON schema
            options = new { temperature = 0.2 },
            messages = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = evidenceText }
            }
        };

        try
        {
            using var resp = await http.PostAsJsonAsync($"{baseUrl}/api/chat", body, ct);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var content = json.GetProperty("message").GetProperty("content").GetString()!;

            var opts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            var result = JsonSerializer.Deserialize<AiAnalysis>(content, opts)
                ?? throw new InvalidOperationException("Empty AI response received.");

            // Anti-hallucination check: ensure AI did not invent unknown URLs or phone numbers
            if (!MentionsOnlyKnownFacts(result.DraftMessage, evidenceText))
            {
                log.LogWarning("AI output failed anti-hallucination guardrail (invented numbers or URLs). Falling back to safe template.");
                return GenerateSafeFallback(evidenceText, "AI response contained unverified claims.");
            }

            return result;
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "Ollama is not responding at {BaseUrl}. Using safe deterministic fallback.", baseUrl);
            return GenerateSafeFallback(evidenceText, $"Ollama is currently offline at {baseUrl}. (Using standard compliance template)");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "AI analysis error.");
            return GenerateSafeFallback(evidenceText, $"AI error: {ex.Message}");
        }
    }

    public static bool MentionsOnlyKnownFacts(string draft, string evidenceText)
    {
        return Risky.Matches(draft).All(m =>
            evidenceText.Contains(m.Value.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    // High quality deterministic fallback when Ollama is offline or on CPU fallback
    private static AiAnalysis GenerateSafeFallback(string evidenceText, string note)
    {
        string business = "your business";
        string category = "business";
        string city = "your area";

        foreach (var line in evidenceText.Split('\n'))
        {
            if (line.StartsWith("Business: ", StringComparison.OrdinalIgnoreCase))
                business = line["Business: ".Length..].Trim();
            else if (line.StartsWith("Category: ", StringComparison.OrdinalIgnoreCase))
                category = line["Category: ".Length..].Trim();
            else if (line.StartsWith("City: ", StringComparison.OrdinalIgnoreCase))
                city = line["City: ".Length..].Trim();
        }

        var draft = $"Hello, I noticed {business} in {city} while reviewing local {category} listings and saw that you don't have an active website listed. " +
                    $"I am a web developer helping local businesses build clean, fast, and mobile-friendly websites to attract more customers. " +
                    $"If you'd be interested in exploring options or seeing a sample mockup, please feel free to reply. " +
                    $"(If you do not wish to receive messages, reply 'STOP' and I will immediately remove you).";

        return new AiAnalysis(
            Summary: $"{business} is a {category} located in {city}. [{note}]",
            RecommendedService: "website",
            DraftMessage: draft,
            MissingInfo: new[] { "Direct owner contact name", "Specific business requirements" }
        );
    }
}
