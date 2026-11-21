using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class DbhStatisticsTests
{
    [Fact]
    public void FixturesCountOnlyPlayerHomeBuildingsFromDbh()
    {
        var result = DbhStatistics.Fixtures([
            new("Toilet", "Dubwise.DubsBadHygiene", true),
            new("Toilet", "dubwise.dubsbadhygiene", true),
            new("Bath", "dubwise.dubsbadhygiene", false),
            new("Bath", "some.other.mod", true),
        ]);
        Assert.Equal(2, result.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2, result.Values["Toilet"].IntegerValue);
        Assert.False(result.Values.ContainsKey("Bath"));
        Assert.Equal(MetricCoverage.SnapshotOnly, result.Values["Toilet"].Coverage);
    }

    [Fact]
    public void EmptyObservedHomeHasZeroFixturesAndMissingDefsArePartial()
    {
        Assert.Equal(0, DbhStatistics.Fixtures([]).Values[MetricEntry.Scalar].IntegerValue);
        var result = DbhStatistics.Fixtures([new(null, "dubwise.dubsbadhygiene", true)]);
        Assert.Equal(1, result.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, result.Values[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void CountsOnlyCompletedJobsAndPreservesBreakdown()
    {
        var state = new FamilyState { ThroughTick = 100, Continuous = true };
        DbhStatistics.Ended(state, "takeShower", succeeded: false);
        DbhStatistics.Ended(state, "UseToilet", succeeded: true);
        DbhStatistics.Ended(state, "UseToilet", succeeded: true);
        DbhStatistics.Ended(state, "washHands", succeeded: true);
        var into = new Dictionary<string, MetricEntry>();
        DbhStatistics.Family.Build(new FamilyOutputContext(state, 100, into));
        var result = into[DbhStatistics.ActivitiesByJob];
        Assert.Equal(3, result.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2, result.Values["UseToilet"].IntegerValue);
        Assert.Equal(1, result.Values["washHands"].IntegerValue);
        Assert.False(result.Values.ContainsKey("takeShower"));
    }
}
