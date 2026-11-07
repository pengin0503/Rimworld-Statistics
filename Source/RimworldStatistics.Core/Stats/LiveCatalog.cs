namespace RimworldStatistics.Core;

/// <summary>
/// The outputs of the acquisition units of Phases B, C and D2 (<see cref="AcquisitionUnits"/>). They are in the catalog with
/// their stored keys, units, aggregation and translation keys, so a unit is published by marking it verified, nothing else.
/// Order is the display order within each category.
/// </summary>
public static class LiveCatalog
{
    private const MetricUnit Count = MetricUnit.Count;
    private const MetricUnit Days = MetricUnit.Days;
    private const OverallAggregation Sum = OverallAggregation.Sum;
    private const OverallAggregation Max = OverallAggregation.Max;
    private const OverallAggregation Min = OverallAggregation.Min;
    private const OverallAggregation Ratio = OverallAggregation.Ratio;
    private const OverallAggregation Each = OverallAggregation.IndividualOnly;

    /// <summary>An output of a live family (stored in the checkpoint).</summary>
    private static MetricDefinition Live(string key, string ledger, MetricCategory category, MetricUnit unit, OverallAggregation overall, string family,
        string label, string description, bool breakdown = false, MetricUnit? breakdownUnit = null, bool signed = false, string? stored = null)
        => new()
        {
            Key = key, StoredKey = stored ?? key, LedgerIds = ledger, Category = category, Unit = unit, Provenance = MetricProvenance.LiveAggregate,
            Source = MetricObservationSource.Checkpoint, Overall = overall, Signed = signed, HasBreakdown = breakdown, BreakdownUnit = breakdownUnit,
            RequiredDlc = AcquisitionUnits.Find(family)?.RequiredDlc, Acquisition = family, LabelKey = label, DescriptionKey = description,
        };

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        // C1 · research
        Live(ResearchFamily.Completed, "Q-001", MetricCategory.Research, Count, Sum, ResearchFamily.Name, "RWS_Metric_ResearchLiveCompleted", "RWS_MetricDesc_ResearchLiveCompleted"),
        Live(ResearchFamily.CompletedTicks, "Q-002", MetricCategory.Research, Count, Each, ResearchFamily.Name, "RWS_Metric_ResearchLiveCompletedTicks", "RWS_MetricDesc_ResearchLiveCompletedTicks",
            breakdown: true, breakdownUnit: Days),
        Live(ResearchFamily.DurationMin, "Q-003", MetricCategory.Research, Days, Min, ResearchFamily.Name, "RWS_Metric_ResearchLiveDurationMin", "RWS_MetricDesc_ResearchLiveDurationMin"),
        Live(ResearchFamily.DurationMax, "Q-004, N3-W-011", MetricCategory.Research, Days, Max, ResearchFamily.Name, "RWS_Metric_ResearchLiveDurationMax", "RWS_MetricDesc_ResearchLiveDurationMax"),
        Live(ResearchFamily.FirstDay, "N5-M-004", MetricCategory.Research, Days, Min, ResearchFamily.Name, "RWS_Metric_ResearchLiveFirstDay", "RWS_MetricDesc_ResearchLiveFirstDay"),

        // C1 · mental
        Live(MentalFamily.Breaks, "P-005", MetricCategory.Mind, Count, Sum, MentalFamily.Name, "RWS_Metric_MentalBreaks", "RWS_MetricDesc_MentalBreaks"),
        Live(MentalFamily.BreaksByDef, "P-006", MetricCategory.Mind, Count, Sum, MentalFamily.Name, "RWS_Metric_MentalBreaksByDef", "RWS_MetricDesc_MentalBreaksByDef", breakdown: true),
        Live(MentalFamily.Inspirations, "P-025", MetricCategory.Mind, Count, Sum, MentalFamily.Name, "RWS_Metric_Inspirations", "RWS_MetricDesc_Inspirations"),
        Live(MentalFamily.InspirationsByDef, "P-025", MetricCategory.Mind, Count, Sum, MentalFamily.Name, "RWS_Metric_InspirationsByDef", "RWS_MetricDesc_InspirationsByDef", breakdown: true),
    };
}
