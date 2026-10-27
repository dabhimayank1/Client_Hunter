using AiClientHunter.Api.Services;
using Xunit;

namespace AiClientHunter.Tests;

public class ScorerTests
{
    [Fact]
    public void Score_WithExplicitProjectRequest_ShouldBeHot()
    {
        // Explicit request (+60) + Request details (+10) + In target area (+5) = 75 points -> hot
        var signals = new LeadSignals(
            ExplicitRequest: true,
            RequestDetails: true,
            Recent7Days: false,
            NewBusinessSignal: false,
            WebsiteFieldMissing: false,
            NoLiveSiteFound: false,
            HasPhone: false,
            InTargetArea: true
        );

        var result = Scorer.Score(signals);

        Assert.Equal(75, result.Points);
        Assert.Equal("hot", result.Priority);
        Assert.Equal(3, result.Reasons.Count);
        Assert.Contains(result.Reasons, r => r.Contains("+60"));
        Assert.Contains(result.Reasons, r => r.Contains("+10"));
        Assert.Contains(result.Reasons, r => r.Contains("+5"));
    }

    [Fact]
    public void Score_WithoutExplicitRequest_ShouldNeverBeHot_EvenWithHighPoints()
    {
        // New business (+25) + Website missing (+15) + No live site (+10) + Has phone (+5) + Target area (+5) = 60 points
        // Blueprint rule: Never mark Hot unless ExplicitRequest is true!
        var signals = new LeadSignals(
            ExplicitRequest: false,
            RequestDetails: false,
            Recent7Days: false,
            NewBusinessSignal: true,
            WebsiteFieldMissing: true,
            NoLiveSiteFound: true,
            HasPhone: true,
            InTargetArea: true
        );

        var result = Scorer.Score(signals);

        Assert.Equal(60, result.Points);
        Assert.Equal("warm", result.Priority); // Must be warm, NOT hot!
    }

    [Fact]
    public void Score_LowPoints_ShouldBePotential()
    {
        // Only Website missing (+15) + In target area (+5) = 20 points (< 30) -> potential
        var signals = new LeadSignals(
            ExplicitRequest: false,
            RequestDetails: false,
            Recent7Days: false,
            NewBusinessSignal: false,
            WebsiteFieldMissing: true,
            NoLiveSiteFound: false,
            HasPhone: false,
            InTargetArea: true
        );

        var result = Scorer.Score(signals);

        Assert.Equal(20, result.Points);
        Assert.Equal("potential", result.Priority);
    }
}
