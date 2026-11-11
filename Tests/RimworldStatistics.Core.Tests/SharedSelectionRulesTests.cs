namespace RimworldStatistics.Core.Tests;

public sealed class SharedSelectionRulesTests
{
    private static MetricObservation Obs(string key, int tick, int minute) => new()
    {
        Key = key, SaveTick = tick, ObservedUtc = new DateTime(2026, 1, 1, 0, minute, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void Latest_PrefersTickThenTimeThenSmallestKey()
    {
        Assert.Null(MetricObservationOrder.Latest(Array.Empty<MetricObservation>()));
        Assert.Equal("b", MetricObservationOrder.Latest(new[] { Obs("a", 10, 9), Obs("b", 20, 0) })!.Key);
        Assert.Equal("b", MetricObservationOrder.Latest(new[] { Obs("a", 20, 1), Obs("b", 20, 2) })!.Key);
        Assert.Equal("a", MetricObservationOrder.Latest(new[] { Obs("b", 20, 2), Obs("a", 20, 2) })!.Key);
    }

    [Fact]
    public void UsesLastCapture_IsExactlyProvidersAndFactions()
    {
        foreach (var d in MetricRegistry.Catalog)
            Assert.Equal(d.ProviderId is not null || d.Acquisition == "factions", SnapshotSelectionPolicy.UsesLastCapture(d));
        Assert.Contains(MetricRegistry.Catalog, d => SnapshotSelectionPolicy.UsesLastCapture(d));
    }

    [Theory]
    [InlineData(false, ObservationStatus.Ok, 15, true)]
    [InlineData(true, ObservationStatus.Ok, 15, false)]
    [InlineData(false, ObservationStatus.Error, 15, false)]
    [InlineData(false, ObservationStatus.Unidentifiable, 15, true)]
    [InlineData(false, ObservationStatus.Ok, 14, false)]
    [InlineData(false, ObservationStatus.Ok, 16, true)]
    public void IsCurrentRead_MatchesTheScanRule(bool superseded, ObservationStatus status, int readerVersion, bool expected)
    {
        var o = new SaveObservation { FileName = "a.rws", Superseded = superseded, Status = status, ReaderVersion = readerVersion };
        Assert.Equal(expected, ProfileMutations.IsCurrentRead(o, 15));
    }
}
