namespace RimworldStatistics.Core;

/// <summary>What the change between two readings of a colony's history means (#307).</summary>
public enum HistoryChangeKind
{
    /// <summary>The later value minus the earlier: how a saved state, or the statistic's own value, changed.</summary>
    Difference,

    /// <summary>
    /// What a running total counted between the two readings: a live total recorded without a gap from one start through
    /// both (or a vanilla counter kept since the game began), on one proven line of saves.
    /// </summary>
    Increase,
}

/// <summary>Why two readings cannot be compared; <see cref="None"/> when they can.</summary>
public enum HistoryChangeIssue
{
    None,

    /// <summary>The same reading twice, or two readings at one game tick: there is no interval.</summary>
    SamePoint,

    /// <summary>A reading has no value: a gap, or a value that is unknown or not applicable.</summary>
    NoValue,

    /// <summary>
    /// The later reading is not proven to continue the earlier one: another branch, a reload of an earlier save, a missing
    /// parent checkpoint, or saves without checkpoints. Readings of one campaign at different ticks prove nothing.
    /// </summary>
    NotLinked,

    /// <summary>
    /// A running total not proven to be recorded without a gap from one start through both readings: a gap, a recording
    /// stopped before a reading or started again between them, a value its family weakened (a cap reached), or a partial
    /// value read before gaps were kept. The difference is not proven to be what was counted in the interval.
    /// </summary>
    CounterGap,

    /// <summary>A running total that is lower at the later reading, which counting alone cannot give.</summary>
    CounterDecrease,

    /// <summary>The difference cannot be represented.</summary>
    Overflow,
}

/// <summary>Why the relative change is not shown when the change is; <see cref="None"/> when it is.</summary>
public enum HistoryRatioIssue
{
    None,

    /// <summary>The earlier value is 0: a change relative to it is not a number (never shown as infinite or 0%).</summary>
    ZeroBase,

    /// <summary>A scale without a true zero (degrees Celsius), on which a ratio means nothing.</summary>
    Unit,

    /// <summary>The ratio cannot be represented.</summary>
    Overflow,
}

/// <summary>The change between two readings of one colony's history, earlier one first (#307).</summary>
public sealed record HistoryChange
{
    /// <summary>The earlier reading's index in the history's points.</summary>
    public required int FromIndex { get; init; }

    /// <summary>The later reading's index in the history's points.</summary>
    public required int ToIndex { get; init; }

    public required MetricHistoryPoint From { get; init; }
    public required MetricHistoryPoint To { get; init; }
    public required HistoryChangeKind Kind { get; init; }
    public HistoryChangeIssue Issue { get; init; }

    /// <summary>The later value minus the earlier; null unless <see cref="Issue"/> is None.</summary>
    public double? Delta { get; init; }

    /// <summary>The change relative to the earlier value's magnitude (0.25 for +25%); null unless both issues are None.</summary>
    public double? Ratio { get; init; }

    public HistoryRatioIssue RatioIssue { get; init; }

    /// <summary>
    /// Either value is a partial record (only for a <see cref="HistoryChangeKind.Difference"/>): the change is between the
    /// values shown, which do not cover everything they name.
    /// </summary>
    public bool Partial { get; init; }

    /// <summary>
    /// For an <see cref="HistoryChangeKind.Increase"/> of a live total whose recording began after the game did: the tick it
    /// began. What it counts in the interval is exact, but an event already under way then (a raid that arrived before
    /// it, say) may never be counted when it ends.
    /// </summary>
    public int? RecordedSince { get; init; }

    /// <summary>
    /// The game ticks between the two readings' own ticks. A checkpoint that later saves carried unchanged counts at its own
    /// tick (<see cref="MetricHistoryPoint.SavedTick"/> is not a new reading).
    /// </summary>
    public long Ticks => (long)To.Tick - From.Tick;
}

/// <summary>
/// Compares two readings of one colony's history (#307). Only a reading the later one is proven to continue is compared:
/// the chart's own links (<see cref="MetricHistoryPoint.Predecessor"/>), which follow embedded checkpoint ids, or one save's
/// in-game recorder, which is that save's own past. Running totals are counted only over an interval they were recorded
/// in without a gap.
/// </summary>
public static class HistoryChanges
{
    /// <summary>A statistic whose readings are running totals of events, compared as an <see cref="HistoryChangeKind.Increase"/>.</summary>
    public static bool IsRunningTotal(MetricDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        // The game's own counters in a save, kept since the game began (the history's basic statistics).
        return definition.Key is BasicMetrics.Raids or BasicMetrics.Threats or BasicMetrics.Killed
            || MetricRates.IsAdditiveTotal(definition);
    }

    /// <summary>Whether the reading at <paramref name="to"/> is proven to continue the one at <paramref name="from"/>.</summary>
    public static bool Continues(MetricHistory history, int from, int to)
    {
        ArgumentNullException.ThrowIfNull(history);
        var points = history.Points;
        if (from < 0 || to >= points.Count || from >= to)
            return false;
        // One save's in-game recorder: every reading is that save's own past.
        if (history.Kind is MetricHistoryKind.GameRecorder or MetricHistoryKind.WealthRecorder)
            return true;
        for (var i = to; i > from;)
        {
            var previous = points[i].Predecessor(i);
            if (previous < from || previous >= i)
                return false;   // the line ends, or passes the earlier reading by: that one is not on it
            i = previous;
        }
        return true;
    }

    /// <summary>
    /// The readings "first and last" compares: the last reading with a value and the earliest reading with a value it is
    /// proven to continue. When there is no such earlier reading, the first and the last readings with a value, whose
    /// comparison then says why they cannot be compared. Null with fewer than two readings with a value.
    /// </summary>
    public static (int From, int To)? FirstAndLast(MetricHistory history)
        => FirstAndLast(history, visibleRange: null);

    /// <summary>
    /// Like <see cref="FirstAndLast(MetricHistory)"/>, restricted to readings inside <paramref name="visibleRange"/> so
    /// points outside a zoomed range are never selected for comparison, while keeping full-history lineage semantics.
    /// </summary>
    public static (int From, int To)? FirstAndLast(MetricHistory history, HistoryTickRange? visibleRange)
    {
        ArgumentNullException.ThrowIfNull(history);
        var points = history.Points;
        int first = -1, last = -1;
        for (var i = 0; i < points.Count; i++)
        {
            if (visibleRange is { } range && !range.Contains(points[i].Tick))
                continue;
            if (!HasValue(points[i]))
                continue;
            if (first < 0)
                first = i;
            last = i;
        }
        if (first < 0 || first == last)
            return null;
        if (history.Kind is MetricHistoryKind.GameRecorder or MetricHistoryKind.WealthRecorder)
            return (first, last);
        var earliest = -1;
        for (var i = last; ;)
        {
            var previous = points[i].Predecessor(i);
            if (previous < first || previous >= i)
                break;
            i = previous;
            if (visibleRange is { } range && !range.Contains(points[i].Tick))
                break;
            if (HasValue(points[i]))
                earliest = i;
        }
        return earliest >= 0 ? (earliest, last) : (first, last);
    }

    /// <summary>Whether the reading at <paramref name="index"/> is a selectable measured reading within <paramref name="visibleRange"/>.</summary>
    public static bool CanPick(MetricHistory history, int index, HistoryTickRange? visibleRange = null)
    {
        ArgumentNullException.ThrowIfNull(history);
        var points = history.Points;
        if (index < 0 || index >= points.Count || !HasValue(points[index]))
            return false;
        return visibleRange is not { } range || range.Contains(points[index].Tick);
    }

    /// <summary>Drops any in-progress or completed two-point comparison whose endpoint lies outside <paramref name="visibleRange"/>.</summary>
    public static (int? PickFrom, HistoryChange? Change) FilterSelection(
        MetricHistory history,
        int? pickFrom,
        HistoryChange? change,
        HistoryTickRange? visibleRange)
    {
        ArgumentNullException.ThrowIfNull(history);
        var filteredPick = pickFrom is { } first && CanPick(history, first, visibleRange) ? first : (int?)null;
        var filteredChange = change is { } c
            && CanPick(history, c.FromIndex, visibleRange)
            && CanPick(history, c.ToIndex, visibleRange)
            ? c : null;
        return (filteredPick, filteredChange);
    }

    /// <summary>The change between the readings at <paramref name="a"/> and <paramref name="b"/>, in either order.</summary>
    public static HistoryChange Compare(MetricDefinition definition, MetricHistory history, int a, int b)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(history);
        var points = history.Points;
        ArgumentOutOfRangeException.ThrowIfNegative(a);
        ArgumentOutOfRangeException.ThrowIfNegative(b);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(a, points.Count);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(b, points.Count);
        var (from, to) = points[a].Tick < points[b].Tick || (points[a].Tick == points[b].Tick && a <= b) ? (a, b) : (b, a);
        var earlier = points[from];
        var later = points[to];
        var change = new HistoryChange
        {
            FromIndex = from, ToIndex = to, From = earlier, To = later,
            Kind = IsRunningTotal(definition) ? HistoryChangeKind.Increase : HistoryChangeKind.Difference,
        };

        if (from == to || earlier.Tick == later.Tick)
            return change with { Issue = HistoryChangeIssue.SamePoint };
        if (!HasValue(earlier) || !HasValue(later))
            return change with { Issue = HistoryChangeIssue.NoValue };
        if (!Continues(history, from, to))
            return change with { Issue = HistoryChangeIssue.NotLinked };

        var start = earlier.Value!;
        var end = later.Value!;
        var x = start.Number!.Value;
        var y = end.Number!.Value;
        var partial = start.Coverage == MetricCoverage.Partial || end.Coverage == MetricCoverage.Partial;
        int? recordedSince = null;
        if (change.Kind == HistoryChangeKind.Increase)
        {
            // A live total counts the interval only when both readings were recorded without a gap from one start, through
            // each reading's own tick (a later start is not a gap); the game's counters in a save run from the game's start.
            // A partial vanilla counter never gives a counted interval.
            bool counted;
            if (definition.Source == MetricObservationSource.Checkpoint)
            {
                counted = Gapless(start, earlier.Tick) && Gapless(end, later.Tick) && start.SinceTick == end.SinceTick;
                if (counted && start.SinceTick > 0)
                    recordedSince = start.SinceTick;
            }
            else
            {
                counted = !partial;
            }
            if (!counted)
                return change with { Issue = HistoryChangeIssue.CounterGap };
            if (y < x)
                return change with { Issue = HistoryChangeIssue.CounterDecrease };
            partial = false;
        }

        var delta = y - x;
        if (!double.IsFinite(delta))
            return change with { Issue = HistoryChangeIssue.Overflow };
        change = change with { Delta = delta, Partial = partial, RecordedSince = recordedSince };
        if (definition.Unit == MetricUnit.Celsius)
            return change with { RatioIssue = HistoryRatioIssue.Unit };
        if (x == 0)
            return change with { RatioIssue = HistoryRatioIssue.ZeroBase };
        var ratio = delta / Math.Abs(x);
        return double.IsFinite(ratio)
            ? change with { Ratio = ratio }
            : change with { RatioIssue = HistoryRatioIssue.Overflow };
    }

    private static bool Gapless(NumericValue value, int tick)
        => value.Gapless && value.SinceTick is not null && value.ThroughTick == tick;

    private static bool HasValue(MetricHistoryPoint point)
        => point.Value is { Number: { } number, Coverage: not (MetricCoverage.Unknown or MetricCoverage.NotApplicable) }
            && double.IsFinite(number);
}
