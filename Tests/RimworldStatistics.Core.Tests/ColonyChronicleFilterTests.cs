namespace RimworldStatistics.Core.Tests;

public sealed class ColonyChronicleFilterTests
{
    private const string Campaign = "camp-chronicle-filter-1";
    private const string Version = "1.7.4985 rev1269";
    private static readonly string HashA = new('a', 64);
    private static readonly DateTime Epoch = new(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition Deaths = MetricRegistry.Find(HealthDeathFamily.DeathsByRaid)!;
    private static readonly MetricDefinition Raids = MetricRegistry.Find(RaidFamily.ByArrivalMode)!;
    private static readonly MetricDefinition ResearchTicks = MetricRegistry.Find(ResearchFamily.CompletedTicks)!;

    [Fact]
    public void ResolveResearchProjectLabel_HandlesTranslatedAndUnloadedModFallbacksWithoutExposingPackageId()
    {
        var loaded = ColonyChronicleFilter.ResolveResearchProjectLabel(
            "MicroelectronicsBasics",
            tryResolveLoadedProjectLabel: static key => key == "MicroelectronicsBasics" ? "マイクロエレクトロニクス基礎" : null);
        Assert.Equal(("マイクロエレクトロニクス基礎", false), loaded);

        var viaBreakdown = ColonyChronicleFilter.ResolveResearchProjectLabel(
            "author.mod/SolarArray",
            tryResolveLoadedProjectLabel: static _ => null,
            tryResolveBreakdownLabel: static key => key == "author.mod/SolarArray" ? "太陽光アレイ" : key);
        Assert.Equal(("太陽光アレイ", false), viaBreakdown);

        var unloadedQualified = ColonyChronicleFilter.ResolveResearchProjectLabel(
            "author.hypermod/HyperReactor",
            tryResolveLoadedProjectLabel: static _ => null,
            tryResolveBreakdownLabel: static key => key);
        Assert.Equal(("HyperReactor", true), unloadedQualified);

        var unloadedUnqualified = ColonyChronicleFilter.ResolveResearchProjectLabel(
            "AncientShields",
            tryResolveLoadedProjectLabel: static _ => null);
        Assert.Equal(("AncientShields", true), unloadedUnqualified);

        var empty = ColonyChronicleFilter.ResolveResearchProjectLabel("", emptyPlaceholder: "—");
        Assert.Equal(("—", false), empty);
    }

    [Fact]
    public void TextSearch_MatchesTranslatedResearchUnloadedFallbackEventsAndIntervalStatistics()
    {
        var entries = SampleEntries();
        var traces = SampleUndatedTraces();

        // Translated research name (case-insensitive + surrounding whitespace).
        var byTranslatedResearch = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "  マイクロエレクトロニクス  ", "", ""));
        Assert.Equal(["res-micro"], byTranslatedResearch.Rows.Select(e => e.StableKey));

        // Unloaded MOD research matches its displayed fallback short name, never its unknown package ID.
        var byFallbackResearch = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "hyperreactor", "", ""));
        Assert.Equal(["res-unloaded"], byFallbackResearch.Rows.Select(e => e.StableKey));

        var byUnknownPackageId = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "author.hypermod", "", ""));
        Assert.Empty(byUnknownPackageId.Rows);

        // Event text (milestone & ending).
        var byMilestone = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "最初の敵襲", "", ""));
        Assert.Equal(["ms-raid"], byMilestone.Rows.Select(e => e.StableKey));

        // Interval statistic name: matches both chronicle short label ("襲撃") and full statistic label ("敵の襲撃").
        var byShortStat = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "襲撃", "", ""));
        Assert.Equal(["int-1-2", "int-4-6"], byShortStat.Rows.Select(e => e.StableKey));

        var byFullStat = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "敵の襲撃", "", ""));
        Assert.Equal(["int-1-2", "int-4-6"], byFullStat.Rows.Select(e => e.StableKey));

        // Direct MatchesSearch handles null, whitespace, and untrimmed queries without allocating or false-matching.
        Assert.True(ColonyChronicleFilter.MatchesSearch("Raids +2", ["Enemy raids"], null));
        Assert.True(ColonyChronicleFilter.MatchesSearch("Raids +2", ["Enemy raids"], "   "));
        Assert.True(ColonyChronicleFilter.MatchesSearch("Raids +2", ["Enemy raids"], "  ENEMY RAIDS "));
        Assert.False(ColonyChronicleFilter.MatchesSearch("Raids +2", ["Enemy raids"], "Solar"));
        Assert.Equal(entries.Length, ColonyChronicleFilter.Apply(entries, traces, default).Rows.Length);
    }

    [Fact]
    public void DayRange_StartOnlyEndOnlyAndBothEndsInclusive_PreserveOriginalIntervalSpans()
    {
        var entries = SampleEntries();
        var traces = SampleUndatedTraces();

        // Start day only (>= 4.0): keeps single-day entries at day >= 4.0 and intervals overlapping [4.0, +inf).
        var fromDay4 = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "4", ""));
        Assert.Equal(["int-2-4", "res-unloaded", "int-4-6", "end-ship"], fromDay4.Rows.Select(e => e.StableKey));

        // Verify interval [2.0, 4.0] touched day 4.0 inclusively and kept its original StartTick/EndTick untouched.
        var touchingStart = Assert.Single(fromDay4.Rows, e => e.StableKey == "int-2-4");
        Assert.Equal((120_000, 240_000), (touchingStart.StartTick, touchingStart.EndTick));

        // End day only (<= 2.0): keeps single-day entries at day <= 2.0 and intervals overlapping [0, 2.0].
        var upToDay2 = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "", "2"));
        Assert.Equal(["ms-raid", "int-1-2", "res-micro", "int-2-4"], upToDay2.Rows.Select(e => e.StableKey));

        var touchingEnd = Assert.Single(upToDay2.Rows, e => e.StableKey == "int-2-4");
        Assert.Equal((120_000, 240_000), (touchingEnd.StartTick, touchingEnd.EndTick));

        // Both ends inclusive [2.5, 3.5]: excludes single-day 2.0 and 4.0, excludes [1, 2] and [4, 6],
        // but includes enclosing interval [2.0, 4.0] and single-day 3.0 with original ticks preserved.
        var middleWindow = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "2.5", "3.5"));
        Assert.Equal(["int-2-4", "gap-3"], middleWindow.Rows.Select(e => e.StableKey));
        var enclosing = Assert.Single(middleWindow.Rows, e => e.StableKey == "int-2-4");
        Assert.Equal((120_000, 240_000), (enclosing.StartTick, enclosing.EndTick));
    }

    [Fact]
    public void DayRange_DecimalBoundaryHasNoFloatingPointDrift()
    {
        // 1.14 days == 68,400 ticks exactly; in IEEE-754 double, 68400 / 60000.0 > 1.14 slightly.
        var exactEntry = MakeSingleDay("exact-1.14", ChronicleEntryKind.ResearchCompleted, ChronicleTimingKind.ExactTick, 68_400);
        var entries = new[]
        {
            new ChronicleFilterableEntry<ChronicleEntry>(exactEntry, "Research completed: Battery", ["Battery"], exactEntry),
        };
        var noTraces = Array.Empty<ChronicleFilterableUndatedTrace<string>>();

        var atPoint = ColonyChronicleFilter.Apply(
            entries, noTraces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "1.14", "1.14"));
        Assert.Single(atPoint.Rows);

        var oneTickLater = MakeSingleDay("tick-after", ChronicleEntryKind.ResearchCompleted, ChronicleTimingKind.ExactTick, 68_401);
        var afterResult = ColonyChronicleFilter.Apply(
            [new ChronicleFilterableEntry<ChronicleEntry>(oneTickLater, "Battery", [], oneTickLater)],
            noTraces,
            ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "1.14", "1.14"));
        Assert.Empty(afterResult.Rows);
    }

    [Fact]
    public void UndatedTraces_SearchedByNameWhenUnboundedAndExcludedWheneverDayFilterIsSet()
    {
        var entries = SampleEntries();
        var traces = SampleUndatedTraces();

        // No filters: both undated traces remain visible.
        var unfiltered = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "", ""));
        Assert.Equal(["trace-ship", "trace-royal"], unfiltered.UndatedTraces);
        Assert.Equal(0, unfiltered.HiddenUndatedTraceCountByDayRange);

        // Text-only search: matches undated trace by its ending name, not by evidence boilerplate.
        var shipSearch = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "宇宙船", "", ""));
        Assert.Equal(["trace-ship"], shipSearch.UndatedTraces);
        Assert.Equal(0, shipSearch.HiddenUndatedTraceCountByDayRange);

        // Start day only, end day only, or both days: undated traces are excluded and counted as hidden.
        foreach (var (start, end) in new[] { ("0", ""), ("", "100"), ("1", "10") })
        {
            var bounded = ColonyChronicleFilter.Apply(
                entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", start, end));
            Assert.Empty(bounded.UndatedTraces);
            Assert.Equal(2, bounded.HiddenUndatedTraceCountByDayRange);
        }
    }

    [Theory]
    [InlineData("abc", "", ChronicleDayRangeStatus.InvalidStartDay, true, false)]
    [InlineData("", "-1", ChronicleDayRangeStatus.InvalidEndDay, false, true)]
    [InlineData("-0", "", ChronicleDayRangeStatus.InvalidStartDay, true, false)]
    [InlineData("+5", "", ChronicleDayRangeStatus.InvalidStartDay, true, false)]
    [InlineData("1,2,3", "2.0", ChronicleDayRangeStatus.InvalidStartDay, true, false)]
    [InlineData("1.2,3", "2.0", ChronicleDayRangeStatus.InvalidStartDay, true, false)]
    [InlineData(".", "bad", ChronicleDayRangeStatus.InvalidBothDays, true, true)]
    public void InvalidDayInputs_AreFlaggedAndNeverLeakEntriesOrUndatedTraces(
        string start,
        string end,
        ChronicleDayRangeStatus expectedStatus,
        bool highlightStart,
        bool highlightEnd)
    {
        var entries = SampleEntries();
        var traces = SampleUndatedTraces();

        var wideInterval = new ChronicleEntry
        {
            StableKey = "int-wide",
            Kind = ChronicleEntryKind.VerifiedInterval,
            TimingKind = ChronicleTimingKind.ProvenInterval,
            StartTick = 30_000,
            EndTick = 3_000_000,
            EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
        };
        var withWide = entries.Append(new ChronicleFilterableEntry<ChronicleEntry>(wideInterval, "Wide", [], wideInterval)).ToArray();

        var query = ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", start, end);
        Assert.Equal(expectedStatus, query.DayRange.Status);
        Assert.False(query.DayRange.IsValid);
        Assert.Equal(highlightStart, query.DayRange.HighlightStartField);
        Assert.Equal(highlightEnd, query.DayRange.HighlightEndField);

        var result = ColonyChronicleFilter.Apply(withWide, traces, query);
        Assert.Empty(result.Rows);
        Assert.Empty(result.UndatedTraces);
        Assert.Equal(0, result.FilterCount(ChronicleKindFilter.All));
    }

    [Fact]
    public void DecimalCommaReversedBoundsAndDisplayedDayRounding_MatchHistoryPlotAndShownLabels()
    {
        var entries = SampleEntries();
        var traces = SampleUndatedTraces();

        // Decimal comma ("1,5" .. "3,5") matches HistoryPlotRange.TryParseDay behavior.
        var commaResult = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "1,5", "3,5"));
        Assert.Equal(["int-1-2", "res-micro", "int-2-4", "gap-3"], commaResult.Rows.Select(e => e.StableKey));

        // Reversed range ("3.5" .. "2.5") normalizes to [2.5, 3.5] with WasReversed = true and no error highlights.
        var reversedQuery = ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "3.5", "2.5");
        Assert.Equal(ChronicleDayRangeStatus.Valid, reversedQuery.DayRange.Status);
        Assert.True(reversedQuery.DayRange.IsValid);
        Assert.True(reversedQuery.DayRange.WasReversed);
        Assert.False(reversedQuery.DayRange.HighlightStartField);
        Assert.False(reversedQuery.DayRange.HighlightEndField);
        Assert.Equal((2.5m, 3.5m), (reversedQuery.DayRange.StartDay, reversedQuery.DayRange.EndDay));
        var reversedResult = ColonyChronicleFilter.Apply(entries, traces, reversedQuery);
        Assert.Equal(["int-2-4", "gap-3"], reversedResult.Rows.Select(e => e.StableKey));
        Assert.Empty(reversedResult.UndatedTraces);

        // 70,000 ticks (1.1666... days) is displayed as "1.2" in the UI; entering "1.2" matches start-only, end-only, and [1.2, 1.2].
        var at70k = MakeSingleDay("res-70k", ChronicleEntryKind.ResearchCompleted, ChronicleTimingKind.ExactTick, 70_000);
        var wrapped70k = new[] { new ChronicleFilterableEntry<ChronicleEntry>(at70k, "Solar", ["Solar"], at70k) };
        var noTraces = Array.Empty<ChronicleFilterableUndatedTrace<string>>();

        Assert.Single(ColonyChronicleFilter.Apply(wrapped70k, noTraces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "1.2", "")).Rows);
        Assert.Single(ColonyChronicleFilter.Apply(wrapped70k, noTraces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "", "1.2")).Rows);
        Assert.Single(ColonyChronicleFilter.Apply(wrapped70k, noTraces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "1.2", "1.2")).Rows);
        Assert.Single(ColonyChronicleFilter.Apply(wrapped70k, noTraces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "1,2", "1,2")).Rows);
        Assert.Empty(ColonyChronicleFilter.Apply(wrapped70k, noTraces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "1.3", "")).Rows);
        Assert.Empty(ColonyChronicleFilter.Apply(wrapped70k, noTraces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "", "1.1")).Rows);
    }

    [Fact]
    public void EmptyWhitespaceNoMatchCompositionSortOrderAndClearFilters_BehaveDeterministically()
    {
        var entries = SampleEntries();
        var traces = SampleUndatedTraces();

        // Whitespace-only inputs behave identically to empty inputs.
        var whitespaceQuery = ChronicleFilterQuery.Create(ChronicleKindFilter.All, "   ", "  ", "\t");
        Assert.False(whitespaceQuery.HasAnyFilter);
        Assert.Equal(ChronicleDayRangeStatus.None, whitespaceQuery.DayRange.Status);
        var whitespaceResult = ColonyChronicleFilter.Apply(entries, traces, whitespaceQuery);
        Assert.Equal(entries.Length, whitespaceResult.Rows.Length);
        Assert.Equal(traces.Length, whitespaceResult.UndatedTraces.Length);

        // Composing category filter + day range [1.5, 4.5] + newestFirst updates per-category counts and reverses row order:
        // overlapping intervals/gaps = int-1-2, int-2-4, gap-3, int-4-6 (4); research = res-micro, res-unloaded (2); total = 6.
        var composed = ColonyChronicleFilter.Apply(
            entries,
            traces,
            ChronicleFilterQuery.Create(ChronicleKindFilter.Research, "", "1.5", "4.5", newestFirst: true));
        Assert.Equal(["res-unloaded", "res-micro"], composed.Rows.Select(e => e.StableKey));
        Assert.Equal(6, composed.FilterCount(ChronicleKindFilter.All));
        Assert.Equal(0, composed.FilterCount(ChronicleKindFilter.Highlights));
        Assert.Equal(4, composed.FilterCount(ChronicleKindFilter.IntervalsAndGaps));
        Assert.Equal(2, composed.FilterCount(ChronicleKindFilter.Research));

        // Valid query with zero matches.
        var noMatch = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "存在しない研究名", "1", "10"));
        Assert.Empty(noMatch.Rows);
        Assert.Empty(noMatch.UndatedTraces);
        Assert.Equal(0, noMatch.FilterCount(ChronicleKindFilter.All));

        // Clearing filters restores all entries and undated traces from the same cached branch arrays.
        var cleared = ColonyChronicleFilter.Apply(
            entries, traces, ChronicleFilterQuery.Create(ChronicleKindFilter.All, "", "", "", newestFirst: false));
        Assert.Equal(entries.Select(e => e.Payload.StableKey), cleared.Rows.Select(e => e.StableKey));
        Assert.Equal(["trace-ship", "trace-royal"], cleared.UndatedTraces);
    }

    [Fact]
    public void MultiBranchChronicle_PreservesFilterAcrossBranchSwitchWithoutRebuildingChronicleOnEdits()
    {
        var f = new BranchFixture();
        var rootSave = f.Save("root.rws", 60_000);
        rootSave.EndingTraces.Add(EndingType.Archonexus);
        f.Save("branchA.rws", 180_000, new LineageEntry(EndingType.Ship, 0, 170_000));
        f.Save("branchB.rws", 240_000);

        var cpRoot = f.Checkpoint("cp-root", null, 60_000, ["root.rws"], (Raids, Counter(1)));
        cpRoot.Metrics[ResearchTicks.StoredKey] = ResearchBreakdown(("MicroelectronicsBasics", 0.8));

        var cpA = f.Checkpoint("cp-A", "cp-root", 180_000, ["branchA.rws"], (Raids, Counter(3)));
        cpA.Metrics[ResearchTicks.StoredKey] = ResearchBreakdown(("MicroelectronicsBasics", 0.8), ("author.modA/StarDrive", 2.5));

        var cpB = f.Checkpoint("cp-B", "cp-root", 240_000, ["branchB.rws"], (Raids, Counter(2)));
        cpB.Metrics[ResearchTicks.StoredKey] = ResearchBreakdown(("MicroelectronicsBasics", 0.8), ("author.modB/DeepDrillPlus", 3.5));

        f.Ending(EndingType.Ship, 0, 170_000, EndingSource.Live);

        var (_, lineage) = f.Build();
        var (chronicleA, _) = f.Build(KeyOf(lineage, "cp-A"));
        var (chronicleB, _) = f.Build(KeyOf(lineage, "cp-B"));
        f.ResetBuildCount();

        var preparedA = PrepareBranch(chronicleA);
        var preparedB = PrepareBranch(chronicleB);

        // Multiple search, day-range, sort, and clear operations run purely in memory with zero ColonyChronicle.Build calls.
        var activeQuery = ChronicleFilterQuery.Create(ChronicleKindFilter.Research, "Drive", "2", "4", newestFirst: true);
        var onA = ColonyChronicleFilter.Apply(preparedA.Entries, preparedA.Traces, activeQuery);
        Assert.Single(onA.Rows);
        Assert.Equal("author.modA/StarDrive", onA.Rows[0].ResearchDefKey);
        Assert.Empty(onA.UndatedTraces);
        Assert.Equal(1, onA.HiddenUndatedTraceCountByDayRange);

        // Switching to Branch B with the same active query finds nothing for "Drive", then searching "Drill" finds Branch B's research.
        var sameQueryOnB = ColonyChronicleFilter.Apply(preparedB.Entries, preparedB.Traces, activeQuery);
        Assert.Empty(sameQueryOnB.Rows);

        var drillOnB = ColonyChronicleFilter.Apply(
            preparedB.Entries,
            preparedB.Traces,
            ChronicleFilterQuery.Create(ChronicleKindFilter.Research, "DeepDrillPlus", "2", "4"));
        Assert.Single(drillOnB.Rows);
        Assert.Equal("author.modB/DeepDrillPlus", drillOnB.Rows[0].ResearchDefKey);

        // Clearing day bounds on Branch B makes the undated Archonexus trace searchable by name without rebuilding.
        var traceSearchOnB = ColonyChronicleFilter.Apply(
            preparedB.Entries,
            preparedB.Traces,
            ChronicleFilterQuery.Create(ChronicleKindFilter.All, "Archonexus", "", ""));
        Assert.Empty(traceSearchOnB.Rows);
        Assert.Single(traceSearchOnB.UndatedTraces);

        Assert.Equal(0, f.BuildCount);
    }

    private static ChronicleFilterableEntry<ChronicleEntry>[] SampleEntries()
    {
        // Day 1.0 (60,000): sampled milestone
        var raidMilestone = MakeSingleDay("ms-raid", ChronicleEntryKind.Milestone, ChronicleTimingKind.HistorySampleDay, 60_000);
        // Day 1.0 - 2.0 (60,000 - 120,000): verified interval with raids
        var interval12 = new ChronicleEntry
        {
            StableKey = "int-1-2",
            Kind = ChronicleEntryKind.VerifiedInterval,
            TimingKind = ChronicleTimingKind.ProvenInterval,
            StartTick = 60_000,
            EndTick = 120_000,
            EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
        };
        // Day 2.0 (120,000): translated research
        var microResearch = new ChronicleEntry
        {
            StableKey = "res-micro",
            Kind = ChronicleEntryKind.ResearchCompleted,
            TimingKind = ChronicleTimingKind.ExactTick,
            StartTick = 120_000,
            EndTick = 120_000,
            EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
            ResearchDefKey = "MicroelectronicsBasics",
        };
        // Day 2.0 - 4.0 (120,000 - 240,000): verified interval with meals cooked
        var interval24 = new ChronicleEntry
        {
            StableKey = "int-2-4",
            Kind = ChronicleEntryKind.VerifiedInterval,
            TimingKind = ChronicleTimingKind.ProvenInterval,
            StartTick = 120_000,
            EndTick = 240_000,
            EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
        };
        // Day 3.0 (180,000): recording gap
        var gap3 = MakeSingleDay("gap-3", ChronicleEntryKind.RecordingGap, ChronicleTimingKind.ExactTick, 180_000);
        // Day 4.0 (240,000): unloaded MOD research
        var unloadedResearch = new ChronicleEntry
        {
            StableKey = "res-unloaded",
            Kind = ChronicleEntryKind.ResearchCompleted,
            TimingKind = ChronicleTimingKind.ExactTick,
            StartTick = 240_000,
            EndTick = 240_000,
            EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
            ResearchDefKey = "author.hypermod/HyperReactor",
        };
        var (unloadedLabel, _) = ColonyChronicleFilter.ResolveResearchProjectLabel(unloadedResearch.ResearchDefKey);
        // Day 4.0 - 6.0 (240,000 - 360,000): verified interval with raids
        var interval46 = new ChronicleEntry
        {
            StableKey = "int-4-6",
            Kind = ChronicleEntryKind.VerifiedInterval,
            TimingKind = ChronicleTimingKind.ProvenInterval,
            StartTick = 240_000,
            EndTick = 360_000,
            EvidenceSource = ChronicleEvidenceSource.CheckpointAggregate,
        };
        // Day 5.0 (300,000): ending
        var shipEnding = MakeSingleDay("end-ship", ChronicleEntryKind.Ending, ChronicleTimingKind.ExactTick, 300_000);

        return
        [
            new(raidMilestone, "最初の敵襲", [], raidMilestone),
            new(interval12, "襲撃 +2", ["襲撃", "敵の襲撃"], interval12),
            new(microResearch, "研究完了: マイクロエレクトロニクス基礎", ["マイクロエレクトロニクス基礎"], microResearch),
            new(interval24, "調理した食事 +45", ["調理した食事"], interval24),
            new(gap3, "3.0 日目からプレイ中の記録を開始", [], gap3),
            new(unloadedResearch, $"研究完了: {unloadedLabel}", [unloadedLabel], unloadedResearch),
            new(interval46, "襲撃 +1", ["襲撃", "敵の襲撃"], interval46),
            new(shipEnding, "宇宙船発射 · 離脱 4 人", [], shipEnding),
        ];
    }

    private static ChronicleFilterableUndatedTrace<string>[] SampleUndatedTraces() =>
    [
        new(new ChronicleUndatedTrace("u1", EndingType.Ship, ChronicleEvidenceSource.SeededTrace), "宇宙船発射", "trace-ship"),
        new(new ChronicleUndatedTrace("u2", EndingType.RoyalAscent, ChronicleEvidenceSource.SaveTrace), "王室昇叙", "trace-royal"),
    ];

    private static ChronicleEntry MakeSingleDay(
        string key,
        ChronicleEntryKind kind,
        ChronicleTimingKind timing,
        int tick)
        => new()
        {
            StableKey = key,
            Kind = kind,
            TimingKind = timing,
            StartTick = tick,
            EndTick = tick,
            EvidenceSource = ChronicleEvidenceSource.LiveEvent,
        };

    private static (ChronicleFilterableEntry<ChronicleEntry>[] Entries, ChronicleFilterableUndatedTrace<ChronicleUndatedTrace>[] Traces) PrepareBranch(
        ColonyChronicle chronicle)
    {
        var entries = chronicle.Entries.Select(e =>
        {
            if (e.Kind == ChronicleEntryKind.ResearchCompleted)
            {
                var (label, _) = ColonyChronicleFilter.ResolveResearchProjectLabel(e.ResearchDefKey);
                return new ChronicleFilterableEntry<ChronicleEntry>(e, $"Research completed: {label}", [label], e);
            }
            if (e.Kind == ChronicleEntryKind.VerifiedInterval)
            {
                var terms = e.IntervalFacts.Select(f => f.MetricKey).ToArray();
                return new ChronicleFilterableEntry<ChronicleEntry>(e, string.Join(" · ", terms), terms, e);
            }
            return new ChronicleFilterableEntry<ChronicleEntry>(e, e.EndingType?.ToString() ?? e.Kind.ToString(), [], e);
        }).ToArray();

        var traces = chronicle.UndatedTraces
            .Select(t => new ChronicleFilterableUndatedTrace<ChronicleUndatedTrace>(t, t.EndingType.ToString(), t))
            .ToArray();

        return (entries, traces);
    }

    private static ChronicleBranchKey KeyOf(ColonySaveLineage lineage, string checkpointId)
        => ChronicleBranchKey.Of(lineage.Nodes.Single(n => n.Kind == SaveLineageNodeKind.Checkpoint && n.CheckpointId == checkpointId));

    private static MetricValue Counter(long value, int since = 0)
        => new() { Coverage = MetricCoverage.Complete, IntegerValue = value, SinceTick = since };

    private static MetricEntry ResearchBreakdown(params (string Key, double Day)[] items)
    {
        var entry = new MetricEntry { Revision = MetricKeys.Revision(ResearchFamily.CompletedTicks) };
        entry.Values[MetricEntry.Scalar] = new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = items.Length };
        foreach (var (k, day) in items)
            entry.Values[k] = new MetricValue { Coverage = MetricCoverage.Complete, RealValue = day };
        return entry;
    }

    private static DateTime Utc(int tick) => Epoch.AddMinutes(tick / 6000.0);

    private sealed class BranchFixture
    {
        public Profile Profile { get; } = new();
        public int BuildCount { get; private set; }

        public void ResetBuildCount() => BuildCount = 0;

        public SaveObservation Save(string file, int tick, params LineageEntry[] lineage)
        {
            var save = new SaveObservation
            {
                FileName = file,
                FileSize = 4096 + file.Length,
                LastWriteUtc = Utc(tick),
                Status = ObservationStatus.Ok,
                ContentHash = "content-" + file,
                GameVersion = Version,
                Identity = new ColonyIdentity { ModCampaignId = Campaign },
                Display = new DisplayInfo { FactionName = "Chronicle Filter Test", WorldName = "Planet" },
                Values = new ObservedValues { TicksGame = tick },
                Lineage = [.. lineage],
            };
            Profile.SaveObservations.Add(save);
            return save;
        }

        public MetricObservation Checkpoint(
            string key,
            string? parent,
            int tick,
            string[] files,
            params (MetricDefinition Definition, MetricValue Value)[] values)
        {
            var obs = new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint,
                Key = key,
                ParentKey = parent,
                PayloadHash = "hash-" + key,
                SaveTick = tick,
                ObservedUtc = Utc(tick),
                GameVersion = Version,
                ModSetHash = HashA,
                Identity = new ColonyIdentity { ModCampaignId = Campaign },
            };
            foreach (var file in files)
            {
                var save = Profile.SaveObservations.First(s => s.FileName == file);
                obs.FileAliases.Add(new MetricFileAlias { FileName = file, FileSize = save.FileSize, LastWriteUtc = save.LastWriteUtc });
            }
            foreach (var (definition, value) in values)
                obs.Metrics[definition.StoredKey] = MetricEntry.Of(definition.Revision, value);
            Profile.MetricObservations.Add(obs);
            return obs;
        }

        public void Ending(EndingType type, int ordinal, int tick, EndingSource source)
        {
            Profile.EndingEvents.Add(new EndingEvent
            {
                CampaignId = Campaign,
                Type = type,
                Ordinal = ordinal,
                Source = source,
                TicksGame = tick,
                RealTimeUtc = Utc(tick),
                Departed = 3,
                DevMode = false,
                GameVersion = Version,
            });
        }

        public (ColonyChronicle Chronicle, ColonySaveLineage Lineage) Build(ChronicleBranchKey? branch = null)
        {
            BuildCount++;
            var colonies = CampaignMatcher.BuildColonies(Profile);
            var colony = Assert.Single(colonies);
            var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
            var view = new StatsView
            {
                Colonies = colonies,
                Stats = stats,
                Numeric = MetricCalculator.BuildView(
                    Profile,
                    colonies,
                    stats,
                    MetricRegistry.Compose(Profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits)),
            };
            var lineage = ColonySaveLineage.Build(Profile, colonies, colony.Key);
            var chronicle = ColonyChronicle.Build(view, colony.Key, lineage, branch);
            Assert.NotNull(chronicle);
            return (chronicle!, lineage);
        }
    }
}
