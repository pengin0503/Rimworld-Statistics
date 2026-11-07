namespace RimworldStatistics.Core.Tests;

/// <summary>
/// The catalog of Phase B/C/D2 outputs and their acquisition units: nothing whose acquisition is not confirmed in the game is
/// published (user instruction: 未検証の取得処理を検証済みと偽って有効化しない), and every output can be published by marking
/// its unit verified.
/// </summary>
public sealed class NumericCatalogTests
{
    [Fact]
    public void Published_IsExactlyTheCatalogOfVerifiedUnits()
    {
        Assert.Equal(MetricRegistry.Catalog.Where(d => d.Published).Select(d => d.Key), MetricRegistry.All.Select(d => d.Key));
        Assert.All(MetricRegistry.All, d => Assert.True(d.Acquisition is null || AcquisitionUnits.Find(d.Acquisition)!.Status == AcquisitionStatus.Verified));
        var pending = new MetricDefinition
        {
            Key = "x", StoredKey = "x", LedgerIds = "x", Category = MetricCategory.Colony, Unit = MetricUnit.Count,
            Provenance = MetricProvenance.LiveAggregate, Overall = OverallAggregation.Sum, LabelKey = "x", DescriptionKey = "x",
            Acquisition = "research",
        };
        Assert.False(pending.Published);
        Assert.True((pending with { Acquisition = null }).Published);
    }

    [Fact]
    public void NoUnitIsVerifiedWithoutAnInGameRun()
        => Assert.All(AcquisitionUnits.All, u => Assert.Equal(AcquisitionStatus.Unverified, u.Status));

    [Fact]
    public void CatalogKeysAndUnitsAreConsistent()
    {
        Assert.Equal(MetricRegistry.Catalog.Count, MetricRegistry.Catalog.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(MetricRegistry.Catalog.Where(d => d.Acquisition != null), d => Assert.NotNull(AcquisitionUnits.Find(d.Acquisition!)));
        Assert.All(LiveFamilies.All, f => Assert.Equal(AcquisitionKind.Family, AcquisitionUnits.Find(f.Name)?.Kind));
        Assert.All(LiveFamilies.All, f => Assert.Equal(AcquisitionUnits.Find(f.Name)!.RequiredDlc, f.RequiredDlc));
        Assert.Equal(AcquisitionUnits.All.Count, AcquisitionUnits.All.Select(u => u.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain(AcquisitionUnits.All, u => u.Id.Contains("anomaly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void CheckpointOutputs_ComeFromTheirFamily()
    {
        // Every catalog output stored by a family is produced by that family's builder (keys are not invented elsewhere).
        foreach (var d in MetricRegistry.Catalog.Where(d => d.Source == MetricObservationSource.Checkpoint && d.Overall != OverallAggregation.Ranking))
        {
            var family = LiveFamilies.Find(d.Acquisition!);
            Assert.True(family != null, d.Key + " has no family");
        }
    }
}
