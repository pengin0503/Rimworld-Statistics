namespace RimworldStatistics.Core.Tests;

public sealed class NumericColonyStartTests
{
    [Theory]
    [InlineData("TemperateForest")]
    [InlineData("ExtremeDesert")]
    public void StartingBiomeSnapshotKeepsStableDefKey(string biome)
    {
        var result = ColonyStartSnapshot.Build(biome);
        var row = result[ColonyStartSnapshot.Biome];
        Assert.Equal(1, row.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1, row.Values[biome].IntegerValue);
        Assert.Equal(MetricCoverage.SnapshotOnly, row.Values[biome].Coverage);
    }

    [Fact]
    public void MissingOrMalformedStartingBiomeIsUnknownNotZero()
    {
        foreach (var bad in new string?[] { null, "", " ", "not/a/def" })
        {
            var m = ColonyStartSnapshot.Build(bad)[ColonyStartSnapshot.Biome];
            Assert.Equal(MetricCoverage.Unknown, m.Values[MetricEntry.Scalar].Coverage);
            Assert.Null(m.Values[MetricEntry.Scalar].Number);
            Assert.Single(m.Values);
        }
    }

    [Fact]
    public void StartingBiomeIsAnUnverifiedStandaloneSnapshotUnit()
    {
        var d = MetricRegistry.Find(ColonyStartSnapshot.Biome)!;
        Assert.Equal("colony_start", d.Acquisition);
        Assert.Equal(MetricObservationSource.SaveSnapshot, d.Source);
        Assert.True(d.HasBreakdown);
        Assert.False(d.Published);
    }
}
