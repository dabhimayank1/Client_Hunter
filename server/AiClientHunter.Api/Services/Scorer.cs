namespace AiClientHunter.Api.Services;

public record LeadSignals(
    bool ExplicitRequest,
    bool RequestDetails,
    bool Recent7Days,
    bool NewBusinessSignal,
    bool WebsiteFieldMissing,
    bool NoLiveSiteFound,
    bool HasPhone,
    bool InTargetArea
);

public record ScoreResult(int Points, string Priority, List<string> Reasons);

public static class Scorer
{
    public static ScoreResult Score(LeadSignals s)
    {
        int pts = 0;
        var why = new List<string>();

        void Add(bool cond, int p, string text)
        {
            if (cond)
            {
                pts += p;
                why.Add($"+{p} {text}");
            }
        }

        Add(s.ExplicitRequest, 60, "Public post asks for a website/app/software developer");
        Add(s.RequestDetails, 10, "Post mentions budget, deadline or details");
        Add(s.Recent7Days, 10, "Posted or listed in the last 7 days");
        Add(s.NewBusinessSignal, 25, "New-business signal");
        Add(s.WebsiteFieldMissing, 15, "Website missing in the source listing");
        Add(s.NoLiveSiteFound, 10, "No live site found by our checks");
        Add(s.HasPhone, 5, "Phone available from a permitted source");
        Add(s.InTargetArea, 5, "In a target city");

        // Rule from Blueprint: Never mark a lead Hot unless an explicit project request exists!
        var priority = s.ExplicitRequest && pts >= 60 ? "hot" : pts >= 30 ? "warm" : "potential";
        return new ScoreResult(pts, priority, why);
    }
}
