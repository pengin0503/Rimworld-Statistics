namespace RimworldStatistics.Core;

public enum MetricCategory
{
    Wealth,
    Population,
    Defense,
    Quests,
    Colony,
    Rankings,
}

public enum MetricUnit
{
    /// <summary>People, quests, mods: whole numbers, or one decimal for means.</summary>
    Count,

    /// <summary>Silver.</summary>
    Silver,

    /// <summary>Game days (60000 ticks), or nominal observation days of the history.</summary>
    Days,

    /// <summary>A fraction shown as a percentage.</summary>
    Percent,

    /// <summary>Colonist mood on the game's 0–100 scale.</summary>
    Mood,

    /// <summary>Events per game year (60 days).</summary>
    PerYear,

    /// <summary>Hours of real play time.</summary>
    Hours,

    /// <summary>Silver per game year.</summary>
    SilverPerYear,
}

/// <summary>Which of a colony's observations gives its value.</summary>
public enum ColonySelection
{
    /// <summary>The representative: latest tick, then latest time, then smallest key.</summary>
    Representative,

    /// <summary>The largest value over all observations of the colony (ledger "観測横断").</summary>
    ObservationMax,

    /// <summary>The smallest value over all observations of the colony.</summary>
    ObservationMin,
}

/// <summary>How colony values combine into the all-colonies value (numeric expansion design §3).</summary>
public enum OverallAggregation
{
    Sum,
    Max,
    Min,

    /// <summary>Summed numerators ÷ summed denominators (rates and means).</summary>
    Ratio,

    /// <summary>Not combined across colonies (start values, nominal days, labels).</summary>
    IndividualOnly,

    /// <summary>A cross-colony comparison computed by <see cref="MetricRankings"/>; not listed per colony.</summary>
    Ranking,
}

/// <summary>Where a value comes from, shown next to its coverage.</summary>
public enum MetricProvenance
{
    VanillaCounter,
    SaveHistory,
    SaveSnapshot,
    LiveAggregate,
}

/// <summary>A shipped output: its stored key, interpretation, unit, aggregation and translation keys.</summary>
public sealed record MetricDefinition
{
    public required string Key { get; init; }

    /// <summary>The key stored in <see cref="MetricObservation.Metrics"/> (several outputs can share one).</summary>
    public required string StoredKey { get; init; }

    /// <summary>Candidate ids of the numeric statistics ledger this output implements.</summary>
    public required string LedgerIds { get; init; }

    public required MetricCategory Category { get; init; }
    public required MetricUnit Unit { get; init; }
    public required MetricProvenance Provenance { get; init; }
    public MetricObservationSource Source { get; init; } = MetricObservationSource.SaveFile;
    public ColonySelection Selection { get; init; } = ColonySelection.Representative;
    public required OverallAggregation Overall { get; init; }

    /// <summary>Values may be negative (net change, growth rate).</summary>
    public bool Signed { get; init; }

    /// <summary>The value has a breakdown (e.g. per quest root def); the scalar is their sum.</summary>
    public bool HasBreakdown { get; init; }

    /// <summary>Package id of the DLC the output needs; null for Core.</summary>
    public string? RequiredDlc { get; init; }

    public required string LabelKey { get; init; }
    public required string DescriptionKey { get; init; }

    public int Revision => MetricKeys.Revision(StoredKey);
}

/// <summary>
/// The outputs whose acquisition was verified and shipped. An output stays registered even when its DLC or hook is off
/// later, so recorded values keep their meaning; acquisition gates only stop new values. Order is the display order.
/// </summary>
public static class MetricRegistry
{
    private static MetricDefinition Def(string key, string ledger, MetricCategory category, MetricUnit unit, MetricProvenance provenance,
        OverallAggregation overall, string label, string description, string? stored = null, ColonySelection selection = ColonySelection.Representative,
        bool signed = false, bool breakdown = false)
        => new()
        {
            Key = key, StoredKey = stored ?? key, LedgerIds = ledger, Category = category, Unit = unit, Provenance = provenance,
            Overall = overall, LabelKey = label, DescriptionKey = description, Selection = selection, Signed = signed, HasBreakdown = breakdown,
        };

    private const MetricCategory W = MetricCategory.Wealth;
    private const MetricCategory P = MetricCategory.Population;
    private const MetricProvenance H = MetricProvenance.SaveHistory;
    private const MetricProvenance S = MetricProvenance.SaveSnapshot;
    private const MetricCategory Rk = MetricCategory.Rankings;

    public static IReadOnlyList<MetricDefinition> All { get; } = new[]
    {
        // Phase A · wealth
        Def(MetricKeys.WealthItemsPeak, "H-001", W, MetricUnit.Silver, H, OverallAggregation.Max, "RWS_Metric_WealthItemsPeak", "RWS_MetricDesc_WealthItemsPeak"),
        Def(MetricKeys.WealthItemsLatest, "H-001", W, MetricUnit.Silver, H, OverallAggregation.Sum, "RWS_Metric_WealthItemsLatest", "RWS_MetricDesc_WealthItemsLatest"),
        Def(MetricKeys.WealthBuildingsPeak, "H-002", W, MetricUnit.Silver, H, OverallAggregation.Max, "RWS_Metric_WealthBuildingsPeak", "RWS_MetricDesc_WealthBuildingsPeak"),
        Def(MetricKeys.WealthBuildingsLatest, "H-002", W, MetricUnit.Silver, H, OverallAggregation.Sum, "RWS_Metric_WealthBuildingsLatest", "RWS_MetricDesc_WealthBuildingsLatest"),
        Def(MetricKeys.WealthPawnsPeak, "H-003", W, MetricUnit.Silver, H, OverallAggregation.Max, "RWS_Metric_WealthPawnsPeak", "RWS_MetricDesc_WealthPawnsPeak"),
        Def(MetricKeys.WealthPawnsLatest, "H-003", W, MetricUnit.Silver, H, OverallAggregation.Sum, "RWS_Metric_WealthPawnsLatest", "RWS_MetricDesc_WealthPawnsLatest"),
        Def(MetricKeys.WealthTotalMean, "H-004", W, MetricUnit.Silver, H, OverallAggregation.Ratio, "RWS_Metric_WealthTotalMean", "RWS_MetricDesc_WealthTotalMean"),
        Def(MetricKeys.WealthTotalFirst, "H-006", W, MetricUnit.Silver, H, OverallAggregation.IndividualOnly, "RWS_Metric_WealthTotalFirst", "RWS_MetricDesc_WealthTotalFirst"),
        Def(MetricKeys.WealthTotalNet, "H-007", W, MetricUnit.Silver, H, OverallAggregation.Sum, "RWS_Metric_WealthTotalNet", "RWS_MetricDesc_WealthTotalNet", signed: true),
        Def(MetricKeys.WealthTotalPeakDay, "H-008", W, MetricUnit.Days, H, OverallAggregation.IndividualOnly, "RWS_Metric_WealthTotalPeakDay", "RWS_MetricDesc_WealthTotalPeakDay"),
        Def(MetricKeys.WealthTotalMaxGainHalfDay, "H-009", W, MetricUnit.Silver, H, OverallAggregation.Max, "RWS_Metric_WealthTotalMaxGainHalfDay", "RWS_MetricDesc_WealthTotalMaxGainHalfDay"),
        Def(MetricKeys.WealthTotalMaxLossHalfDay, "H-010", W, MetricUnit.Silver, H, OverallAggregation.Max, "RWS_Metric_WealthTotalMaxLossHalfDay", "RWS_MetricDesc_WealthTotalMaxLossHalfDay"),
        Def(MetricKeys.WealthTotalMaxLossRateHalfDay, "H-011", W, MetricUnit.Percent, H, OverallAggregation.Max, "RWS_Metric_WealthTotalMaxLossRateHalfDay", "RWS_MetricDesc_WealthTotalMaxLossRateHalfDay"),
        Def(MetricKeys.WealthTotalMaxGain15D, "H-012", W, MetricUnit.Silver, H, OverallAggregation.Max, "RWS_Metric_WealthTotalMaxGain15D", "RWS_MetricDesc_WealthTotalMaxGain15D"),
        Def(MetricKeys.WealthTotalMaxLoss15D, "H-013", W, MetricUnit.Silver, H, OverallAggregation.Max, "RWS_Metric_WealthTotalMaxLoss15D", "RWS_MetricDesc_WealthTotalMaxLoss15D"),
        Def(MetricKeys.WealthTotalMaxGrowthRateYear, "H-014", W, MetricUnit.Percent, H, OverallAggregation.Max, "RWS_Metric_WealthTotalMaxGrowthRateYear", "RWS_MetricDesc_WealthTotalMaxGrowthRateYear", signed: true),
        Def(MetricKeys.WealthTotalLongestGrowth, "H-015", W, MetricUnit.Days, H, OverallAggregation.Max, "RWS_Metric_WealthTotalLongestGrowth", "RWS_MetricDesc_WealthTotalLongestGrowth"),
        Def(MetricKeys.WealthTotalFirst100K, "H-016", W, MetricUnit.Days, H, OverallAggregation.Min, "RWS_Metric_WealthTotalFirst100K", "RWS_MetricDesc_WealthTotalFirst100K"),
        Def(MetricKeys.WealthTotalFirst1M, "H-018", W, MetricUnit.Days, H, OverallAggregation.Min, "RWS_Metric_WealthTotalFirst1M", "RWS_MetricDesc_WealthTotalFirst1M"),
        Def(MetricKeys.WealthBuildingsMaxShare, "H-019", W, MetricUnit.Percent, H, OverallAggregation.Max, "RWS_Metric_WealthBuildingsMaxShare", "RWS_MetricDesc_WealthBuildingsMaxShare"),

        // Phase A · population
        Def(MetricKeys.FreeColonistsMean, "H-020", P, MetricUnit.Count, H, OverallAggregation.Ratio, "RWS_Metric_FreeColonistsMean", "RWS_MetricDesc_FreeColonistsMean"),
        Def(MetricKeys.FreeColonistsMin, "H-021", P, MetricUnit.Count, H, OverallAggregation.Min, "RWS_Metric_FreeColonistsMin", "RWS_MetricDesc_FreeColonistsMin"),
        Def(MetricKeys.FreeColonistsFirst10, "H-023", P, MetricUnit.Days, H, OverallAggregation.Min, "RWS_Metric_FreeColonistsFirst10", "RWS_MetricDesc_FreeColonistsFirst10"),
        Def(MetricKeys.PrisonersPeak, "H-024", P, MetricUnit.Count, H, OverallAggregation.Max, "RWS_Metric_PrisonersPeak", "RWS_MetricDesc_PrisonersPeak"),
        Def(MetricKeys.PrisonersMean, "H-025", P, MetricUnit.Count, H, OverallAggregation.Ratio, "RWS_Metric_PrisonersMean", "RWS_MetricDesc_PrisonersMean"),
        Def(MetricKeys.MoodMean, "H-026", P, MetricUnit.Mood, H, OverallAggregation.Ratio, "RWS_Metric_MoodMean", "RWS_MetricDesc_MoodMean"),
        Def(MetricKeys.MoodMin, "H-027", P, MetricUnit.Mood, H, OverallAggregation.Min, "RWS_Metric_MoodMin", "RWS_MetricDesc_MoodMin"),
        Def(MetricKeys.MoodMax, "H-028", P, MetricUnit.Mood, H, OverallAggregation.Max, "RWS_Metric_MoodMax", "RWS_MetricDesc_MoodMax"),
        Def(MetricKeys.MoodBelow50Days, "H-029", P, MetricUnit.Days, H, OverallAggregation.Sum, "RWS_Metric_MoodBelow50Days", "RWS_MetricDesc_MoodBelow50Days"),
        Def(MetricKeys.MoodAtLeast80Days, "H-030", P, MetricUnit.Days, H, OverallAggregation.Sum, "RWS_Metric_MoodAtLeast80Days", "RWS_MetricDesc_MoodAtLeast80Days"),
        Def(MetricKeys.DeathsPerYear, "H-032", P, MetricUnit.PerYear, MetricProvenance.VanillaCounter, OverallAggregation.Ratio, "RWS_Metric_DeathsPerYear", "RWS_MetricDesc_DeathsPerYear"),

        // Phase A · vanilla counters and save metadata
        Def(MetricKeys.RaidsPerYear, "H-031", MetricCategory.Defense, MetricUnit.PerYear, MetricProvenance.VanillaCounter, OverallAggregation.Ratio, "RWS_Metric_RaidsPerYear", "RWS_MetricDesc_RaidsPerYear"),
        Def(MetricKeys.ModsLatest, "H-033", MetricCategory.Colony, MetricUnit.Count, S, OverallAggregation.IndividualOnly, "RWS_Metric_ModsLatest", "RWS_MetricDesc_ModsLatest"),
        Def("colony.mods.peak", "H-033", MetricCategory.Colony, MetricUnit.Count, S, OverallAggregation.IndividualOnly, "RWS_Metric_ModsPeak", "RWS_MetricDesc_ModsPeak",
            stored: MetricKeys.ModsLatest, selection: ColonySelection.ObservationMax),
        Def("colony.mods.min", "H-033", MetricCategory.Colony, MetricUnit.Count, S, OverallAggregation.IndividualOnly, "RWS_Metric_ModsMin", "RWS_MetricDesc_ModsMin",
            stored: MetricKeys.ModsLatest, selection: ColonySelection.ObservationMin),

        // Phase A · retained quests
        Def(MetricKeys.QuestSuccess, "S-001", MetricCategory.Quests, MetricUnit.Count, S, OverallAggregation.Sum, "RWS_Metric_QuestSuccess", "RWS_MetricDesc_QuestSuccess"),
        Def(MetricKeys.QuestFailure, "S-001", MetricCategory.Quests, MetricUnit.Count, S, OverallAggregation.Sum, "RWS_Metric_QuestFailure", "RWS_MetricDesc_QuestFailure"),
        Def(MetricKeys.QuestSuccessRate, "S-002", MetricCategory.Quests, MetricUnit.Percent, S, OverallAggregation.Ratio, "RWS_Metric_QuestSuccessRate", "RWS_MetricDesc_QuestSuccessRate"),
        Def(MetricKeys.QuestSuccessByRoot, "Q-006", MetricCategory.Quests, MetricUnit.Count, S, OverallAggregation.Sum, "RWS_Metric_QuestSuccessByRoot", "RWS_MetricDesc_QuestSuccessByRoot", breakdown: true),
        Def(MetricKeys.QuestExpired, "Q-007", MetricCategory.Quests, MetricUnit.Count, S, OverallAggregation.Sum, "RWS_Metric_QuestExpired", "RWS_MetricDesc_QuestExpired"),
        Def(MetricKeys.QuestAccepted, "N3-X-001", MetricCategory.Quests, MetricUnit.Count, S, OverallAggregation.Sum, "RWS_Metric_QuestAccepted", "RWS_MetricDesc_QuestAccepted"),
        Def(MetricKeys.QuestSuccessDurationMean, "N3-X-003", MetricCategory.Quests, MetricUnit.Days, S, OverallAggregation.Ratio, "RWS_Metric_QuestSuccessDurationMean", "RWS_MetricDesc_QuestSuccessDurationMean"),
        Def(MetricKeys.QuestSuccessDurationMin, "N3-X-004", MetricCategory.Quests, MetricUnit.Days, S, OverallAggregation.Min, "RWS_Metric_QuestSuccessDurationMin", "RWS_MetricDesc_QuestSuccessDurationMin"),
        Def(MetricKeys.QuestSuccessDurationMax, "N3-X-005", MetricCategory.Quests, MetricUnit.Days, S, OverallAggregation.Max, "RWS_Metric_QuestSuccessDurationMax", "RWS_MetricDesc_QuestSuccessDurationMax"),

        // Phase D1 · rankings from v1 records and Phase A observations (R-011 is not supported and has no row)
        Def(MetricRankings.ByDifficultyDays, "S-003", Rk, MetricUnit.Days, S, OverallAggregation.Ranking, "RWS_Metric_RankByDifficultyDays", "RWS_MetricDesc_RankByDifficultyDays", breakdown: true),
        Def(MetricRankings.ByDifficultyEndings, "S-003, R-015", Rk, MetricUnit.Count, MetricProvenance.LiveAggregate, OverallAggregation.Ranking, "RWS_Metric_RankByDifficultyEndings", "RWS_MetricDesc_RankByDifficultyEndings", breakdown: true),
        Def(MetricRankings.ByPermadeathDays, "S-004", Rk, MetricUnit.Days, S, OverallAggregation.Ranking, "RWS_Metric_RankByPermadeathDays", "RWS_MetricDesc_RankByPermadeathDays", breakdown: true),
        Def(MetricRankings.ByPermadeathEndings, "S-004", Rk, MetricUnit.Count, MetricProvenance.LiveAggregate, OverallAggregation.Ranking, "RWS_Metric_RankByPermadeathEndings", "RWS_MetricDesc_RankByPermadeathEndings", breakdown: true),
        Def(MetricRankings.PopulationPeak, "R-003", Rk, MetricUnit.Count, MetricProvenance.VanillaCounter, OverallAggregation.Ranking, "RWS_Metric_RankPopulationPeak", "RWS_MetricDesc_RankPopulationPeak"),
        Def(MetricRankings.FastestEnding, "R-007", Rk, MetricUnit.Days, MetricProvenance.LiveAggregate, OverallAggregation.Ranking, "RWS_Metric_RankFastestEnding", "RWS_MetricDesc_RankFastestEnding"),
        Def(MetricRankings.NetWealthPerYear, "R-010", Rk, MetricUnit.SilverPerYear, H, OverallAggregation.Ranking, "RWS_Metric_RankNetWealthPerYear", "RWS_MetricDesc_RankNetWealthPerYear",
            stored: MetricKeys.WealthTotalNet, signed: true),
        Def(MetricRankings.TopScenario, "R-013", Rk, MetricUnit.Count, MetricProvenance.LiveAggregate, OverallAggregation.Ranking, "RWS_Metric_RankTopScenario", "RWS_MetricDesc_RankTopScenario", breakdown: true),
        Def(MetricRankings.DaysByStoryteller, "R-014", Rk, MetricUnit.Days, S, OverallAggregation.Ranking, "RWS_Metric_RankDaysByStoryteller", "RWS_MetricDesc_RankDaysByStoryteller", breakdown: true),
        Def(MetricRankings.PlaytimePeak, "R-016", Rk, MetricUnit.Hours, MetricProvenance.VanillaCounter, OverallAggregation.Ranking, "RWS_Metric_RankPlaytimePeak", "RWS_MetricDesc_RankPlaytimePeak"),
        Def(MetricKeys.WealthPerColonistMax, "R-009", Rk, MetricUnit.Silver, H, OverallAggregation.Ranking, "RWS_Metric_RankWealthPerColonist", "RWS_MetricDesc_RankWealthPerColonist"),
        Def(MetricRankings.MinDeathsAtEnding, "R-008", Rk, MetricUnit.Count, MetricProvenance.LiveAggregate, OverallAggregation.Ranking, "RWS_Metric_RankMinDeathsAtEnding", "RWS_MetricDesc_RankMinDeathsAtEnding"),
        Def(MetricRankings.ZeroDeathEndings, "N1-F-003", Rk, MetricUnit.Count, MetricProvenance.LiveAggregate, OverallAggregation.Ranking, "RWS_Metric_RankZeroDeathEndings", "RWS_MetricDesc_RankZeroDeathEndings"),
    };

    private static readonly Dictionary<string, MetricDefinition> ByKey = All.ToDictionary(d => d.Key, StringComparer.Ordinal);

    public static MetricDefinition? Find(string key) => ByKey.GetValueOrDefault(key);
}
