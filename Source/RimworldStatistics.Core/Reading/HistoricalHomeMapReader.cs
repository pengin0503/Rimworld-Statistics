namespace RimworldStatistics.Core;

/// <summary>Bounded, detached references serialized by MapInfo and WorldObject.ExposeData.</summary>
internal readonly record struct SavedMapParent(int? UniqueId, string? Parent, bool? Pocket, bool Valid);
internal readonly record struct SavedWorldMapObject(int? Id, string? Def, string? Faction, bool Valid);

/// <summary>
/// Partial lower bound for player home maps in pre-install saves. Only explicit
/// WorldObject references to vanilla player-owned Settlement parents count.
/// In particular, MOD/DLC homes, gravships and maps with unresolved parents
/// remain unknown. The scalar is one saved-state observation, NOT a lifetime peak.
/// </summary>
internal static class HistoricalHomeMapReader
{
    public const int MaxMaps = 128;
    public const int MaxWorldObjects = 4096;

    public static Dictionary<string, MetricEntry>? Build(
        IReadOnlyList<SavedMapParent> maps,
        IReadOnlyList<SavedWorldMapObject> objects,
        IReadOnlyList<SavedFaction> factions,
        string? scenarioPlayerDef,
        bool mapsOverflowed,
        bool worldObjectsOverflowed,
        bool factionsOverflowed)
    {
        if (mapsOverflowed || worldObjectsOverflowed || factionsOverflowed || maps.Count == 0
            || maps.Count > MaxMaps || objects.Count > MaxWorldObjects)
            return null;
        // Without the exact historical Def database, restrict this to the two
        // known vanilla player factions and explicit saved IDs.
        if (scenarioPlayerDef is { Length: > 0 }
            && scenarioPlayerDef is not ("PlayerColony" or "PlayerTribe"))
            return null;
        var players = factions.Where(f =>
            f.Def is ("PlayerColony" or "PlayerTribe")
            && (scenarioPlayerDef is null || f.Def == scenarioPlayerDef)).ToArray();
        if (players.Length != 1 || !players[0].Valid || !players[0].Active
            || players[0].LoadId is not { } playerId || playerId < 0)
            return null;
        // An unrelated faction using the player's explicit ID also breaks
        // the attribution proof; omitted IDs do not authorize a fallback.
        var factionIds = new HashSet<int>();
        foreach (var faction in factions)
            if (faction.LoadId is { } id && !factionIds.Add(id))
                return null;

        // Any duplicate explicit parent or map ID makes the reference graph
        // ambiguous. Never resolve it by list order or by an assumed default.
        var objectIds = new HashSet<int>();
        foreach (var obj in objects)
            if (obj.Id is { } id && !objectIds.Add(id))
                return null;
        var mapIds = new HashSet<int>();
        foreach (var map in maps)
            if (map.UniqueId is { } id && !mapIds.Add(id))
                return null;

        var playerReference = "Faction_" + playerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var homeParents = objects
            .Where(o => o.Valid && o.Id is >= 0 && o.Def == "Settlement" && o.Faction == playerReference)
            .Select(o => "WorldObject_" + o.Id!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .ToHashSet(StringComparer.Ordinal);
        if (homeParents.Count == 0)
            return null;

        var confirmed = 0L;
        foreach (var map in maps)
            if (map.Valid && map.UniqueId is >= 0 && map.Pocket != true
                && map.Parent is { Length: > 0 } parent && homeParents.Contains(parent))
                confirmed++;
        // A partial zero cannot establish that this save has no player homes:
        // the home may be a gravship or a MOD-provided map parent.
        if (confirmed == 0)
            return null;

        var key = FacilitiesSnapshot.HomeMapsPeak;
        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
            {
                Coverage = MetricCoverage.Partial,
                IntegerValue = confirmed,
            }),
        };
    }
}
