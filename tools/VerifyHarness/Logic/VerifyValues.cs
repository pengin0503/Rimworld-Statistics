using System.Globalization;
using RimworldStatistics.Core;

namespace RimworldStatistics.VerifyHarness;

/// <summary>One stored value of an observation, flattened for the harness: a scalar ("key") or a breakdown entry ("key[bucket]").</summary>
public sealed record FlatValue(string Text, double? Number, MetricCoverage Coverage, int? Samples)
{
    /// <summary>The value as the log shows it: number, rate parts, sample count and any coverage other than complete.</summary>
    public string Display
    {
        get
        {
            var s = Text;
            if (Samples is { } n)
                s += " n=" + n.ToString(CultureInfo.InvariantCulture);
            if (Coverage != MetricCoverage.Complete)
                s += " [" + Coverage + "]";
            return s;
        }
    }
}

/// <summary>A value that is not the same before and after (null: it did not exist on that side).</summary>
public sealed record FlatChange(string Key, string? Unit, FlatValue? Before, FlatValue? After)
{
    /// <summary>After − before for numeric values (a missing side counts as 0 only when the other side is a count).</summary>
    public double? Delta => (Before?.Number, After?.Number) switch
    {
        ({ } b, { } a) => a - b,
        (null, { } a) when Before is null => a,
        _ => null,
    };
}

/// <summary>
/// Game-independent helpers of the verification harness: flatten an observation, compare two of them, and find the acquisition
/// unit of a stored key. Compiled into the harness and into the Core tests; never shipped with the mod.
/// </summary>
public static class VerifyValues
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    private static readonly Dictionary<string, string?> UnitByStoredKey = MetricRegistry.Catalog
        .GroupBy(d => d.StoredKey, Ord)
        .ToDictionary(g => g.Key, g => g.Select(d => d.Acquisition).FirstOrDefault(a => a != null), Ord);

    /// <summary>The acquisition unit an observation key belongs to (a breakdown entry belongs to its key's unit).</summary>
    public static string? UnitOf(string flatKey)
    {
        var i = flatKey.IndexOf('[');
        var key = i < 0 ? flatKey : flatKey[..i];
        return UnitByStoredKey.GetValueOrDefault(key);
    }

    public static string Format(MetricValue v)
    {
        string N(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
        if (v.IntegerValue is { } i)
            return i.ToString(CultureInfo.InvariantCulture);
        var parts = v.Numerator is { } num && v.Denominator is { } den ? " (" + N(num) + "/" + N(den) + ")" : "";
        if (v.RealValue is { } r)
            return N(r) + parts;
        return parts.Length > 0 ? parts.Trim() : "—";
    }

    /// <summary>Every value of <paramref name="o"/>, keyed "key" for a scalar and "key[bucket]" for a breakdown entry.</summary>
    public static SortedDictionary<string, FlatValue> Flatten(MetricObservation? o)
    {
        var result = new SortedDictionary<string, FlatValue>(Ord);
        if (o is null)
            return result;
        foreach (var (key, entry) in o.Metrics)
        {
            if (entry.IsQuarantined)
                continue;
            foreach (var (bucket, v) in entry.Values)
                result[bucket == MetricEntry.Scalar ? key : key + "[" + bucket + "]"] = new FlatValue(Format(v), v.Number, v.Coverage, v.SampleCount);
        }
        return result;
    }

    /// <summary>The values that differ (text, coverage or sample count), in key order.</summary>
    public static List<FlatChange> Diff(IReadOnlyDictionary<string, FlatValue> before, IReadOnlyDictionary<string, FlatValue> after)
    {
        var keys = new SortedSet<string>(before.Keys, Ord);
        keys.UnionWith(after.Keys);
        var changes = new List<FlatChange>();
        foreach (var key in keys)
        {
            before.TryGetValue(key, out var b);
            after.TryGetValue(key, out var a);
            if (b != a)
                changes.Add(new FlatChange(key, UnitOf(key), b, a));
        }
        return changes;
    }

    /// <summary>The change of one flat key's number between two snapshots (0 when neither side has a value).</summary>
    public static double DeltaOf(IReadOnlyDictionary<string, FlatValue> before, IReadOnlyDictionary<string, FlatValue> after, string key)
        => (after.GetValueOrDefault(key)?.Number ?? 0) - (before.GetValueOrDefault(key)?.Number ?? 0);

    /// <summary>The change of one flat key's sample count between two snapshots.</summary>
    public static int SamplesDeltaOf(IReadOnlyDictionary<string, FlatValue> before, IReadOnlyDictionary<string, FlatValue> after, string key)
        => (after.GetValueOrDefault(key)?.Samples ?? 0) - (before.GetValueOrDefault(key)?.Samples ?? 0);
}
