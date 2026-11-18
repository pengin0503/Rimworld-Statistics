namespace RimworldStatistics.Core;

/// <summary>
/// Why a colony has no point in a <see cref="MetricScatterPlot"/>. When both values have a problem, the one listed first
/// here is reported (with the axes it applies to), so each colony is counted under exactly one reason.
/// </summary>
public enum ScatterExclusionReason
{
    /// <summary>The colony is excluded from the statistics in the settings.</summary>
    Excluded,

    /// <summary>The colony is not in the selected colony group.</summary>
    OutOfScope,

    /// <summary>The statistic does not apply to the colony.</summary>
    NotApplicable,

    /// <summary>A threshold the colony never reached: a known result, but not a number to place on an axis.</summary>
    NotReached,

    /// <summary>A known reading without one number (a 0 ÷ 0 rate, or a reading kept only by category).</summary>
    NoSingleValue,

    /// <summary>No reading: not recorded, unknown, or not a finite number.</summary>
    Missing,
}

/// <summary>The axes a <see cref="ScatterExclusion"/> applies to.</summary>
[Flags]
public enum ScatterAxes
{
    None = 0,
    X = 1,
    Y = 2,
    Both = X | Y,
}

/// <summary>One plotted value, exactly as the rankings and colony charts show it for the colony.</summary>
public sealed record ScatterValue(double Number, MetricCoverage Coverage, MetricEvidence Evidence, int? ThroughTick);

/// <summary>One colony's point: its X and Y values from the same view.</summary>
public sealed record ScatterPoint(string ColonyKey, ScatterValue X, ScatterValue Y)
{
    public bool HasPartial => X.Coverage == MetricCoverage.Partial || Y.Coverage == MetricCoverage.Partial;

    /// <summary>Both values name the tick they run to, and the ticks differ: they describe different moments.</summary>
    public bool ObservedAtDifferentTicks => X.ThroughTick is { } x && Y.ThroughTick is { } y && x != y;

    /// <summary>The point is drawn, but marked: one value is partial, or the two values are of different moments.</summary>
    public bool NeedsCaution => HasPartial || ObservedAtDifferentTicks;
}

/// <summary>A colony in the plot's scope without a point, and why.</summary>
public sealed record ScatterExclusion(string ColonyKey, ScatterExclusionReason Reason, ScatterAxes Axes);

/// <summary>One axis: the statistic, how its values should be read, and its zero-inclusive ticks.</summary>
public sealed record ScatterAxis(
    string MetricKey, MetricDefinition Definition, bool IsBasic, ColonyReportValueKind ValueKind, IReadOnlyList<double> Ticks)
{
    /// <summary>
    /// Where <paramref name="value"/> lies between the first and last tick, from 0 to 1 (0.5 when the ticks have no span).
    /// Values are scaled before subtracting, so even opposite-sign finite extremes stay representable.
    /// </summary>
    public double Position(double value)
    {
        if (Ticks.Count < 2 || !double.IsFinite(value))
            return 0.5d;
        var lo = Ticks[0];
        var hi = Ticks[^1];
        var scale = Math.Max(1d, Math.Max(Math.Abs(lo), Math.Abs(hi)));
        var span = hi / scale - lo / scale;
        if (!double.IsFinite(span) || span <= 0d)
            return 0.5d;
        var position = (value / scale - lo / scale) / span;
        return double.IsFinite(position) ? Math.Clamp(position, 0d, 1d) : 0.5d;
    }
}

/// <summary>Points drawn at one spot: every colony whose marker would cover the first one's.</summary>
public sealed record ScatterCluster(double X, double Y, IReadOnlyList<int> PointIndices);

/// <summary>
/// Read-only cross-colony scatter plot of two statistics (V2-D). Each colony in the view's scope gives one point when it has
/// a finite value for both statistics, read through the same path as the rankings and colony charts
/// (<see cref="BasicMetrics.Value"/> and the colony's numeric rows). Nothing is filled in: a colony without both values is
/// listed with its reason instead, never drawn at 0. No correlation or trend line is computed.
/// </summary>
public sealed class MetricScatterPlot
{
    /// <summary>Below this many points, no pattern can be read from the plot, and the plot says so.</summary>
    public const int FewPoints = 3;

    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private enum AxisState
    {
        // Ordered as ScatterExclusionReason's per-value reasons: the lowest is reported.
        NotApplicable = ScatterExclusionReason.NotApplicable,
        NotReached = ScatterExclusionReason.NotReached,
        NoSingleValue = ScatterExclusionReason.NoSingleValue,
        Missing = ScatterExclusionReason.Missing,
        Value = int.MaxValue,
    }

    private readonly record struct AxisRead(AxisState State, ScatterValue? Value);

    public required ScatterAxis X { get; init; }
    public required ScatterAxis Y { get; init; }

    /// <summary>One per plotted colony, in colony key order.</summary>
    public required IReadOnlyList<ScatterPoint> Points { get; init; }

    /// <summary>Every other colony of the view, by reason and then colony key.</summary>
    public required IReadOnlyList<ScatterExclusion> Exclusions { get; init; }

    public int CautionCount { get; init; }

    public int ColonyCount => Points.Count + Exclusions.Count;

    public bool TooFewPoints => Points.Count < FewPoints;

    public int Count(ScatterExclusionReason reason) => Exclusions.Count(e => e.Reason == reason);

    public int Count(ScatterExclusionReason reason, ScatterAxes axes)
        => Exclusions.Count(e => e.Reason == reason && e.Axes == axes);

    /// <summary>A statistic the rankings can rank: a basic statistic, or a per-colony numeric one that is not derived.</summary>
    public static bool CanPlot(StatsView view, string metricKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(metricKey);
        return BasicMetrics.Contains(metricKey)
            || (MetricLookup.Definition(view, metricKey) is { } definition && Plottable(definition));
    }

    /// <summary>The plot of <paramref name="yKey"/> against <paramref name="xKey"/>; null when either cannot be plotted.</summary>
    public static MetricScatterPlot? Build(StatsView view, string xKey, string yKey)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(xKey);
        ArgumentNullException.ThrowIfNull(yKey);
        if (Axis(view, xKey) is not { } x || Axis(view, yKey) is not { } y)
            return null;

        var numeric = new Dictionary<string, NumericColony>(Ord);
        foreach (var colony in view.Numeric.Colonies)
            numeric.TryAdd(colony.Key, colony);

        var points = new List<ScatterPoint>();
        var exclusions = new List<ScatterExclusion>();
        foreach (var colony in view.Stats.Colonies.OrderBy(c => c.Key, Ord))
        {
            if (colony.Excluded)
            {
                exclusions.Add(new ScatterExclusion(colony.Key, ScatterExclusionReason.Excluded, ScatterAxes.None));
                continue;
            }
            if (colony.OutOfScope)
            {
                exclusions.Add(new ScatterExclusion(colony.Key, ScatterExclusionReason.OutOfScope, ScatterAxes.None));
                continue;
            }

            numeric.TryGetValue(colony.Key, out var rows);
            var xRead = Read(x, colony, rows);
            var yRead = Read(y, colony, rows);
            if (xRead.Value is { } xValue && yRead.Value is { } yValue)
            {
                points.Add(new ScatterPoint(colony.Key, xValue, yValue));
                continue;
            }

            var state = (AxisState)Math.Min((int)xRead.State, (int)yRead.State);
            var axes = (xRead.State == state ? ScatterAxes.X : ScatterAxes.None)
                | (yRead.State == state ? ScatterAxes.Y : ScatterAxes.None);
            exclusions.Add(new ScatterExclusion(colony.Key, (ScatterExclusionReason)state, axes));
        }

        var ordered = exclusions.OrderBy(e => e.Reason).ThenBy(e => e.ColonyKey, Ord).ToArray();
        return new MetricScatterPlot
        {
            X = x with { Ticks = Ticks(points.Select(p => p.X.Number)) },
            Y = y with { Ticks = Ticks(points.Select(p => p.Y.Number)) },
            Points = points,
            Exclusions = ordered,
            CautionCount = points.Count(p => p.NeedsCaution),
        };
    }

    /// <summary>
    /// Groups the points for a plot area of <paramref name="width"/> × <paramref name="height"/> pixels (y up from the
    /// bottom): a point joins the first cluster whose position lies within <paramref name="spacing"/> of its own, so
    /// markers that would cover one another are drawn once with their count. Clusters are in first-member order.
    /// </summary>
    public IReadOnlyList<ScatterCluster> Cluster(double width, double height, double spacing)
    {
        var w = double.IsFinite(width) && width > 0d ? width : 1d;
        var h = double.IsFinite(height) && height > 0d ? height : 1d;
        var cell = double.IsFinite(spacing) && spacing > 0d ? spacing : 1d;

        var clusters = new List<(double X, double Y, List<int> Members)>();
        var buckets = new Dictionary<(long, long), List<int>>();
        for (var i = 0; i < Points.Count; i++)
        {
            var px = X.Position(Points[i].X.Number) * w;
            var py = Y.Position(Points[i].Y.Number) * h;
            var bx = (long)Math.Floor(px / cell);
            var by = (long)Math.Floor(py / cell);

            var joined = -1;
            for (var dx = -1L; dx <= 1 && joined < 0; dx++)
                for (var dy = -1L; dy <= 1 && joined < 0; dy++)
                    if (buckets.TryGetValue((bx + dx, by + dy), out var nearby))
                        foreach (var c in nearby)
                        {
                            var (cx, cy, _) = clusters[c];
                            if (Math.Abs(cx - px) <= cell && Math.Abs(cy - py) <= cell
                                && (joined < 0 || c < joined))
                                joined = c;
                        }

            if (joined >= 0)
            {
                clusters[joined].Members.Add(i);
                continue;
            }
            if (!buckets.TryGetValue((bx, by), out var bucket))
                buckets[(bx, by)] = bucket = [];
            bucket.Add(clusters.Count);
            clusters.Add((px, py, [i]));
        }
        return clusters.Select(c => new ScatterCluster(c.X, c.Y, c.Members)).ToArray();
    }

    private static bool Plottable(MetricDefinition definition)
        => definition.Overall != OverallAggregation.Ranking && !CrossColonyCatalog.IsDerived(definition);

    private static ScatterAxis? Axis(StatsView view, string key)
    {
        if (BasicMetrics.Contains(key))
        {
            var basic = BasicMetrics.Definition(key);
            return new ScatterAxis(key, basic, true, ColonySummaryReport.ClassifyValueKind(basic, isBasic: true), []);
        }
        return MetricLookup.Definition(view, key) is { } definition && Plottable(definition)
            ? new ScatterAxis(key, definition, false, ColonySummaryReport.ClassifyValueKind(definition), [])
            : null;
    }

    private static AxisRead Read(ScatterAxis axis, ColonyStats colony, NumericColony? numeric)
    {
        if (axis.IsBasic)
        {
            // The rankings' and colony chart's basic reading: the colony's latest observed save, as a snapshot.
            return BasicMetrics.Value(colony, axis.MetricKey) is { } number && double.IsFinite(number)
                ? new AxisRead(AxisState.Value, new ScatterValue(number, MetricCoverage.SnapshotOnly,
                    MetricEvidence.UnknownLegacy, colony.HasObservedPoint ? colony.MaxTicks : null))
                : new AxisRead(AxisState.Missing, null);
        }

        var row = numeric?.Rows.FirstOrDefault(r => r.Definition.Key == axis.MetricKey);
        if (row is null)
            return new AxisRead(AxisState.Missing, null);
        var value = row.Value;
        if (value.Coverage == MetricCoverage.NotApplicable)
            return new AxisRead(AxisState.NotApplicable, null);
        if (value.Coverage == MetricCoverage.Unknown)
            return new AxisRead(AxisState.Missing, null);
        if (value.Number is { } n)
            return double.IsFinite(n)
                ? new AxisRead(AxisState.Value, new ScatterValue(n, value.Coverage, value.Evidence, value.ThroughTick))
                : new AxisRead(AxisState.Missing, null);
        if (value.NotReached)
            return new AxisRead(AxisState.NotReached, null);
        return value.Known ? new AxisRead(AxisState.NoSingleValue, null) : new AxisRead(AxisState.Missing, null);
    }

    private static IReadOnlyList<double> Ticks(IEnumerable<double> values)
    {
        double? min = null, max = null;
        foreach (var v in values)
        {
            min = min is { } a ? Math.Min(a, v) : v;
            max = max is { } b ? Math.Max(b, v) : v;
        }
        return MetricChartTicks.Generate(min ?? 0d, max ?? 0d);
    }
}
