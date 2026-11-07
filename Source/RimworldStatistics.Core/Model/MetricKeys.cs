namespace RimworldStatistics.Core;

/// <summary>
/// Stable keys of the values stored in a <see cref="MetricObservation"/> (the metric keys of the numeric statistics
/// ledger) and their definition revisions. A key is never reused for a different definition: a changed definition gets a
/// higher revision, and values of different revisions are never combined.
/// </summary>
public static class MetricKeys
{
    /// <summary>Breakdown key of a value whose category is not known (e.g. a quest without a root def).</summary>
    public const string UnknownBucket = "?";

    // Phase A · wealth (history recorders Wealth_*)
    public const string WealthItemsPeak = "wealth.items.peak";
    public const string WealthItemsLatest = "wealth.items.latest";
    public const string WealthBuildingsPeak = "wealth.buildings.peak";
    public const string WealthBuildingsLatest = "wealth.buildings.latest";
    public const string WealthPawnsPeak = "wealth.pawns.peak";
    public const string WealthPawnsLatest = "wealth.pawns.latest";
    public const string WealthTotalMean = "wealth.total.mean";
    public const string WealthTotalFirst = "wealth.total.first";
    public const string WealthTotalNet = "wealth.total.net";
    public const string WealthTotalPeakDay = "wealth.total.peak_day";
    public const string WealthTotalMaxGainHalfDay = "wealth.total.max_gain_half_day";
    public const string WealthTotalMaxLossHalfDay = "wealth.total.max_loss_half_day";
    public const string WealthTotalMaxLossRateHalfDay = "wealth.total.max_loss_rate_half_day";
    public const string WealthTotalMaxGain15D = "wealth.total.max_gain_15d";
    public const string WealthTotalMaxLoss15D = "wealth.total.max_loss_15d";
    public const string WealthTotalMaxGrowthRateYear = "wealth.total.max_growth_rate_year";
    public const string WealthTotalLongestGrowth = "wealth.total.longest_growth";
    public const string WealthTotalFirst100K = "wealth.total.first_100k";
    public const string WealthTotalFirst1M = "wealth.total.first_1m";
    public const string WealthBuildingsMaxShare = "wealth.buildings.max_share";

    /// <summary>Same-index Wealth_Total ÷ FreeColonists maximum (ledger R-009, shown with the Phase D rankings).</summary>
    public const string WealthPerColonistMax = "ranking.wealth.per_colonist";

    // Phase A · population (FreeColonists, Prisoners, ColonistMood)
    public const string FreeColonistsMean = "population.free.mean";
    public const string FreeColonistsMin = "population.free.min";
    public const string FreeColonistsFirst10 = "population.free.first_10";
    public const string PrisonersPeak = "population.prisoners.peak";
    public const string PrisonersMean = "population.prisoners.mean";
    public const string MoodMean = "population.mood.mean";
    public const string MoodMin = "population.mood.min";
    public const string MoodMax = "population.mood.max";
    public const string MoodBelow50Days = "population.mood.below_50d";
    public const string MoodAtLeast80Days = "population.mood.at_least_80d";

    // Phase A · vanilla counters and save metadata
    public const string RaidsPerYear = "defense.raids_per_year";
    public const string DeathsPerYear = "population.deaths_per_year";
    public const string ModsLatest = "colony.mods.latest";

    // Phase A · retained quests
    public const string QuestSuccess = "quest.retained.success";
    public const string QuestFailure = "quest.retained.failure";
    public const string QuestSuccessRate = "quest.retained.success_rate";
    public const string QuestSuccessByRoot = "quest.retained.success_by_root";
    public const string QuestExpired = "quest.retained.expired";
    public const string QuestAccepted = "quest.retained.accepted";
    public const string QuestSuccessDurationMean = "quest.retained.success_duration_mean";
    public const string QuestSuccessDurationMin = "quest.retained.success_duration_min";
    public const string QuestSuccessDurationMax = "quest.retained.success_duration_max";

    /// <summary>The current definition revision of a stored key. Every key starts at 1.</summary>
    public static int Revision(string key) => 1;
}
