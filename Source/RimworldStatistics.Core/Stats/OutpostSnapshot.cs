namespace RimworldStatistics.Core;

/// <summary>GEN-OUT-001: active VEF Outposts at one successful save; never a production/event counter.</summary>
public readonly record struct OutpostState(string? DefName, int? PawnCount);

public static class OutpostSnapshot
{
    public const string Unit = "outpost_state";
    public const string PackageId = "oskarpotocki.vanillafactionsexpanded.core";
    public const string Pawns = "exploration.outposts.pawns";
    public const string ByType = "exploration.outposts.by_type";
    public const string Active = "exploration.outposts.active";

    /// <summary>Recognize the VEF Outposts.Outpost base and its subclasses without referencing a third-party DLL.</summary>
    public static bool IsOutpostType(Type? type)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (string.Equals(current.FullName, "Outposts.Outpost", StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>Count of live player-owned outposts in a representative saved state.</summary>
    public static Dictionary<string, MetricEntry> Build(int activeCount)
    {
        if (activeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(activeCount));
        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [Active] = MetricEntry.Of(MetricKeys.Revision(Active),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = activeCount }),
        };
    }
    public static Dictionary<string, MetricEntry> Build(IReadOnlyList<OutpostState> outposts)
    {
        var result = Build(outposts.Count);
        var groups = outposts.Where(outpost => !string.IsNullOrWhiteSpace(outpost.DefName))
            .GroupBy(outpost => outpost.DefName!, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
            .Take(FamilyOps.KeyCap + 1).ToArray();
        var partialType = groups.Length > FamilyOps.KeyCap || outposts.Any(outpost => string.IsNullOrWhiteSpace(outpost.DefName));
        var byType = MetricEntry.Of(MetricKeys.Revision(ByType), Value(outposts.Count, partialType));
        foreach (var group in groups.Take(FamilyOps.KeyCap))
            byType.Values[group.Key] = Value(group.Count(), partialType);
        result[ByType] = byType;

        var known = outposts.Where(outpost => outpost.PawnCount is >= 0).ToArray();
        if (outposts.Count == 0 || known.Length > 0)
            result[Pawns] = MetricEntry.Of(MetricKeys.Revision(Pawns),
                Value(known.Sum(outpost => (long)outpost.PawnCount!.Value), known.Length != outposts.Count));
        return result;

        static MetricValue Value(long count, bool partial) => new()
        {
            Coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly,
            IntegerValue = count,
        };
    }

}
