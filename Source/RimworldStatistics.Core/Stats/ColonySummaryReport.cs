namespace RimworldStatistics.Core;

/// <summary>Distinguishes how a per-colony metric value should be read in a colony summary report (V2-C).</summary>
public enum ColonyReportValueKind
{
    CumulativeTotal,
    PeakOrExtreme,
    LatestSnapshot,
    MeanOrRate,
}

/// <summary>Why a colony does or does not have a cross-colony rank for a metric in the active scope.</summary>
public enum ColonyRankStatus
{
    Ranked,
    Excluded,
    OutOfScope,
    Unmeasured,
    NotApplicable,
}

/// <summary>Overview and lineage metadata of one colony for <see cref="ColonySummaryReport"/>.</summary>
public sealed record ColonyReportOverview
{
    public required string ColonyKey { get; init; }
    public string? FactionName { get; init; }
    public string? WorldName { get; init; }
    public string? Scenario { get; init; }
    public string? Storyteller { get; init; }
    public string? Difficulty { get; init; }
    public bool? Permadeath { get; init; }
    public bool HasObservedPoint { get; init; }
    public bool Excluded { get; init; }
    public bool OutOfScope { get; init; }
    public int MaxTicks { get; init; }
    public double MaxRealPlaySeconds { get; init; }
    public int SaveCount { get; init; }
    public DateTime? LastPlayedUtc { get; init; }
    public required IReadOnlyList<string> GameVersions { get; init; }
    public required IReadOnlyDictionary<EndingType, int> EndingCounts { get; init; }
    public required IReadOnlyList<EndingEvent> CountedEndings { get; init; }
    public required IReadOnlyList<EndingType> UnexplainedTraces { get; init; }
    public int RecoveredEndingCount { get; init; }
    public bool HasDevOnlyEndings { get; init; }
    public CheckpointLineage LineageState { get; init; } = CheckpointLineage.None;
    public int LineageNodeCount { get; init; }
    public int LineageBranchCount { get; init; }
    public int LineageMissingParentCount { get; init; }
    public int LineageUnprovenSaveCount { get; init; }
}

/// <summary>One metric entry in a <see cref="ColonySummaryReport"/>, combining value, coverage, observation range and cross-colony rank.</summary>
public sealed record ColonyReportMetricItem
{
    public required string MetricKey { get; init; }
    public required MetricDefinition Definition { get; init; }
    public required bool IsBasic { get; init; }
    public required ColonyReportValueKind ValueKind { get; init; }
    public required NumericValue Value { get; init; }
    public required ColonyRankStatus RankStatus { get; init; }
    public int? Rank { get; init; }
    public int MeasuredColonies { get; init; }
    public int TargetColonies { get; init; }
    public bool RankHasPartial { get; init; }
}

/// <summary>Counts of numeric metrics by coverage and evidence quality for one colony.</summary>
public sealed record ColonyReportCoverageSummary(
    int TotalMetrics,
    int CompleteCount,
    int SnapshotOnlyCount,
    int PartialCount,
    int UnknownCount,
    int NotApplicableCount,
    int ExperimentalCount);

/// <summary>
/// Read-only single-colony lifetime summary report (V2-C).
/// Aggregates existing <see cref="StatsView"/>, <see cref="MetricLookup"/>, <see cref="LeaderboardBuilder"/> and optional
/// <see cref="ColonySaveLineage"/> without adding or mutating any stored statistics.
/// </summary>
public sealed class ColonySummaryReport
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private static readonly string[] CuratedNumericKeys =
    [
        MetricKeys.WealthTotalMean,
        MetricKeys.WealthPerColonistMax,
        MetricKeys.MoodMean,
        MetricKeys.MoodMin,
        MembershipFamily.FirstJoin,
        MembershipFamily.PrisonerRecruit,
        MembershipFamily.HumanBirths,
        ResearchFamily.Completed,
        HealthDeathFamily.HostileKills,
        RaidFamily.PointsMax,
        WorkFamily.HarvestUnits,
        WorkFamily.MiningUnits,
        WorkFamily.CraftedUnits,
        TradeFamily.SalesTotal,
        TradeFamily.Deals,
        MetricKeys.QuestSuccess,
    ];

    public required ColonyReportOverview Overview { get; init; }
    public required IReadOnlyList<ColonyReportMetricItem> KeyRecords { get; init; }
    public required IReadOnlyList<ColonyReportMetricItem> TopRankedHighlights { get; init; }
    public required IReadOnlyList<ColonyReportMetricItem> ModHighlights { get; init; }
    public required ColonyReportCoverageSummary Coverage { get; init; }

    public static ColonySummaryReport? Build(StatsView view, string colonyKey, ColonySaveLineage? lineage = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(colonyKey);

        var stats = view.Stats.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        if (stats is null)
            return null;

        var colony = view.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        var numericColony = view.Numeric.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        var rowsByKey = numericColony?.Rows.ToDictionary(r => r.Definition.Key, Ord)
            ?? new Dictionary<string, NumericRow>(Ord);
        var explainedTypes = stats.CountedEndings.Select(e => e.Type).ToHashSet();
        var unexplainedTraces = colony?.Traces.Where(t => !explainedTypes.Contains(t)).OrderBy(t => t).ToArray()
            ?? Array.Empty<EndingType>();

        var overview = new ColonyReportOverview
        {
            ColonyKey = stats.Key,
            FactionName = stats.FactionName,
            WorldName = stats.WorldName,
            Scenario = stats.Scenario,
            Storyteller = stats.Storyteller,
            Difficulty = stats.Difficulty,
            Permadeath = stats.Permadeath,
            HasObservedPoint = stats.HasObservedPoint,
            Excluded = stats.Excluded,
            OutOfScope = stats.OutOfScope,
            MaxTicks = stats.MaxTicks,
            MaxRealPlaySeconds = stats.MaxRealPlay,
            SaveCount = stats.SaveCount,
            LastPlayedUtc = stats.LastPlayedUtc,
            GameVersions = stats.GameVersions,
            EndingCounts = stats.EndingCounts,
            CountedEndings = stats.CountedEndings,
            UnexplainedTraces = unexplainedTraces,
            RecoveredEndingCount = colony?.RecoveredEndings.Count ?? 0,
            HasDevOnlyEndings = stats.HasDevOnlyEndings,
            LineageState = lineage?.SummaryLineage ?? CheckpointLineage.None,
            LineageNodeCount = lineage?.Nodes.Count ?? 0,
            LineageBranchCount = lineage?.BranchPointCount ?? 0,
            LineageMissingParentCount = lineage?.MissingParentCount ?? 0,
            LineageUnprovenSaveCount = lineage?.UnprovenNodeCount ?? 0,
        };

        // Cache leaderboards across sections so each metric is ranked at most once.
        var boardCache = new Dictionary<string, Leaderboard>(Ord);
        Leaderboard GetBoard(string key)
        {
            if (!boardCache.TryGetValue(key, out var board))
                boardCache[key] = board = LeaderboardBuilder.Build(view, key, descending: true);
            return board;
        }

        var keyRecords = new List<ColonyReportMetricItem>(BasicMetrics.Keys.Count + CuratedNumericKeys.Length);
        var keyRecordSet = new HashSet<string>(Ord);

        foreach (var basicKey in BasicMetrics.Keys)
        {
            keyRecords.Add(BuildBasicItem(stats, basicKey, GetBoard(basicKey)));
            keyRecordSet.Add(basicKey);
        }

        foreach (var metricKey in CuratedNumericKeys)
        {
            var def = rowsByKey.GetValueOrDefault(metricKey)?.Definition ?? MetricLookup.Definition(view, metricKey);
            if (def is null)
                continue;
            var val = rowsByKey.TryGetValue(metricKey, out var row)
                ? row.Value
                : new NumericValue { Coverage = MetricCoverage.Unknown };
            keyRecords.Add(BuildNumericItem(stats, def, val, GetBoard(metricKey)));
            keyRecordSet.Add(metricKey);
        }

        // Additional top-ranked highlights (#1..#3 among >= 2 measured colonies) and measured MOD metrics.
        var topHighlights = new List<ColonyReportMetricItem>();
        var modHighlights = new List<ColonyReportMetricItem>();

        if (numericColony is not null)
        {
            foreach (var row in numericColony.Rows)
            {
                var def = row.Definition;
                if (def.Overall == OverallAggregation.Ranking || CrossColonyCatalog.IsDerived(def))
                    continue;
                if (!row.Value.Known || row.Value.Number is not { } num || !double.IsFinite(num))
                    continue;

                var isMod = !string.IsNullOrWhiteSpace(def.OwnerPackageId);
                if (isMod)
                {
                    modHighlights.Add(BuildNumericItem(stats, def, row.Value, GetBoard(def.Key)));
                    continue;
                }

                if (keyRecordSet.Contains(def.Key) || stats.Excluded || stats.OutOfScope)
                    continue;

                var board = GetBoard(def.Key);
                if (board.Measured < 2)
                    continue;
                var entry = board.Entries.FirstOrDefault(e => e.ColonyKey == colonyKey);
                if (entry is { Rank: <= 3 })
                    topHighlights.Add(BuildNumericItem(stats, def, row.Value, board));
            }
        }

        var orderedTop = topHighlights
            .OrderBy(i => i.Rank ?? int.MaxValue)
            .ThenByDescending(i => i.MeasuredColonies)
            .ThenBy(i => i.MetricKey, Ord)
            .Take(12)
            .ToArray();

        var orderedMod = modHighlights
            .OrderBy(i => i.Value.Coverage == MetricCoverage.Complete ? 0 : i.Value.Coverage == MetricCoverage.SnapshotOnly ? 1 : 2)
            .ThenBy(i => i.Rank ?? int.MaxValue)
            .ThenBy(i => i.MetricKey, Ord)
            .Take(24)
            .ToArray();

        var coverage = BuildCoverageSummary(numericColony);

        return new ColonySummaryReport
        {
            Overview = overview,
            KeyRecords = keyRecords,
            TopRankedHighlights = orderedTop,
            ModHighlights = orderedMod,
            Coverage = coverage,
        };
    }

    public static ColonyReportValueKind ClassifyValueKind(MetricDefinition definition, bool isBasic = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (isBasic)
        {
            return definition.Key switch
            {
                BasicMetrics.PeakWealth or BasicMetrics.MaxPopulation => ColonyReportValueKind.PeakOrExtreme,
                BasicMetrics.LastWealth => ColonyReportValueKind.LatestSnapshot,
                _ => ColonyReportValueKind.CumulativeTotal,
            };
        }

        var key = definition.Key;
        if (key.EndsWith(".mean", StringComparison.Ordinal)
            || key.EndsWith("_mean", StringComparison.Ordinal)
            || key.EndsWith("_rate", StringComparison.Ordinal)
            || key.Contains("_per_year", StringComparison.Ordinal)
            || definition.Unit is MetricUnit.Percent or MetricUnit.Mood)
            return ColonyReportValueKind.MeanOrRate;

        if (key.EndsWith(".latest", StringComparison.Ordinal))
            return ColonyReportValueKind.LatestSnapshot;

        if (key.Contains(".peak", StringComparison.Ordinal)
            || key.EndsWith("_peak", StringComparison.Ordinal)
            || key.EndsWith(".max", StringComparison.Ordinal)
            || key.EndsWith("_max", StringComparison.Ordinal)
            || key.EndsWith(".min", StringComparison.Ordinal)
            || key.EndsWith("_min", StringComparison.Ordinal)
            || key.Contains(".max_", StringComparison.Ordinal)
            || key.Contains(".longest_", StringComparison.Ordinal)
            || key.Contains(".first_", StringComparison.Ordinal)
            || key.EndsWith(".oldest", StringComparison.Ordinal)
            || definition.Overall is OverallAggregation.Max or OverallAggregation.Min)
            return ColonyReportValueKind.PeakOrExtreme;

        return definition.Provenance == MetricProvenance.SaveSnapshot
            ? ColonyReportValueKind.LatestSnapshot
            : ColonyReportValueKind.CumulativeTotal;
    }

    private static ColonyReportMetricItem BuildBasicItem(ColonyStats stats, string key, Leaderboard board)
    {
        var def = BasicMetrics.Definition(key);
        var raw = BasicMetrics.Value(stats, key);
        // Endings and save count are known even on ending-only colonies; BasicMetrics.Value requires HasObservedPoint,
        // so preserve BasicMetrics.Value's exact contract so ending-only colonies never show 0 days/raids/wealth (#332).
        NumericValue val;
        if (raw is { } num && double.IsFinite(num))
        {
            val = new NumericValue
            {
                Number = num,
                Coverage = MetricCoverage.SnapshotOnly,
                ThroughTick = stats.HasObservedPoint ? stats.MaxTicks : null,
            };
        }
        else if (!stats.HasObservedPoint && key == BasicMetrics.Endings)
        {
            val = new NumericValue
            {
                Number = stats.EndingCounts.Values.Sum(),
                Coverage = MetricCoverage.SnapshotOnly,
            };
        }
        else if (!stats.HasObservedPoint && key == BasicMetrics.Saves)
        {
            val = new NumericValue
            {
                Number = stats.SaveCount,
                Coverage = MetricCoverage.SnapshotOnly,
            };
        }
        else
        {
            val = new NumericValue { Coverage = MetricCoverage.Unknown };
        }

        var (status, rank) = ResolveRank(stats, val, board);
        return new ColonyReportMetricItem
        {
            MetricKey = key,
            Definition = def,
            IsBasic = true,
            ValueKind = ClassifyValueKind(def, isBasic: true),
            Value = val,
            RankStatus = status,
            Rank = rank,
            MeasuredColonies = board.Measured,
            TargetColonies = board.Targets,
            RankHasPartial = board.HasPartial,
        };
    }

    private static ColonyReportMetricItem BuildNumericItem(ColonyStats stats, MetricDefinition def, NumericValue val, Leaderboard board)
    {
        var (status, rank) = ResolveRank(stats, val, board);
        return new ColonyReportMetricItem
        {
            MetricKey = def.Key,
            Definition = def,
            IsBasic = false,
            ValueKind = ClassifyValueKind(def, isBasic: false),
            Value = val,
            RankStatus = status,
            Rank = rank,
            MeasuredColonies = board.Measured,
            TargetColonies = board.Targets,
            RankHasPartial = board.HasPartial,
        };
    }

    private static (ColonyRankStatus Status, int? Rank) ResolveRank(ColonyStats stats, NumericValue val, Leaderboard board)
    {
        if (val.Coverage == MetricCoverage.NotApplicable)
            return (ColonyRankStatus.NotApplicable, null);
        if (stats.Excluded)
            return (ColonyRankStatus.Excluded, null);
        if (stats.OutOfScope)
            return (ColonyRankStatus.OutOfScope, null);
        if (!val.Known || val.Number is not { } d || !double.IsFinite(d))
            return (ColonyRankStatus.Unmeasured, null);

        var entry = board.Entries.FirstOrDefault(e => e.ColonyKey == stats.Key);
        return entry is not null ? (ColonyRankStatus.Ranked, entry.Rank) : (ColonyRankStatus.Unmeasured, null);
    }

    private static ColonyReportCoverageSummary BuildCoverageSummary(NumericColony? colony)
    {
        if (colony is null)
            return new ColonyReportCoverageSummary(0, 0, 0, 0, 0, 0, 0);

        int total = 0, complete = 0, snapshot = 0, partial = 0, unknown = 0, na = 0, experimental = 0;
        foreach (var row in colony.Rows)
        {
            if (CrossColonyCatalog.IsDerived(row.Definition))
                continue;
            total++;
            switch (row.Value.Coverage)
            {
                case MetricCoverage.Complete: complete++; break;
                case MetricCoverage.SnapshotOnly: snapshot++; break;
                case MetricCoverage.Partial: partial++; break;
                case MetricCoverage.NotApplicable: na++; break;
                default: unknown++; break;
            }
            if (row.Value.Known && row.Value.Evidence == MetricEvidence.Experimental)
                experimental++;
        }
        return new ColonyReportCoverageSummary(total, complete, snapshot, partial, unknown, na, experimental);
    }
}
