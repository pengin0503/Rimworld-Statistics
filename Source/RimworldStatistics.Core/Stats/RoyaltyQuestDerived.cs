namespace RimworldStatistics.Core;

/// <summary>
/// Strictly attributed Imperial quest successes from retained Quest.root Defs.
/// The 1.7.4985 Royalty QuestScriptDefs prove the listed roots are imperial;
/// generic hospitality/monuments cannot be attributed without a saved faction,
/// so this metric remains Partial even when the retained manager is readable.
/// </summary>
public static class RoyaltyQuestDerived
{
    public const string Success = "royalty.quest_success";

    private static readonly HashSet<string> ImperialRoots = new(StringComparer.Ordinal)
    {
        "EndGame_RoyalAscent",
        "BestowingCeremony",
        "ChangeRoyalHeir",
        "Decree_ProduceItem",
        "Decree_HarvestCrop",
        "Decree_HuntAnimal",
        "Decree_BuildMonument",
    };

    public static MetricEntry Build(IReadOnlyList<RetainedQuest> retained)
    {
        var completed = retained.Count(q => q.Accepted && q.Ended
            && q.Outcome == "Success" && q.Root is not null && ImperialRoots.Contains(q.Root));
        return MetricEntry.Of(MetricKeys.Revision(Success), new MetricValue
        {
            IntegerValue = completed,
            // Other empire quests may have generic roots (e.g. Hospitality);
            // don't claim full lifetime/cross-root coverage of those successes.
            Coverage = MetricCoverage.Partial,
        });
    }

    public static MetricDefinition Definition { get; } = new()
    {
        Key = Success, StoredKey = Success, LedgerIds = "L-003",
        Category = MetricCategory.Royalty, Unit = MetricUnit.Count,
        Provenance = MetricProvenance.SaveSnapshot, Source = MetricObservationSource.SaveFile,
        Overall = OverallAggregation.Sum, Acquisition = "royalty_quests",
        RequiredDlc = AcquisitionUnits.Royalty,
        LabelKey = "RWS_Metric_RoyaltyQuestSuccess",
        DescriptionKey = "RWS_MetricDesc_RoyaltyQuestSuccess",
    };
}
