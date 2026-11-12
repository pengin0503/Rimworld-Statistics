namespace RimworldStatistics.Core;

/// <summary>In-tree integrations for optional frameworks, composed with the baseline catalog.</summary>
public static class BuiltInModMetricCatalog
{
    public static IReadOnlyList<MetricDefinition> Definitions { get; } =
    [
        Vpe(VpeStatistics.LevelsGained, "RWS_Metric_VpeLevelsGained", "RWS_MetricDesc_VpeLevelsGained", OverallAggregation.Sum, live: true),
        Vpe(VpeStatistics.Psycasters, "RWS_Metric_VpePsycasters", "RWS_MetricDesc_VpePsycasters", OverallAggregation.Sum),
        Vpe(VpeStatistics.MaxLevel, "RWS_Metric_VpeMaxLevel", "RWS_MetricDesc_VpeMaxLevel", OverallAggregation.Max),
        Vpe(VpeStatistics.SumLevels, "RWS_Metric_VpeSumLevels", "RWS_MetricDesc_VpeSumLevels", OverallAggregation.Sum),
        Outposts(OutpostSnapshot.Pawns, "RWS_Metric_OutpostPawns", "RWS_MetricDesc_OutpostPawns", OutpostSnapshot.Unit),
        Outposts(OutpostSnapshot.ByType, "RWS_Metric_OutpostByType", "RWS_MetricDesc_OutpostByType", OutpostSnapshot.Unit, breakdown: true),
        Outposts(OutpostProduction.Deliveries, "RWS_Metric_OutpostDeliveries", "RWS_MetricDesc_OutpostDeliveries", OutpostProduction.Unit),
        Outposts(OutpostProduction.ByDef, "RWS_Metric_OutpostProducts", "RWS_MetricDesc_OutpostProducts", OutpostProduction.Unit, breakdown: true),
    ];

    private static MetricDefinition Vpe(string key, string label, string description, OverallAggregation overall, bool live = false) => new()
    {
        Key = key, StoredKey = key, LedgerIds = "M1", Category = MetricCategory.Royalty, Unit = MetricUnit.Count,
        RequiredDlc = AcquisitionUnits.Royalty, Overall = overall,
        Source = live ? MetricObservationSource.Checkpoint : MetricObservationSource.SaveSnapshot,
        Provenance = live ? MetricProvenance.LiveAggregate : MetricProvenance.SaveSnapshot,
        Acquisition = live ? VpeStatistics.EventUnit : VpeStatistics.SnapshotUnit,
        OwnerPackageId = VpeStatistics.PackageId,
        LabelKey = label, DescriptionKey = description,
    };
    private static MetricDefinition Outposts(string key, string label, string description, string unit, bool breakdown = false) => new()
    {
        Key = key, StoredKey = key, LedgerIds = "M2", Category = MetricCategory.Exploration, Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum, HasBreakdown = breakdown,
        Source = unit == OutpostProduction.Unit ? MetricObservationSource.Checkpoint : MetricObservationSource.SaveSnapshot,
        Provenance = unit == OutpostProduction.Unit ? MetricProvenance.LiveAggregate : MetricProvenance.SaveSnapshot,
        Acquisition = unit, OwnerPackageId = OutpostSnapshot.PackageId,
        LabelKey = label, DescriptionKey = description,
    };

}
