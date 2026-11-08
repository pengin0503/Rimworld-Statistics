namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · caravan (ledger Q-008…Q-014, N2-E-001, N2-E-003, N3-X-007, N3-X-008, N4-R-011; evidence #caravan). An expedition
/// runs from a departure to the caravan entering a player home; a split, a merge or any other removal ends its tracking without
/// a completed sample.
/// <para>
/// Q-014: in 1.7.4985 an ancient danger is the sealed temple of <c>GenStep_ScatterShrines</c> (player settlement maps only):
/// walls without doors around a fogged interior, marked by a <c>RectTrigger</c> whose signal tag starts with
/// "ancientTempleApproached-" and which the game itself treats as revealed once the centre cell of its rect is unfogged
/// (<c>destroyIfUnfogged</c>). A shrine is watched while that centre is fogged and counts once, when <c>FogGrid.Unfog</c>
/// clears it; a wall destroyed elsewhere or an enemy appearing is not an opening. A shrine first seen already open is not counted.
/// </para>
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
    public const string AncientDangersOpened = "exploration.ancient_danger_opened";

    // Revision 2 added Q-014: a revision-1 checkpoint did not watch shrines and is never continued.
    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 2, Build = Build };

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
    private const string Shrine = "shrine";             // active: shrine key → tick it was first seen sealed
    private const string ShrinesOpened = "shrines_opened";   // distinct shrine keys opened (each one once)
    private const string ShrineMissed = "shrines_missed";    // Overflow: a watched shrine was found open without its opening seen

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

    /// <summary>The key of the shrine whose warning trigger has <paramref name="mapId"/> and this rect (inclusive bounds).</summary>
    public static string ShrineKey(string mapId, int minX, int minZ, int maxX, int maxZ)
        => FormattableString.Invariant($"{mapId}|{minX},{minZ},{maxX},{maxZ}");

    public static bool TryParseShrineKey(string key, out string mapId, out int minX, out int minZ, out int maxX, out int maxZ)
    {
        mapId = "";
        minX = minZ = maxX = maxZ = 0;
        var bar = key.IndexOf('|');
        if (bar <= 0)
            return false;
        var parts = key[(bar + 1)..].Split(',');
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        const System.Globalization.NumberStyles Int = System.Globalization.NumberStyles.AllowLeadingSign;
        if (parts.Length != 4 || !int.TryParse(parts[0], Int, inv, out minX) || !int.TryParse(parts[1], Int, inv, out minZ)
            || !int.TryParse(parts[2], Int, inv, out maxX) || !int.TryParse(parts[3], Int, inv, out maxZ) || maxX < minX || maxZ < minZ)
            return false;
        mapId = key[..bar];
        return true;
    }

    /// <summary>A shrine seen with its centre still fogged: watched until it opens (never again once it has opened).</summary>
    public static void ShrineSealed(FamilyState s, string key, int tick)
    {
        if (s.Peek(ShrinesOpened)?.By?.ContainsKey(key) == true || s.Tracked(Shrine, key) is not null)
            return;
        s.Track(Shrine, key, tick);
    }

    /// <summary>The centre of a watched shrine was unfogged: it was opened.</summary>
    public static void ShrineUnsealed(FamilyState s, string key)
    {
        if (s.Untrack(Shrine, key) is null)
            return;
        s.Distinct(ShrinesOpened, key);
    }

    /// <summary>
    /// A watched shrine was found open without its opening having been seen (e.g. at a load after a missed event): it is not
    /// counted, and the count is partial from then on.
    /// </summary>
    public static void ShrineFoundOpen(FamilyState s, string key)
    {
        if (s.Untrack(Shrine, key) is null)
            return;
        s.Acc(ShrineMissed).Overflow = true;
    }

    /// <summary>A map was removed: its shrines are no longer watched.</summary>
    public static void ShrinesOnMapGone(FamilyState s, string mapId)
    {
        if (!s.Active.TryGetValue(Shrine, out var watched))
            return;
        var prefix = mapId + "|";
        foreach (var key in watched.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
            s.Untrack(Shrine, key);
    }

    /// <summary>The shrines watched now, in key order (the adapter's lookup is rebuilt from these).</summary>
    public static IReadOnlyList<string> WatchedShrines(FamilyState s)
        => s.Active.TryGetValue(Shrine, out var watched) ? watched.Keys.Order(StringComparer.Ordinal).ToList() : new List<string>();

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
        c.PutDistinct(ShrinesOpened, AncientDangersOpened);
        // An expedition dropped at the cap is neither a completed sample nor a loss.
        if (c.State.TrackerOverflowed(Start) || c.State.TrackerOverflowed(Dist))
            c.MarkPartial(LongestExpedition, LongestDistance, Lost_);
        // A shrine not watched at the cap, or found open unseen, can be missing from the count.
        if (c.State.TrackerOverflowed(Shrine) || c.State.Peek(ShrineMissed)?.Overflow == true)
            c.MarkPartial(AncientDangersOpened);
    }
}
