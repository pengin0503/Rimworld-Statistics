namespace RimworldStatistics.Core;

/// <summary>
/// Summarizes the history recorder series of one save into Phase A metrics (numeric expansion design §3 "履歴の境界",
/// ledger sections wealth and population). Only summaries are kept; the series themselves stay in this call's memory.
/// </summary>
/// <remarks>
/// A point is an observation every 30000 ticks (a nominal half day, as HistoryAutoRecorderGroup draws it), not a measured
/// time. Outputs that use time (days, windows, first reach) are made only for a "start-aligned" series: a game version
/// whose cadence is verified, ticksGame &gt; 0, floor(ticksGame / 30000) + 1 points and a valid first point. Other series
/// give only maximum, minimum, last value and the plain mean of valid points. Missing points are never filled or bridged;
/// a value computed with points left out is Partial.
/// </remarks>
public static class HistoryMetricReader
{
    public const int TicksPerPoint = 30000;
    public const int MaxPoints = 262144;
    public const int MaxBase64Chars = 2097152;
    private const int PointsPer15Days = 30;
    private const int PointsPerYear = 120;

    public const string WealthTotal = "Wealth_Total";
    public const string WealthItems = "Wealth_Items";
    public const string WealthBuildings = "Wealth_Buildings";
    public const string WealthPawns = "Wealth_Pawns";
    public const string FreeColonists = "FreeColonists";
    public const string Prisoners = "Prisoners";
    public const string ColonistMood = "ColonistMood";

    /// <summary>The recorder defs read for Phase A; each records every 30000 ticks (1.7.4985 HistoryAutoRecorders.xml).</summary>
    public static readonly IReadOnlySet<string> Defs = new HashSet<string>(StringComparer.Ordinal)
        { WealthTotal, WealthItems, WealthBuildings, WealthPawns, FreeColonists, Prisoners, ColonistMood };

    /// <summary>
    /// Versions whose 30000-tick cadence is verified: 1.7 by its Defs, 1.5 and 1.6 by real saves (point counts match the
    /// ticks in every save examined). Other versions get no time-based outputs.
    /// </summary>
    private static readonly HashSet<string> TimedVersions = new(StringComparer.Ordinal) { "1.5", "1.6", "1.7" };

    /// <summary>"1.6.4566 rev607" → "1.6"; null when it has no major.minor.</summary>
    public static string? MajorMinor(string? gameVersion)
    {
        if (gameVersion is null)
            return null;
        var parts = gameVersion.Trim().Split(' ')[0].Split('.');
        return parts.Length >= 2 && parts[0].Length > 0 && parts[1].Length > 0 && parts[0].All(char.IsAsciiDigit) && parts[1].All(char.IsAsciiDigit)
            ? parts[0] + "." + parts[1]
            : null;
    }

    public static bool IsTimedVersion(string? gameVersion) => MajorMinor(gameVersion) is { } v && TimedVersions.Contains(v);

    private sealed class Series
    {
        public required float[] X { get; init; }
        public required bool[] Valid { get; init; }
        public int N => X.Length;
        public bool AnyMissing { get; init; }
        public bool Aligned { get; init; }
    }

    /// <param name="series">Decoded series by recorder def; null for a series that is present but unusable.</param>
    public static void Summarize(IReadOnlyDictionary<string, float[]?> series, string? gameVersion, int ticksGame, IDictionary<string, MetricEntry> into)
    {
        var timed = IsTimedVersion(gameVersion);
        Series? Get(string def, Func<float, bool> valid)
        {
            if (!series.TryGetValue(def, out var x) || x is null || x.Length == 0)
                return null;
            var ok = x.Select(valid).ToArray();
            return new Series
            {
                X = x,
                Valid = ok,
                AnyMissing = ok.Any(v => !v),
                Aligned = timed && ticksGame > 0 && x.Length == ticksGame / TicksPerPoint + 1 && ok[0],
            };
        }

        static bool Wealth(float v) => float.IsFinite(v) && v >= 0;
        static bool Count(float v) => float.IsFinite(v) && v >= 0;
        static bool Mood(float v) => float.IsFinite(v) && v > 0 && v <= 100;   // 0 is what the worker records without colonists

        void Put(string key, MetricValue? value)
        {
            if (value is not null)
                into[key] = MetricEntry.Of(MetricKeys.Revision(key), value);
        }

        var total = Get(WealthTotal, Wealth);
        var buildings = Get(WealthBuildings, Wealth);
        foreach (var (def, peak, latest) in new[]
        {
            (WealthItems, MetricKeys.WealthItemsPeak, MetricKeys.WealthItemsLatest),
            (WealthBuildings, MetricKeys.WealthBuildingsPeak, MetricKeys.WealthBuildingsLatest),
            (WealthPawns, MetricKeys.WealthPawnsPeak, MetricKeys.WealthPawnsLatest),
        })
        {
            var s = def == WealthBuildings ? buildings : Get(def, Wealth);
            Put(peak, Max(s));
            Put(latest, Latest(s));
        }
        Put(MetricKeys.WealthBuildingsMaxShare, MaxRatio(buildings, total));

        if (total is not null)
        {
            Put(MetricKeys.WealthTotalMean, Mean(total));
            if (total.Aligned)
            {
                var invalid = total.AnyMissing ? InvalidPrefix(total) : null;
                Put(MetricKeys.WealthTotalFirst, Snap(total.X[0], false));
                Put(MetricKeys.WealthTotalNet, Net(total));
                Put(MetricKeys.WealthTotalPeakDay, PeakDay(total));
                Put(MetricKeys.WealthTotalMaxGainHalfDay, Window(total, invalid, 1, gain: true));
                Put(MetricKeys.WealthTotalMaxLossHalfDay, Window(total, invalid, 1, gain: false));
                Put(MetricKeys.WealthTotalMaxLossRateHalfDay, MaxLossRate(total));
                Put(MetricKeys.WealthTotalMaxGain15D, Window(total, invalid, PointsPer15Days, gain: true));
                Put(MetricKeys.WealthTotalMaxLoss15D, Window(total, invalid, PointsPer15Days, gain: false));
                Put(MetricKeys.WealthTotalMaxGrowthRateYear, MaxGrowthRate(total, invalid, PointsPerYear));
                Put(MetricKeys.WealthTotalLongestGrowth, LongestGrowth(total));
                Put(MetricKeys.WealthTotalFirst100K, FirstReach(total, 100000));
                Put(MetricKeys.WealthTotalFirst1M, FirstReach(total, 1000000));
            }
        }

        var free = Get(FreeColonists, Count);
        Put(MetricKeys.FreeColonistsMean, Mean(free));
        Put(MetricKeys.FreeColonistsMin, Min(free));
        if (free is { Aligned: true })
            Put(MetricKeys.FreeColonistsFirst10, FirstReach(free, 10));
        Put(MetricKeys.WealthPerColonistMax, MaxRatio(total, free));

        var prisoners = Get(Prisoners, Count);
        Put(MetricKeys.PrisonersPeak, Max(prisoners));
        Put(MetricKeys.PrisonersMean, Mean(prisoners));

        var mood = Get(ColonistMood, Mood);
        Put(MetricKeys.MoodMean, Mean(mood));
        Put(MetricKeys.MoodMin, Min(mood));
        Put(MetricKeys.MoodMax, Max(mood));
        if (mood is { Aligned: true })
        {
            Put(MetricKeys.MoodBelow50Days, IntervalDays(mood, left => left < 50));
            Put(MetricKeys.MoodAtLeast80Days, IntervalDays(mood, left => left >= 80));
        }
    }

    private static MetricValue Snap(double value, bool partial)
        => new() { Coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly, RealValue = value };

    /// <summary>Not defined for this series: too short for the window, or no point meets the definition (e.g. a positive start).</summary>
    private static MetricValue NotApplicable() => new() { Coverage = MetricCoverage.NotApplicable };

    private static MetricValue? Max(Series? s) => Extreme(s, max: true);

    private static MetricValue? Min(Series? s) => Extreme(s, max: false);

    private static MetricValue? Extreme(Series? s, bool max)
    {
        if (s is null)
            return null;
        double? best = null;
        for (var i = 0; i < s.N; i++)
            if (s.Valid[i] && (best is null || (max ? s.X[i] > best : s.X[i] < best)))
                best = s.X[i];
        return best is { } b ? Snap(b, s.AnyMissing) : null;
    }

    /// <summary>The last point; an invalid last point is unknown, never replaced by an earlier one.</summary>
    private static MetricValue? Latest(Series? s) => s is not null && s.Valid[^1] ? Snap(s.X[^1], false) : null;

    private static MetricValue? Mean(Series? s)
    {
        if (s is null)
            return null;
        double sum = 0;
        var n = 0;
        for (var i = 0; i < s.N; i++)
        {
            if (!s.Valid[i])
                continue;
            sum += s.X[i];
            n++;
        }
        if (n == 0)
            return null;
        var v = Snap(sum / n, s.AnyMissing);
        v.Numerator = sum;
        v.Denominator = n;
        return v;
    }

    /// <summary>Last − first; needs both ends, a gap in between makes it Partial. The point count gives the years spanned.</summary>
    private static MetricValue? Net(Series s)
    {
        if (!s.Valid[^1])
            return null;
        var v = Snap((double)s.X[^1] - s.X[0], s.AnyMissing);
        v.SampleCount = s.N;
        return v;
    }

    private static MetricValue? PeakDay(Series s)
    {
        var best = -1;
        for (var i = 0; i < s.N; i++)
            if (s.Valid[i] && (best < 0 || s.X[i] > s.X[best]))
                best = i;
        return best < 0 ? null : Snap(best / 2.0, s.AnyMissing);
    }

    /// <summary>Number of invalid points in [from, to].</summary>
    private static int[] InvalidPrefix(Series s)
    {
        var prefix = new int[s.N + 1];
        for (var i = 0; i < s.N; i++)
            prefix[i + 1] = prefix[i] + (s.Valid[i] ? 0 : 1);
        return prefix;
    }

    /// <summary>Largest rise (or fall) between points <paramref name="span"/> apart, over windows without a missing point; never negative.</summary>
    private static MetricValue? Window(Series s, int[]? invalid, int span, bool gain)
    {
        if (s.N <= span)
            return NotApplicable();
        var windows = s.N - span;
        var used = 0;
        double best = 0;
        for (var i = 0; i + span < s.N; i++)
        {
            if (invalid is not null && invalid[i + span + 1] != invalid[i])
                continue;
            used++;
            var d = gain ? (double)s.X[i + span] - s.X[i] : (double)s.X[i] - s.X[i + span];
            best = Math.Max(best, d);
        }
        return used == 0 ? null : Snap(best, used < windows);
    }

    /// <summary>max(0, (before − after) ÷ before) over consecutive valid points with before &gt; 0.</summary>
    private static MetricValue? MaxLossRate(Series s)
    {
        if (s.N < 2)
            return NotApplicable();
        double? best = null, num = null, den = null;
        var skipped = false;
        for (var i = 0; i + 1 < s.N; i++)
        {
            if (!s.Valid[i] || !s.Valid[i + 1])
            {
                skipped = true;
                continue;
            }
            if (s.X[i] <= 0)
                continue;
            var loss = Math.Max(0, (double)s.X[i] - s.X[i + 1]);
            var rate = loss / s.X[i];
            if (best is null || rate > best)
                (best, num, den) = (rate, loss, s.X[i]);
        }
        if (best is null)
            return skipped ? null : NotApplicable();
        var v = Snap(best.Value, skipped);
        v.Numerator = num;
        v.Denominator = den;
        return v;
    }

    /// <summary>Largest (after − before) ÷ before over windows <paramref name="span"/> points apart without a gap; may be negative.</summary>
    private static MetricValue? MaxGrowthRate(Series s, int[]? invalid, int span)
    {
        if (s.N <= span)
            return NotApplicable();
        double? best = null, num = null, den = null;
        var skipped = false;
        for (var i = 0; i + span < s.N; i++)
        {
            if (invalid is not null && invalid[i + span + 1] != invalid[i])
            {
                skipped = true;
                continue;
            }
            if (s.X[i] <= 0)
                continue;
            var change = (double)s.X[i + span] - s.X[i];
            var rate = change / s.X[i];
            if (best is null || rate > best)
                (best, num, den) = (rate, change, s.X[i]);
        }
        if (best is null)
            return skipped ? null : NotApplicable();
        var v = Snap(best.Value, skipped);
        v.Numerator = num;
        v.Denominator = den;
        return v;
    }

    /// <summary>Longest run of strictly increasing consecutive points ÷ 2 days; equal values and gaps end a run.</summary>
    private static MetricValue? LongestGrowth(Series s)
    {
        if (s.N < 2)
            return NotApplicable();
        int run = 0, best = 0, pairs = 0;
        for (var i = 0; i + 1 < s.N; i++)
        {
            if (!s.Valid[i] || !s.Valid[i + 1])
            {
                run = 0;
                continue;
            }
            pairs++;
            run = s.X[i + 1] > s.X[i] ? run + 1 : 0;
            best = Math.Max(best, run);
        }
        return pairs == 0 ? null : Snap(best / 2.0, s.AnyMissing);
    }

    /// <summary>Nominal day of the first point at or above the threshold; every earlier point must be valid. Not reached: no value.</summary>
    private static MetricValue? FirstReach(Series s, double threshold)
    {
        for (var i = 0; i < s.N; i++)
        {
            if (!s.Valid[i])
                return null;
            if (s.X[i] >= threshold)
                return Snap(i / 2.0, false);
        }
        return new MetricValue { Coverage = MetricCoverage.SnapshotOnly };
    }

    /// <summary>Half-day intervals with both ends valid whose left end matches, ÷ 2.</summary>
    private static MetricValue? IntervalDays(Series s, Func<float, bool> left)
    {
        if (s.N < 2)
            return NotApplicable();
        int pairs = 0, matching = 0;
        for (var i = 0; i + 1 < s.N; i++)
        {
            if (!s.Valid[i] || !s.Valid[i + 1])
                continue;
            pairs++;
            if (left(s.X[i]))
                matching++;
        }
        return pairs == 0 ? null : Snap(matching / 2.0, s.AnyMissing);
    }

    /// <summary>
    /// Largest numerator ÷ denominator at the same index (denominator &gt; 0). Both series must be start-aligned with the same
    /// number of points, so an index means the same time in both.
    /// </summary>
    private static MetricValue? MaxRatio(Series? numerator, Series? denominator)
    {
        if (numerator is not { Aligned: true } || denominator is not { Aligned: true } || numerator.N != denominator.N)
            return null;
        double? best = null, num = null, den = null;
        var skipped = false;
        for (var i = 0; i < numerator.N; i++)
        {
            if (!numerator.Valid[i] || !denominator.Valid[i])
            {
                skipped = true;
                continue;
            }
            if (denominator.X[i] <= 0)
                continue;
            var r = (double)numerator.X[i] / denominator.X[i];
            if (best is null || r > best)
                (best, num, den) = (r, numerator.X[i], denominator.X[i]);
        }
        if (best is null)
            return skipped ? null : NotApplicable();
        var v = Snap(best.Value, skipped);
        v.Numerator = num;
        v.Denominator = den;
        return v;
    }
}
