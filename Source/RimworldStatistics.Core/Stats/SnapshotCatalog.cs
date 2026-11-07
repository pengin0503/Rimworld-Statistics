namespace RimworldStatistics.Core;

/// <summary>Phase B snapshots are independent from event checkpoints and remain hidden until verified.</summary>
public static class SnapshotCatalog
{
    private static MetricDefinition Snap(string key, string ledger, MetricUnit metricUnit, OverallAggregation aggregation,
        string unit, string label, string description, MetricCategory category = MetricCategory.Research, bool breakdown = false, ColonySelection selection = ColonySelection.Representative)
        => new()
        {
            Key = key, StoredKey = key, LedgerIds = ledger, Category = category, Unit = metricUnit,
            Provenance = MetricProvenance.SaveSnapshot, Source = MetricObservationSource.SaveSnapshot,
            Overall = aggregation, Acquisition = unit, HasBreakdown = breakdown, Selection = selection,
            LabelKey = label, DescriptionKey = description,
        };

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        Snap(ResearchStateSnapshot.Finished, "S-012", MetricUnit.Count, OverallAggregation.Sum, "research_state", "RWS_Metric_ResearchSnapshotFinished", "RWS_MetricDesc_ResearchSnapshotFinished"),
        Snap(ResearchStateSnapshot.FinishedRate, "S-012", MetricUnit.Percent, OverallAggregation.Ratio, "research_state", "RWS_Metric_ResearchSnapshotFinishedRate", "RWS_MetricDesc_ResearchSnapshotFinishedRate"),
        Snap(ResearchStateSnapshot.TechPeak, "S-013", MetricUnit.Count, OverallAggregation.Max, "research_state", "RWS_Metric_ResearchSnapshotTechPeak", "RWS_MetricDesc_ResearchSnapshotTechPeak", breakdown: true),
        Snap(FactionStateSnapshot.Relations, "S-014", MetricUnit.Count, OverallAggregation.Sum, "factions", "RWS_Metric_FactionSnapshotRelations", "RWS_MetricDesc_FactionSnapshotRelations", category: MetricCategory.Economy, breakdown: true),
        Snap(FactionStateSnapshot.GoodwillRange, "S-015", MetricUnit.Count, OverallAggregation.IndividualOnly, "factions", "RWS_Metric_FactionSnapshotGoodwillRange", "RWS_MetricDesc_FactionSnapshotGoodwillRange", category: MetricCategory.Economy, breakdown: true),
        Snap(RosterSnapshot.Free, "S-005", MetricUnit.Count, OverallAggregation.Sum, "roster", "RWS_Metric_RosterFree", "RWS_MetricDesc_RosterFree", category: MetricCategory.Population),
        Snap(RosterSnapshot.Prisoners, "S-006", MetricUnit.Count, OverallAggregation.Sum, "roster", "RWS_Metric_RosterPrisoners", "RWS_MetricDesc_RosterPrisoners", category: MetricCategory.Population),
        Snap(RosterSnapshot.Animals, "S-007", MetricUnit.Count, OverallAggregation.Sum, "roster", "RWS_Metric_RosterAnimals", "RWS_MetricDesc_RosterAnimals", category: MetricCategory.Animals),
        Snap(RosterSnapshot.Mechs, "S-008", MetricUnit.Count, OverallAggregation.Sum, "roster", "RWS_Metric_RosterMechs", "RWS_MetricDesc_RosterMechs", category: MetricCategory.Population),
        Snap(RosterSnapshot.AugmentedPeak, "S-021", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterAugmentedPeak", "RWS_MetricDesc_RosterAugmentedPeak", category: MetricCategory.Population, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.Skill20PawnsPeak, "S-022", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterSkill20PawnsPeak", "RWS_MetricDesc_RosterSkill20PawnsPeak", category: MetricCategory.Population, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.Skill20PerPawnPeak, "S-023", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterSkill20PerPawnPeak", "RWS_MetricDesc_RosterSkill20PerPawnPeak", category: MetricCategory.Population, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.Oldest, "N1-C-001", MetricUnit.Years, OverallAggregation.Max, "roster", "RWS_Metric_RosterOldest", "RWS_MetricDesc_RosterOldest", category: MetricCategory.Population, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.AnimalDefsPeak, "N1-D-008", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterAnimalDefsPeak", "RWS_MetricDesc_RosterAnimalDefsPeak", category: MetricCategory.Animals, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.Passions, "N3-W-010", MetricUnit.Count, OverallAggregation.IndividualOnly, "roster", "RWS_Metric_RosterPassions", "RWS_MetricDesc_RosterPassions", category: MetricCategory.Population, breakdown: true),
        Snap(RosterSnapshot.AgeMean, "N4-P-011", MetricUnit.Years, OverallAggregation.Ratio, "roster", "RWS_Metric_RosterAgeMean", "RWS_MetricDesc_RosterAgeMean", category: MetricCategory.Population),
        Snap(RosterSnapshot.InjuredPeak, "P-019", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterInjuredPeak", "RWS_MetricDesc_RosterInjuredPeak", category: MetricCategory.Medicine, selection: ColonySelection.ObservationMax),
    };
}
