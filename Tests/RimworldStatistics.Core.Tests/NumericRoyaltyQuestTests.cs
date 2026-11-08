namespace RimworldStatistics.Core.Tests;

public sealed class NumericRoyaltyQuestTests
{
    [Fact]
    public void OnlyExplicitlyImperialQuestRootsAreAttributed_NoGenericRoyaltyQuestGuess()
    {
        var qs = new List<RetainedQuest>
        {
            new(1, "EndGame_RoyalAscent", AcceptanceTick: 100, Ended: true, Outcome: "Success"),
            new(2, "BestowingCeremony", AcceptanceTick: 120, Ended: true, Outcome: "Success"),
            new(3, "Decree_ProduceItem", AcceptanceTick: 150, Ended: true, Outcome: "Success"),
            new(4, "BuildMonument_Root_Basic", AcceptanceTick: 155, Ended: true, Outcome: "Success"),
            new(5, "BestowingCeremony", AcceptanceTick: 160, Ended: true, Outcome: "Fail"),
            new(6, "ChangeRoyalHeir", AcceptanceTick: 200, Ended: true, Outcome: "Success"),
        };
        var m = new Dictionary<string, MetricEntry>();
        RetainedQuestMetricReader.Summarize(qs, 0, 60000, m);
        var v = m[RoyaltyQuestDerived.Success].Values[MetricEntry.Scalar];
        Assert.Equal(4, v.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, v.Coverage);
        Assert.False(MetricRegistry.Find(RoyaltyQuestDerived.Success)!.Published);
    }

    [Fact]
    public void NoQuestManagerDoesNotFabricateZeroEmpireSuccess()
    {
        var m = new Dictionary<string, MetricEntry>();
        RetainedQuestMetricReader.Summarize(null, 0, 60000, m);
        Assert.False(m.ContainsKey(RoyaltyQuestDerived.Success));
    }
}
