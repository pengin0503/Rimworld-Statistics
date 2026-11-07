namespace RimworldStatistics.Core;

/// <summary>Phase B snapshots are independent from event checkpoints and remain hidden until verified.</summary>
public static class SnapshotCatalog
{
    private static MetricDefinition Snap(string key, string ledger, MetricUnit metricUnit, OverallAggregation aggregation,
        string unit, string label, string description, bool breakdown = false)
        => new()
        {
            Key = key, StoredKey = key, LedgerIds = ledger, Category = MetricCategory.Research, Unit = metricUnit,
            Provenance = MetricProvenance.SaveSnapshot, Source = MetricObservationSource.SaveSnapshot,
            Overall = aggregation, Acquisition = unit, HasBreakdown = breakdown,
            LabelKey = label, DescriptionKey = description,
        };

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        Snap(ResearchStateSnapshot.Finished, "S-012", MetricUnit.Count, OverallAggregation.Sum, "research_state", "RWS_Metric_ResearchSnapshotFinished", "RWS_MetricDesc_ResearchSnapshotFinished"),
        Snap(ResearchStateSnapshot.FinishedRate, "S-012", MetricUnit.Percent, OverallAggregation.Ratio, "research_state", "RWS_Metric_ResearchSnapshotFinishedRate", "RWS_MetricDesc_ResearchSnapshotFinishedRate"),
        Snap(ResearchStateSnapshot.TechPeak, "S-013", MetricUnit.Count, OverallAggregation.Max, "research_state", "RWS_Metric_ResearchSnapshotTechPeak", "RWS_MetricDesc_ResearchSnapshotTechPeak", breakdown: true),
    };
}
