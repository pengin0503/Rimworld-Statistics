namespace RimworldStatistics.Core;

/// <summary>
/// Phase D1 rankings whose inputs are Phase B/C data. Register for development
/// inspection, but publish only after the input acquisition unit has passed game
/// verification; a missing observation is never a zero-valued candidate.
/// </summary>
public static class RankingDependencyCatalog
{
    private static MetricDefinition Rank(string key, string ledger, MetricUnit unit,
        MetricProvenance source, string acquisition, string suffix)
        => new()
        {
            Key = key, StoredKey = key, LedgerIds = ledger,
            Category = MetricCategory.Rankings, Unit = unit, Provenance = source,
            Overall = OverallAggregation.Ranking, Acquisition = acquisition,
            LabelKey = "RWS_Metric_Rank" + suffix,
            DescriptionKey = "RWS_MetricDesc_Rank" + suffix,
        };

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        Rank(MetricRankings.MostKills, "R-001", MetricUnit.Count,
            MetricProvenance.LiveAggregate, HealthDeathFamily.Name, "MostKills"),
        Rank(MetricRankings.LongestDeathFree, "R-002", MetricUnit.Days,
            MetricProvenance.LiveAggregate, HealthDeathFamily.Name, "DeathFree"),
        Rank(MetricRankings.MostCrafting, "R-004", MetricUnit.Count,
            MetricProvenance.LiveAggregate, WorkFamily.Name, "MostCrafting"),
        Rank(MetricRankings.LargestSale, "R-005", MetricUnit.Silver,
            MetricProvenance.LiveAggregate, TradeFamily.Name, "LargestSale"),
        Rank(MetricRankings.MostResearch, "R-006", MetricUnit.Count,
            MetricProvenance.SaveSnapshot, "research_state", "MostResearch"),
    };
}
