namespace RimworldStatistics.Core.Tests;

/// <summary>Conservative successful-save snapshot of player-owned VEF Outposts, not item deliveries.</summary>
public sealed class OutpostSnapshotTests
{
    [Fact]
    public void ExplicitExperimentalSnapshotHasCrossColonyComparisons()
    {
        var unit = AcquisitionUnits.Find(OutpostSnapshot.Unit);
        Assert.NotNull(unit);
        Assert.Equal(AcquisitionKind.Snapshot, unit!.Kind);
        Assert.Equal(AcquisitionStatus.Unverified, unit.Status);
        var def = MetricRegistry.Find(OutpostSnapshot.Active)!;
        Assert.Equal(OutpostSnapshot.Unit, def.Acquisition);
        Assert.Equal(MetricObservationSource.SaveSnapshot, def.Source);
        Assert.Equal(OverallAggregation.Sum, def.Overall);
        Assert.NotNull(MetricRegistry.Find(CrossColonyCatalog.MaxPrefix + OutpostSnapshot.Active));
        Assert.NotNull(MetricRegistry.Find(CrossColonyCatalog.MinPrefix + OutpostSnapshot.Active));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ActivePlayerOutpostsAreARepresentativeSuccessfulSaveValue(int count)
    {
        var entry = OutpostSnapshot.Build(count)[OutpostSnapshot.Active];
        var value = entry.Values[MetricEntry.Scalar];
        Assert.Equal((long)count, value.IntegerValue);
        Assert.Equal(MetricCoverage.SnapshotOnly, value.Coverage);
        Assert.Equal(MetricKeys.Revision(OutpostSnapshot.Active), entry.Revision);
    }

    [Fact]
    public void OutpostTypeDetectionAcceptsSubclassesAndRejectsUnrelatedTypes()
    {
        Assert.True(OutpostSnapshot.IsOutpostType(typeof(global::Outposts.Outpost)));
        Assert.True(OutpostSnapshot.IsOutpostType(typeof(global::Outposts.TestDerivedOutpost)));
        Assert.False(OutpostSnapshot.IsOutpostType(typeof(string)));
        Assert.False(OutpostSnapshot.IsOutpostType(null));
    }

    [Fact]
    public void InvalidCountIsNotMistakenForObservedZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OutpostSnapshot.Build(-1));
    }

    [Fact]
    public void APlayerOutpostCountIsNotALifetimeProductionOrDeliveryCounter()
    {
        var definition = MetricRegistry.Find(OutpostSnapshot.Active)!;
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(definition));
        Assert.Null(MetricRegistry.Find("outposts.produced_units"));
        Assert.Null(MetricRegistry.Find("outposts.received_units"));
    }
}
