namespace RimworldStatistics.Core.Tests;

/// <summary>Phase B research-state calculation; no game needed.</summary>
public sealed class NumericSnapshotResearchTests
{
    [Fact]
    public void CompletedProjectsAreCountedWithTheWholeRegisteredDenominator()
    {
        var m = ResearchStateSnapshot.Build(new[]
        {
            new ResearchStateFact("InitialCompleted", true, 1),
            new ResearchStateFact("Unstarted", false, 4),
            new ResearchStateFact("Finished", true, 3),
            new ResearchStateFact("InProgress", false, 2),
        });
        var n = m[ResearchStateSnapshot.Finished].Values[""];
        var rate = m[ResearchStateSnapshot.FinishedRate].Values[""];
        var peak = m[ResearchStateSnapshot.TechPeak].Values[""];
        Assert.Equal(2, n.IntegerValue);
        Assert.Equal(2d, rate.Numerator);
        Assert.Equal(4d, rate.Denominator);
        Assert.Equal(0.5d, rate.RealValue);
        Assert.Equal(3, peak.IntegerValue);
        Assert.Contains("Finished", m[ResearchStateSnapshot.TechPeak].Values.Keys);
        Assert.Equal(MetricCoverage.SnapshotOnly, peak.Coverage);
    }

    [Fact]
    public void NoRegisteredResearchLeavesRateNotApplicable()
    {
        var m = ResearchStateSnapshot.Build(Array.Empty<ResearchStateFact>());
        Assert.Equal(0, m[ResearchStateSnapshot.Finished].Values[""].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable, m[ResearchStateSnapshot.FinishedRate].Values[""].Coverage);
        Assert.Equal(MetricCoverage.NotApplicable, m[ResearchStateSnapshot.TechPeak].Values[""].Coverage);
    }

    [Fact]
    public void AmbiguousOrDuplicateDefsWeakenCoverageWithoutInflatingCounts()
    {
        var m = ResearchStateSnapshot.Build(new[]
        {
            new ResearchStateFact("A", true, 4),
            new ResearchStateFact("A", true, 4),
            new ResearchStateFact("B", false, 3),
        });
        Assert.Equal(1, m[ResearchStateSnapshot.Finished].Values[""].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, m[ResearchStateSnapshot.Finished].Values[""].Coverage);
        Assert.Equal(2d, m[ResearchStateSnapshot.FinishedRate].Values[""].Denominator);
    }

    [Fact]
    public void ResearchOutputsAreCatalogedButNotPublishedBeforeVerification()
    {
        foreach (var key in new[] { ResearchStateSnapshot.Finished, ResearchStateSnapshot.FinishedRate, ResearchStateSnapshot.TechPeak })
        {
            var d = MetricRegistry.Find(key);
            Assert.NotNull(d);
            Assert.Equal(MetricObservationSource.SaveSnapshot, d!.Source);
            Assert.Equal("research_state", d.Acquisition);
            Assert.False(d.Published);
        }
    }
}
