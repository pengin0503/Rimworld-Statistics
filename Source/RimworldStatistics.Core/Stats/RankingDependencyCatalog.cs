namespace RimworldStatistics.Core;

/// <summary>
/// Phase D dependency ranks. Each one remains unverified (and thus absent from
/// the normal UI) until its B/C input acquisition is confirmed in the game.
/// Keys are literal so static translation-key auditing can prove completeness.
/// </summary>
public static class RankingDependencyCatalog
{
    private static MetricDefinition Rank(string key, string ledger, MetricUnit unit,
        MetricProvenance provenance, string acquisition, string label, string description)
        => new()
        {
            Key = key, StoredKey = key, LedgerIds = ledger,
            Category = MetricCategory.Rankings, Unit = unit, Provenance = provenance,
            Overall = OverallAggregation.Ranking, Acquisition = acquisition,
            LabelKey = label, DescriptionKey = description,
        };

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        Rank(MetricRankings.DaysByBiome, "N1-F-001", MetricUnit.Days, MetricProvenance.SaveSnapshot,
            "colony_start", "RWS_Metric_RankDaysByBiome", "RWS_MetricDesc_RankDaysByBiome") with { HasBreakdown = true },
        Rank(MetricRankings.EndingsByBiome, "N1-F-002", MetricUnit.Count, MetricProvenance.SaveSnapshot,
            "colony_start", "RWS_Metric_RankEndingsByBiome", "RWS_MetricDesc_RankEndingsByBiome") with { HasBreakdown = true },
        Rank(MetricRankings.KillsToDeaths, "R-012", MetricUnit.Count, MetricProvenance.LiveAggregate,
            HealthDeathFamily.Name, "RWS_Metric_RankKillsToDeaths", "RWS_MetricDesc_RankKillsToDeaths"),
        Rank(MetricRankings.MostKills, "R-001", MetricUnit.Count, MetricProvenance.LiveAggregate,
            HealthDeathFamily.Name, "RWS_Metric_RankMostKills", "RWS_MetricDesc_RankMostKills"),
        Rank(MetricRankings.LongestDeathFree, "R-002", MetricUnit.Days, MetricProvenance.LiveAggregate,
            HealthDeathFamily.Name, "RWS_Metric_RankDeathFree", "RWS_MetricDesc_RankDeathFree"),
        Rank(MetricRankings.MostCrafting, "R-004", MetricUnit.Count, MetricProvenance.LiveAggregate,
            WorkFamily.Name, "RWS_Metric_RankMostCrafting", "RWS_MetricDesc_RankMostCrafting"),
        Rank(MetricRankings.LargestSale, "R-005", MetricUnit.Silver, MetricProvenance.LiveAggregate,
            TradeFamily.Name, "RWS_Metric_RankLargestSale", "RWS_MetricDesc_RankLargestSale"),
        Rank(MetricRankings.MostResearch, "R-006", MetricUnit.Count, MetricProvenance.SaveSnapshot,
            "research_state", "RWS_Metric_RankMostResearch", "RWS_MetricDesc_RankMostResearch"),
    };
}
