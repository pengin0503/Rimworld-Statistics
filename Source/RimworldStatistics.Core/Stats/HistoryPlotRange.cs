using System.Globalization;

namespace RimworldStatistics.Core;

/// <summary>
/// Inclusive in-game tick range [StartTick, EndTick] for zooming a per-colony history plot.
/// Reversed endpoints are normalized so StartTick &lt;= EndTick, and negative ticks are clamped to 0.
/// </summary>
public readonly record struct HistoryTickRange
{
    public int StartTick { get; }
    public int EndTick { get; }

    public HistoryTickRange(int firstTick, int secondTick)
    {
        var lo = Math.Min(firstTick, secondTick);
        var hi = Math.Max(firstTick, secondTick);
        StartTick = Math.Max(0, lo);
        EndTick = Math.Max(0, hi);
    }

    public bool Contains(int tick) => tick >= StartTick && tick <= EndTick;

    public bool IsSingleTick => StartTick == EndTick;
}

public enum HistoryRangeStatus
{
    FullRange = 0,
    Zoomed = 1,
    InvalidInput = 2,
}

public readonly record struct HistoryRangeResolution(
    HistoryRangeStatus Status,
    HistoryTickRange? Range,
    bool WasReversed = false);

/// <summary>
/// Resolves user-entered in-game day inputs into a normalized <see cref="HistoryTickRange"/> without mutating stored history.
/// </summary>
public static class HistoryPlotRange
{
    /// <summary>Parses a non-negative finite day number (accepting invariant '.' or a single decimal ',').</summary>
    public static bool TryParseDay(string? text, out double day)
    {
        day = 0d;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed[0] == '-')
            return false;

        string normalized = trimmed;
        if (!trimmed.Contains('.') && trimmed.IndexOf(',') is var comma && comma >= 0)
        {
            if (trimmed.IndexOf(',', comma + 1) >= 0)
                return false;
            normalized = trimmed.Replace(',', '.');
        }

        const NumberStyles style = NumberStyles.AllowLeadingWhite
            | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowLeadingSign;

        if (!double.TryParse(normalized, style, CultureInfo.InvariantCulture, out var parsed)
            || !double.IsFinite(parsed)
            || parsed < 0d)
        {
            return false;
        }

        day = parsed;
        return true;
    }

    public static int DayToTick(double day)
    {
        if (!double.IsFinite(day) || day <= 0d)
            return 0;
        var scaled = Math.Round(day * StatFormat.TicksPerDay, MidpointRounding.AwayFromZero);
        return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
    }

    /// <summary>
    /// Resolves optional start and end day inputs against the full plot bounds [fullFirstTick, fullLastTick].
    /// Both blank returns <see cref="HistoryRangeStatus.FullRange"/>; reversed inputs normalize to ascending order.
    /// </summary>
    public static HistoryRangeResolution Resolve(
        string? startText,
        string? endText,
        int fullFirstTick,
        int fullLastTick)
    {
        var hasStart = !string.IsNullOrWhiteSpace(startText);
        var hasEnd = !string.IsNullOrWhiteSpace(endText);
        if (!hasStart && !hasEnd)
            return new HistoryRangeResolution(HistoryRangeStatus.FullRange, null);

        double startDay = 0d;
        double endDay = 0d;
        if (hasStart && !TryParseDay(startText, out startDay))
            return new HistoryRangeResolution(HistoryRangeStatus.InvalidInput, null);
        if (hasEnd && !TryParseDay(endText, out endDay))
            return new HistoryRangeResolution(HistoryRangeStatus.InvalidInput, null);

        var safeFullFirst = Math.Max(0, Math.Min(fullFirstTick, fullLastTick));
        var safeFullLast = Math.Max(0, Math.Max(fullFirstTick, fullLastTick));

        if (hasStart && hasEnd)
        {
            var reversed = startDay > endDay;
            var loTick = DayToTick(Math.Min(startDay, endDay));
            var hiTick = DayToTick(Math.Max(startDay, endDay));
            return new HistoryRangeResolution(
                HistoryRangeStatus.Zoomed,
                new HistoryTickRange(loTick, hiTick),
                WasReversed: reversed);
        }

        if (hasStart)
        {
            var startTick = DayToTick(startDay);
            var endTick = Math.Max(startTick, safeFullLast);
            return new HistoryRangeResolution(
                HistoryRangeStatus.Zoomed,
                new HistoryTickRange(startTick, endTick));
        }
        else
        {
            var endTick = DayToTick(endDay);
            var startTick = endTick < safeFullFirst ? 0 : safeFullFirst;
            return new HistoryRangeResolution(
                HistoryRangeStatus.Zoomed,
                new HistoryTickRange(startTick, endTick));
        }
    }

    /// <summary>
    /// Filters measured points to <paramref name="visibleRange"/> while preserving original point instances
    /// so lineage tooltips and history indices continue to reference the underlying <see cref="MetricHistory"/>.
    /// </summary>
    public static MetricHistoryPoint[] FilterMeasured(
        MetricHistoryPoint[] measured,
        HistoryTickRange? visibleRange)
    {
        ArgumentNullException.ThrowIfNull(measured);
        if (visibleRange is not { } range || measured.Length == 0)
            return measured;
        if (range.Contains(measured[0].Tick) && range.Contains(measured[^1].Tick))
            return measured;

        var list = new List<MetricHistoryPoint>();
        for (var i = 0; i < measured.Length; i++)
        {
            if (range.Contains(measured[i].Tick))
                list.Add(measured[i]);
        }
        return list.Count == 0 ? [] : list.ToArray();
    }
}
