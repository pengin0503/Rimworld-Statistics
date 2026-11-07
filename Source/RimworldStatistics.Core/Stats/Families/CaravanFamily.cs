namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · caravan (ledger Q-008…Q-013, N2-E-001, N2-E-003, N3-X-007, N3-X-008, N4-R-011; evidence #caravan). An expedition
/// runs from a departure to the caravan entering a player home; a split, a merge or any other removal ends its tracking without
/// a completed sample. Held: Q-014 (no confirmed notification of unsealing an ancient danger in 1.7.4985).
/// </summary>
public static class CaravanFamily
{
    public const string Name = "caravan";

    public const string Departures = "caravan.departures";
    public const string Lost_ = "caravan.lost";
    public const string TilesTravelled = "caravan.tiles_travelled";
    public const string UniqueTiles = "caravan.unique_tiles";
    public const string SitesCleared = "exploration.enemy_sites_cleared";
    public const string SettlementsAbandoned = "exploration.settlements_abandoned";
    public const string LongestExpedition = "caravan.duration_max";
    public const string Ambushes = "caravan.ambushes";
    public const string LongestDistance = "caravan.distance_max";
    public const string SettlementsFounded = "exploration.settlements_founded";
    public const string ConcurrentMax = "caravan.concurrent_max";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Departed_ = "departures";
    private const string LostCount = "lost";
    private const string Tiles = "tiles";
    private const string Unique = "unique_tiles";
    private const string Duration = "duration";
    private const string Distance = "distance";
    private const string Ambush = "ambushes";
    private const string Cleared = "sites_cleared";
    private const string Abandon = "abandoned";
    private const string Found = "founded";
    private const string Concurrent_ = "concurrent";
    private const string Start = "start";   // active: caravan id → departure tick
    private const string Dist = "dist";     // active: caravan id → tiles walked

    public static void Departed(FamilyState s, string caravanId, int tick)
    {
        s.Count(Departed_);
        s.Track(Start, caravanId, tick);
        s.Track(Dist, caravanId, 0);
    }

    /// <summary>A player caravan entered its next path tile; only a step to an adjacent tile is walking.</summary>
    public static void Moved(FamilyState s, string caravanId, string tileKey, bool adjacent)
    {
        if (!adjacent)
            return;
        s.Count(Tiles);
        s.Distinct(Unique, tileKey);
        if (s.Tracked(Dist, caravanId) is { } d)
            s.Track(Dist, caravanId, d + 1);
    }

    public static void Returned(FamilyState s, string caravanId, int tick)
    {
        var start = s.Untrack(Start, caravanId);
        var dist = s.Untrack(Dist, caravanId);
        if (start is not { } st || tick < st)
            return;
        s.Observe(Duration, tick - st);
        s.Observe(Distance, dist ?? 0);
    }

    public static void Aborted(FamilyState s, string caravanId)
    {
        s.Untrack(Start, caravanId);
        s.Untrack(Dist, caravanId);
    }

    /// <summary>A tracked caravan's last member died.</summary>
    public static void Lost(FamilyState s, string caravanId)
    {
        if (s.Untrack(Start, caravanId) is null)
            return;
        s.Untrack(Dist, caravanId);
        s.Count(LostCount);
    }

    public static void Ambushed(FamilyState s) => s.Count(Ambush);
    public static void SiteCleared(FamilyState s) => s.Count(Cleared);
    public static void Abandoned(FamilyState s) => s.Count(Abandon);
    public static void Founded(FamilyState s) => s.Count(Found);
    public static void Concurrent(FamilyState s, int caravans) => s.Observe(Concurrent_, caravans);

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Departed_, Departures);
        c.PutCount(LostCount, Lost_);
        c.PutCount(Tiles, TilesTravelled);
        c.PutDistinct(Unique, UniqueTiles);
        c.PutExtreme(Duration, LongestExpedition, max: true, 1.0 / StatFormat.TicksPerDay);
        var d = c.State.Peek(Distance);
        c.Put(LongestDistance, d?.Max is { } dm ? c.Value(integer: (long)dm, samples: (int)(d.N ?? 0)) : c.Value(coverage: MetricCoverage.NotApplicable));
        c.PutCount(Ambush, Ambushes);
        c.PutCount(Cleared, SitesCleared);
        c.PutCount(Abandon, SettlementsAbandoned);
        c.PutCount(Found, SettlementsFounded);
        var k = c.State.Peek(Concurrent_);
        c.Put(ConcurrentMax, k?.Max is { } km ? c.Value(integer: (long)km) : c.Value(coverage: MetricCoverage.NotApplicable));
        // An expedition dropped at the cap is neither a completed sample nor a loss.
        if (c.State.TrackerOverflowed(Start) || c.State.TrackerOverflowed(Dist))
            c.MarkPartial(LongestExpedition, LongestDistance, Lost_);
    }
}
