namespace RimworldStatistics.Core;

/// <summary>Only facts from a live, non-temporary faction with a unique stable identity are eligible.</summary>
public readonly record struct FactionStateFact(string Id, bool Active, bool Ally, bool Hostile, int? Goodwill);

/// <summary>Faction counts and optional goodwill range, scoped to player-to-faction relations.</summary>
public static class FactionStateSnapshot
{
    public const string Relations = "diplomacy.snapshot.relations";
    public const string GoodwillRange = "diplomacy.snapshot.goodwill_range";

    public static Dictionary<string, MetricEntry> Build(IEnumerable<FactionStateFact> factions, bool partial = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long allies = 0;
        long hostiles = 0;
        int? min = null;
        int? max = null;
        foreach (var f in factions)
        {
            if (!f.Active)
                continue;
            if (string.IsNullOrEmpty(f.Id) || !seen.Add(f.Id) || (f.Ally && f.Hostile))
            {
                partial = true;
                continue;
            }
            if (f.Ally) allies++;
            if (f.Hostile) hostiles++;
            if (f.Goodwill is { } v)
            {
                min = min.HasValue ? Math.Min(min.Value, v) : v;
                max = max.HasValue ? Math.Max(max.Value, v) : v;
            }
        }
        var c = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var related = MetricEntry.Of(MetricKeys.Revision(Relations),
            new MetricValue { Coverage = c, IntegerValue = allies + hostiles });
        related.Values["ally"] = new MetricValue { Coverage = c, IntegerValue = allies };
        related.Values["hostile"] = new MetricValue { Coverage = c, IntegerValue = hostiles };
        // Range has no meaningful additive scalar. It is a per-colony breakdown.
        var range = MetricEntry.Of(MetricKeys.Revision(GoodwillRange), new MetricValue
        {
            Coverage = MetricCoverage.NotApplicable,
        });
        if (min.HasValue)
            range.Values["min"] = new MetricValue { Coverage = c, IntegerValue = min };
        if (max.HasValue)
            range.Values["max"] = new MetricValue { Coverage = c, IntegerValue = max };
        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [Relations] = related, [GoodwillRange] = range,
        };
    }
}
