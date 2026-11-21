namespace RimworldStatistics.Core.Tests;

public sealed class HistoryPlotRangeTests
{
    private const int Day = StatFormat.TicksPerDay;
    private static readonly MetricDefinition SaveMetric = MetricRegistry.Find(MetricKeys.WealthItemsPeak)!;
    private static readonly MetricDefinition RunningTotal = MetricRegistry.Find(ResearchFamily.Completed)!;

    private static NumericValue Val(double n, MetricCoverage coverage = MetricCoverage.Complete)
        => new() { Number = n, Coverage = coverage };

    [Fact]
    public void Resolve_NormalizesReversedSameDayOneSidedBlankAndRejectsInvalidInputs()
    {
        // Both blank -> full range
        var full = HistoryPlotRange.Resolve("", "   ", 10 * Day, 50 * Day);
        Assert.Equal(HistoryRangeStatus.FullRange, full.Status);
        Assert.Null(full.Range);
        Assert.False(full.WasReversed);

        // Normal ascending decimal and comma-decimal inputs
        var normal = HistoryPlotRange.Resolve("12.5", "30", 10 * Day, 50 * Day);
        Assert.Equal(HistoryRangeStatus.Zoomed, normal.Status);
        Assert.Equal(new HistoryTickRange(750_000, 1_800_000), normal.Range);
        Assert.False(normal.WasReversed);

        var comma = HistoryPlotRange.Resolve("12,5", "15,25", 0, 50 * Day);
        Assert.Equal(HistoryRangeStatus.Zoomed, comma.Status);
        Assert.Equal(new HistoryTickRange(750_000, 915_000), comma.Range);

        // Reversed -> normalized to ascending with WasReversed = true
        var reversed = HistoryPlotRange.Resolve("40", "15", 10 * Day, 50 * Day);
        Assert.Equal(HistoryRangeStatus.Zoomed, reversed.Status);
        Assert.True(reversed.WasReversed);
        Assert.Equal(new HistoryTickRange(15 * Day, 40 * Day), reversed.Range);
        Assert.False(reversed.Range!.Value.IsSingleTick);

        // Same day -> zero-span single tick, finite and non-negative
        var sameDay = HistoryPlotRange.Resolve("20", "20", 10 * Day, 50 * Day);
        Assert.Equal(HistoryRangeStatus.Zoomed, sameDay.Status);
        Assert.False(sameDay.WasReversed);
        Assert.Equal(new HistoryTickRange(20 * Day, 20 * Day), sameDay.Range);
        Assert.True(sameDay.Range!.Value.IsSingleTick);

        // One-sided inputs against full bounds
        var startOnly = HistoryPlotRange.Resolve("25", "", 10 * Day, 50 * Day);
        Assert.Equal(new HistoryTickRange(25 * Day, 50 * Day), startOnly.Range);

        var startPastEnd = HistoryPlotRange.Resolve("80", "", 10 * Day, 50 * Day);
        Assert.Equal(new HistoryTickRange(80 * Day, 80 * Day), startPastEnd.Range);

        var endOnly = HistoryPlotRange.Resolve("", "35", 10 * Day, 50 * Day);
        Assert.Equal(new HistoryTickRange(10 * Day, 35 * Day), endOnly.Range);

        var endBeforeFirst = HistoryPlotRange.Resolve("", "5", 10 * Day, 50 * Day);
        Assert.Equal(new HistoryTickRange(0, 5 * Day), endBeforeFirst.Range);

        // Huge day clamps safely to int.MaxValue without overflow
        var huge = HistoryPlotRange.Resolve("0", "999999", 0, 50 * Day);
        Assert.Equal(HistoryRangeStatus.Zoomed, huge.Status);
        Assert.Equal(new HistoryTickRange(0, int.MaxValue), huge.Range);

        // Invalid inputs
        foreach (var (s, e) in new[]
        {
            ("-1", "10"),
            ("10", "-0.5"),
            ("abc", "10"),
            ("10", "NaN"),
            ("Infinity", "10"),
            ("1,2,3", "10"),
        })
        {
            var invalid = HistoryPlotRange.Resolve(s, e, 0, 50 * Day);
            Assert.Equal(HistoryRangeStatus.InvalidInput, invalid.Status);
            Assert.Null(invalid.Range);
        }
    }

    [Fact]
    public void Slice_PreservesOriginalHistoryGapsPartialCoverageUnlinkedFlagsAndInterleavedBranches()
    {
        // a(10d) ─┬─ b2(20d, Partial) ── gap(25d) ── c2(35d, continues b2 across gap & sibling)
        //         └─ b1(30d) ─────────────────────── c1(40d, continues b1)
        // plus an unlinked save u(45d)
        var original = new MetricHistoryPoint[]
        {
            new(10 * Day, Val(100)),
            new(20 * Day, Val(120, MetricCoverage.Partial), ConnectToPrevious: true),
            new(25 * Day, null), // gap (no value, never 0)
            new(30 * Day, Val(300)) { ContinuesFrom = 0, SkipsOnlyOtherBranches = false },
            new(35 * Day, Val(135)) { ContinuesFrom = 1, SkipsOnlyOtherBranches = false },
            new(40 * Day, Val(340)) { ContinuesFrom = 3, SkipsOnlyOtherBranches = true },
            new(45 * Day, Val(500)) { Unlinked = true },
        };

        // Zoom to [20d, 45d]: root a(10d) is outside the range
        var sliced = MetricHistoryPlot.Slice(original, new HistoryTickRange(20 * Day, 45 * Day));

        // Original points remain untouched
        Assert.Equal(7, original.Length);
        Assert.True(original[1].ConnectToPrevious);
        Assert.Equal(0, original[3].ContinuesFrom);

        // Kept indices map: 1->0 (20d), 2->1 (25d gap), 3->2 (30d), 4->3 (35d), 5->4 (40d), 6->5 (45d)
        Assert.Equal([20 * Day, 25 * Day, 30 * Day, 35 * Day, 40 * Day, 45 * Day], sliced.Select(p => p.Tick));
        Assert.Null(sliced[1].Value); // gap stays null, never 0
        Assert.Equal(MetricCoverage.Partial, sliced[0].Value!.Coverage);

        // b2(new 0) and b1(new 2) had root a(old 0) outside range -> not connected to anything in slice,
        // and critically NOT turned into Unlinked hollow points!
        Assert.Equal(-1, sliced[0].Predecessor(0));
        Assert.False(sliced[0].ConnectToPrevious);
        Assert.False(sliced[0].Unlinked);

        Assert.Equal(-1, sliced[2].Predecessor(2));
        Assert.False(sliced[2].Unlinked);

        // c2(new 3) still continues b2(new 0), and c1(new 4) still continues b1(new 2) with SkipsOnlyOtherBranches preserved
        Assert.Equal(0, sliced[3].Predecessor(3));
        Assert.False(sliced[3].SkipsOnlyOtherBranches);
        Assert.Equal(2, sliced[4].Predecessor(4));
        Assert.True(sliced[4].SkipsOnlyOtherBranches);

        // Unlinked point u(new 5) stays Unlinked and unconnected
        Assert.True(sliced[5].Unlinked);
        Assert.Equal(-1, sliced[5].Predecessor(5));

        // Full-range slice returns the exact same list instance without copying
        var fullSlice = MetricHistoryPlot.Slice(original, new HistoryTickRange(0, 60 * Day));
        Assert.Same(original, fullSlice);
    }

    [Fact]
    public void SliceThenThin_RetainsFullDetailInZoomedWindowAndHandlesEmptyRange()
    {
        // 1,000 half-day recorder points (0.0d .. 499.5d)
        var dense = Enumerable.Range(0, 1000)
            .Select(i => new MetricHistoryPoint(
                i * HistoryMetricReader.TicksPerPoint,
                i == 210 ? null : Val(1000 + i, MetricCoverage.SnapshotOnly),
                ConnectToPrevious: i > 0 && i != 210 && i != 211))
            .ToArray();

        // Full range at 100px width is thinned
        var fullThinned = MetricHistoryPlot.Thin(dense, 100);
        Assert.True(fullThinned.Count < dense.Length);

        // Zooming into days 100..110 (21 points, including the gap at index 210 = day 105.0) keeps every single point unthinned
        var zoomRange = new HistoryTickRange(100 * Day, 110 * Day);
        var zoomed = MetricHistoryPlot.Thin(MetricHistoryPlot.Slice(dense, zoomRange), 100);
        Assert.Equal(21, zoomed.Count);
        var gapIndex = zoomed.ToList().FindIndex(p => p.Value is null);
        Assert.Equal(10, gapIndex); // day 105.0
        Assert.Equal(-1, zoomed[gapIndex + 1].Predecessor(gapIndex + 1)); // gap is never bridged

        // Empty range between two saves produces zero points, never interpolated values
        var sparse = new MetricHistoryPoint[]
        {
            new(10 * Day, Val(50)),
            new(40 * Day, Val(90), ConnectToPrevious: true),
        };
        var emptySlice = MetricHistoryPlot.Slice(sparse, new HistoryTickRange(20 * Day, 30 * Day));
        Assert.Empty(emptySlice);
    }

    [Fact]
    public void OverlayTracker_SharesZoomXAxisAcrossDisjointObservationDaysAndRestoresFullRange()
    {
        // Primary observed on days 10, 20, 30; Overlay A on days 25, 45, 60; Overlay B on days 2, 8
        var primary = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new MetricHistoryPoint(10 * Day, Val(100, MetricCoverage.SnapshotOnly)),
            new MetricHistoryPoint(20 * Day, null), // gap in primary
            new MetricHistoryPoint(30 * Day, Val(300, MetricCoverage.Partial)),
        ]);
        var primaryMeasured = primary.Points.Where(p => p.Value is not null).ToArray();

        var overlayA = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new MetricHistoryPoint(25 * Day, Val(250, MetricCoverage.SnapshotOnly)),
            new MetricHistoryPoint(45 * Day, Val(900, MetricCoverage.SnapshotOnly), ConnectToPrevious: true),
            new MetricHistoryPoint(60 * Day, Val(1200, MetricCoverage.SnapshotOnly)) { Unlinked = true },
        ]);
        var overlayB = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new MetricHistoryPoint(2 * Day, Val(10, MetricCoverage.SnapshotOnly)),
            new MetricHistoryPoint(8 * Day, Val(40, MetricCoverage.SnapshotOnly), ConnectToPrevious: true),
        ]);

        var tracker = new HistoryOverlayTracker("primary", maxOverlays: 2);
        Assert.True(tracker.TryAdd("a", "Colony A", out var entryA));
        Assert.True(tracker.TryAdd("b", "Colony B", out var entryB));
        Assert.True(tracker.TryComplete(entryA!.RequestId, overlayA, primary.Kind));
        Assert.True(tracker.TryComplete(entryB!.RequestId, overlayB, primary.Kind));

        var fullBounds = tracker.ComputeBounds(primary, primaryMeasured);
        Assert.Equal((2 * Day, 60 * Day), (fullBounds.FirstTick, fullBounds.LastTick));
        Assert.Equal((10d, 1200d), (fullBounds.MinValue, fullBounds.MaxValue));
        Assert.True(fullBounds.HasPartial);
        Assert.True(fullBounds.HasUnlinked);

        // 1. Zoom to [15d, 50d]: Primary has 30d (300, Partial) + 20d gap; Overlay A has 25d (250) & 45d (900); Overlay B has none
        var zoom1 = new HistoryTickRange(15 * Day, 50 * Day);
        var bounds1 = tracker.ComputeBounds(primary, primaryMeasured, zoom1);
        Assert.Equal((15 * Day, 50 * Day), (bounds1.FirstTick, bounds1.LastTick));
        Assert.Equal((250d, 900d), (bounds1.MinValue, bounds1.MaxValue));
        Assert.True(bounds1.HasPartial);
        Assert.False(bounds1.HasUnlinked); // 60d unlinked point is outside zoom1
        Assert.Equal(3, tracker.CountVisibleMeasured(primaryMeasured, zoom1));
        Assert.Equal([25 * Day, 45 * Day], entryA.GetOrThinPoints(200, zoom1).Select(p => p.Tick));
        Assert.Equal([25 * Day, 45 * Day], entryA.GetVisibleMeasured(zoom1).Select(p => p.Tick));
        Assert.Empty(entryB.GetOrThinPoints(200, zoom1));
        Assert.Empty(entryB.GetVisibleMeasured(zoom1));

        // Original point identity is preserved in GetVisibleMeasured for lineage lookup
        Assert.Same(overlayA.Points[0], entryA.GetVisibleMeasured(zoom1)[0]);

        // 2. Zoom to [40d, 55d]: Primary has 0 points, Overlay A has 45d (900) -> still valid shared X axis and finite Y
        var overlayOnlyZoom = new HistoryTickRange(40 * Day, 55 * Day);
        var boundsOverlayOnly = tracker.ComputeBounds(primary, primaryMeasured, overlayOnlyZoom);
        Assert.Equal((40 * Day, 55 * Day), (boundsOverlayOnly.FirstTick, boundsOverlayOnly.LastTick));
        Assert.Equal((900d, 900d), (boundsOverlayOnly.MinValue, boundsOverlayOnly.MaxValue));
        Assert.Equal(1, tracker.CountVisibleMeasured(primaryMeasured, overlayOnlyZoom));
        Assert.Empty(HistoryPlotRange.FilterMeasured(primaryMeasured, overlayOnlyZoom));

        // 3. Zoom to [32d, 38d]: Neither primary nor overlays have points -> empty range keeps finite fallback Y bounds
        var emptyZoom = new HistoryTickRange(32 * Day, 38 * Day);
        var boundsEmpty = tracker.ComputeBounds(primary, primaryMeasured, emptyZoom);
        Assert.Equal((32 * Day, 38 * Day), (boundsEmpty.FirstTick, boundsEmpty.LastTick));
        Assert.Equal((10d, 1200d), (boundsEmpty.MinValue, boundsEmpty.MaxValue));
        Assert.Equal(0, tracker.CountVisibleMeasured(primaryMeasured, emptyZoom));

        // 4. Same-day zoom [25d, 25d]
        var sameDayZoom = new HistoryTickRange(25 * Day, 25 * Day);
        var boundsSameDay = tracker.ComputeBounds(primary, primaryMeasured, sameDayZoom);
        Assert.Equal((25 * Day, 25 * Day), (boundsSameDay.FirstTick, boundsSameDay.LastTick));
        Assert.Equal((250d, 250d), (boundsSameDay.MinValue, boundsSameDay.MaxValue));
        Assert.Equal(1, tracker.CountVisibleMeasured(primaryMeasured, sameDayZoom));

        // 5. Return to full range restores exact full bounds, points, and hover candidates
        Assert.Equal(fullBounds, tracker.ComputeBounds(primary, primaryMeasured, visibleRange: null));
        Assert.Equal(3, entryA.GetOrThinPoints(200, visibleRange: null).Count);
        Assert.Equal(3, entryA.GetVisibleMeasured(visibleRange: null).Length);
        Assert.Equal(2, entryB.GetOrThinPoints(200, visibleRange: null).Count);
        Assert.Equal(7, tracker.CountVisibleMeasured(primaryMeasured, visibleRange: null));
    }

    [Fact]
    public void ComparisonSelection_ExcludesOutOfRangePointsPreservesUnderlyingContinuityAndRestoresOnFullRange()
    {
        // Running-total checkpoint history:
        // 0: 10d (5, Complete, since 0)
        // 1: 20d (8, Complete, since 0, continues 0)
        // 2: 30d (null gap, continues 1)
        // 3: 40d (12, Partial due to gap, since 0, continues 2)
        // 4: 50d (15, Complete, restarted since 45d, continues 3)
        // 5: 60d (20, Complete, since 45d, continues 4)
        var points = new MetricHistoryPoint[]
        {
            new(10 * Day, new NumericValue { Number = 5, Coverage = MetricCoverage.Complete, Gapless = true, SinceTick = 0, ThroughTick = 10 * Day }),
            new(20 * Day, new NumericValue { Number = 8, Coverage = MetricCoverage.Complete, Gapless = true, SinceTick = 0, ThroughTick = 20 * Day }, ConnectToPrevious: true),
            new(30 * Day, null, ConnectToPrevious: true),
            new(40 * Day, new NumericValue { Number = 12, Coverage = MetricCoverage.Partial, Gapless = false, SinceTick = 0, ThroughTick = 40 * Day }, ConnectToPrevious: true),
            new(50 * Day, new NumericValue { Number = 15, Coverage = MetricCoverage.Complete, Gapless = true, SinceTick = 45 * Day, ThroughTick = 50 * Day }, ConnectToPrevious: true),
            new(60 * Day, new NumericValue { Number = 20, Coverage = MetricCoverage.Complete, Gapless = true, SinceTick = 45 * Day, ThroughTick = 60 * Day }, ConnectToPrevious: true),
        };
        var history = new MetricHistory(MetricHistoryKind.CheckpointLineage, points);

        // Full range FirstAndLast is (0, 5)
        Assert.Equal((0, 5), HistoryChanges.FirstAndLast(history, visibleRange: null));

        // Zoom to [20d, 50d]: out-of-range indices 0 and 5, plus gap index 2, cannot be picked
        var zoomMid = new HistoryTickRange(20 * Day, 50 * Day);
        Assert.False(HistoryChanges.CanPick(history, 0, zoomMid));
        Assert.True(HistoryChanges.CanPick(history, 1, zoomMid));
        Assert.False(HistoryChanges.CanPick(history, 2, zoomMid)); // gap
        Assert.True(HistoryChanges.CanPick(history, 3, zoomMid));
        Assert.True(HistoryChanges.CanPick(history, 4, zoomMid));
        Assert.False(HistoryChanges.CanPick(history, 5, zoomMid));

        // FirstAndLast in [20d, 50d] picks (1, 4) — never index 0 or 5 — and full-history CounterGap is still detected!
        Assert.Equal((1, 4), HistoryChanges.FirstAndLast(history, zoomMid));
        var midChange = HistoryChanges.Compare(RunningTotal, history, 1, 4);
        Assert.Equal(HistoryChangeIssue.CounterGap, midChange.Issue);

        // Zoom to [48d, 60d]: indices 4 and 5 are in range, FirstAndLast picks (4, 5) with valid +5 increase (RecordedSince 45d)
        var zoomLate = new HistoryTickRange(48 * Day, 60 * Day);
        Assert.Equal((4, 5), HistoryChanges.FirstAndLast(history, zoomLate));
        var lateChange = HistoryChanges.Compare(RunningTotal, history, 4, 5);
        Assert.Equal(HistoryChangeIssue.None, lateChange.Issue);
        Assert.Equal(5d, lateChange.Delta);
        Assert.Equal(45 * Day, lateChange.RecordedSince);

        // Existing selection is kept when inside zoomLate, and cleared when an endpoint falls outside zoomMid
        Assert.Equal(((int?)4, lateChange), HistoryChanges.FilterSelection(history, 4, lateChange, zoomLate));
        Assert.Equal(((int?)null, (HistoryChange?)null), HistoryChanges.FilterSelection(history, 0, lateChange, zoomMid));

        // Zoom with 0 or 1 measured points yields null FirstAndLast
        Assert.Null(HistoryChanges.FirstAndLast(history, new HistoryTickRange(25 * Day, 35 * Day))); // only gap
        Assert.Null(HistoryChanges.FirstAndLast(history, new HistoryTickRange(18 * Day, 35 * Day))); // only index 1

        // Resetting to null (full range) restores full selection eligibility
        Assert.Equal((0, 5), HistoryChanges.FirstAndLast(history, visibleRange: null));
        Assert.True(HistoryChanges.CanPick(history, 0, visibleRange: null));
        Assert.True(HistoryChanges.CanPick(history, 5, visibleRange: null));
    }

    [Fact]
    public void FirstAndLast_InZoomedBranchingHistory_FindsInRangeAncestorOrReportsNotLinked()
    {
        // 0: 10d (a) ─┬─ 1: 20d (b2) ── 3: 35d (c2)
        //             └─ 2: 30d (b1) ── 4: 40d (c1)
        var history = new MetricHistory(MetricHistoryKind.SaveObservations,
        [
            new MetricHistoryPoint(10 * Day, Val(100, MetricCoverage.SnapshotOnly)),
            new MetricHistoryPoint(20 * Day, Val(120, MetricCoverage.SnapshotOnly), ConnectToPrevious: true),
            new MetricHistoryPoint(30 * Day, Val(300, MetricCoverage.SnapshotOnly)) { ContinuesFrom = 0, SkipsOnlyOtherBranches = true },
            new MetricHistoryPoint(35 * Day, Val(135, MetricCoverage.SnapshotOnly)) { ContinuesFrom = 1, SkipsOnlyOtherBranches = true },
            new MetricHistoryPoint(40 * Day, Val(340, MetricCoverage.SnapshotOnly)) { ContinuesFrom = 2, SkipsOnlyOtherBranches = true },
        ]);

        // Zoom [20d, 40d]: last is 4 (c1), whose in-range ancestor is 2 (b1), NOT sibling 1 (b2) and NOT out-of-range 0 (a)
        var zoomBoth = new HistoryTickRange(20 * Day, 40 * Day);
        Assert.Equal((2, 4), HistoryChanges.FirstAndLast(history, zoomBoth));
        Assert.Equal(40d, HistoryChanges.Compare(SaveMetric, history, 2, 4).Delta);

        // Zoom [20d, 30d]: only siblings b2(1) and b1(2) are visible -> returns (1, 2) whose comparison explains NotLinked
        var zoomSiblings = new HistoryTickRange(20 * Day, 30 * Day);
        Assert.Equal((1, 2), HistoryChanges.FirstAndLast(history, zoomSiblings));
        Assert.Equal(HistoryChangeIssue.NotLinked, HistoryChanges.Compare(SaveMetric, history, 1, 2).Issue);
    }
}
