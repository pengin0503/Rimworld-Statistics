namespace RimworldStatistics.Core;

/// <summary>Bounded, detached references serialized by MapInfo and WorldObject.ExposeData.</summary>
internal readonly record struct SavedMapParent(int? UniqueId, string? Parent, bool? Pocket, bool Valid);
internal readonly record struct SavedWorldMapObject(int? Id, string? Def, string? Faction, bool Valid);
/// <summary>A narrowly recognized saved Building_Turret or Building_TurretGun instance.</summary>
internal readonly record struct SavedMapTurret(int? MapId, string? ThingId, string? Faction, bool Valid);
/// <summary>Only direct, saved, vanilla Silver/Medicine stacks; no contents or MOD Def inference.</summary>
internal readonly record struct SavedMapStockItem(
    int? MapId, string? ThingId, string? DefName, int StackCount,
    string? Faction, bool Forbidden, bool Valid);

/// <summary>
/// Detached direct-map Pawn evidence. No names, skills, ideology, health details,
/// genealogy, MOD race data or runtime Pawn references are retained.
/// </summary>
internal readonly record struct SavedMapPawn(
    int? MapId, string? ThingId, string? RaceDef, string? KindDef,
    string? Faction, string? HostFaction, string? GuestStatus,
    bool GuestPresent, bool HostFactionSeen, bool HealthPresent,
    bool Dead, bool Valid);

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
    public const int MaxSavedTurrets = 8192;
    public const int MaxSavedStockItems = 16384;
    public const int MaxTrustedStockStack = 1_000_000;
    public const int MaxSavedPawns = 8192;

    public static Dictionary<string, MetricEntry>? Build(
        IReadOnlyList<SavedMapParent> maps,
        IReadOnlyList<SavedWorldMapObject> objects,
        IReadOnlyList<SavedFaction> factions,
        string? scenarioPlayerDef,
        bool mapsOverflowed,
        bool worldObjectsOverflowed,
        bool factionsOverflowed,
        IReadOnlyList<SavedMapTurret>? turrets = null,
        bool turretsOverflowed = false,
        IReadOnlyList<SavedMapStockItem>? stock = null,
        bool stockOverflowed = false,
        IReadOnlyList<SavedMapPawn>? pawns = null,
        bool pawnsOverflowed = false)
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

        var confirmedMapIds = new HashSet<int>();
        foreach (var map in maps)
            if (map.Valid && map.UniqueId is >= 0 && map.Pocket != true
                && map.Parent is { Length: > 0 } parent && homeParents.Contains(parent))
                confirmedMapIds.Add(map.UniqueId!.Value);
        var confirmed = (long)confirmedMapIds.Count;
        // A partial zero cannot establish that this save has no player homes:
        // the home may be a gravship or a MOD-provided map parent.
        if (confirmed == 0)
            return null;

        var key = FacilitiesSnapshot.HomeMapsPeak;
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
            {
                Coverage = MetricCoverage.Partial,
                IntegerValue = confirmed,
            }),
        };
        // A positive lower bound, never a historical maximum or proof that
        // every MOD turret class is recognized. Only actual game subclasses
        // Building_TurretGun / Building_Turret are captured by the stream reader.
        if (turrets is { Count: > 0 } && !turretsOverflowed && turrets.Count <= MaxSavedTurrets)
        {
            // An identical ThingID observed in multiple places/factions is ambiguous.
            // Only a unanimously owned, verified-home group counts once.
            var verifiedCount = turrets.Where(t => !string.IsNullOrWhiteSpace(t.ThingId))
                .GroupBy(t => t.ThingId!, StringComparer.Ordinal)
                .LongCount(group => group.All(t => t.Valid && t.MapId is { } id
                    && confirmedMapIds.Contains(id) && t.Faction == playerReference));
            if (verifiedCount > 0)
            {
                var turretKey = FacilitiesSnapshot.TurretsPeak;
                result[turretKey] = MetricEntry.Of(MetricKeys.Revision(turretKey), new MetricValue
                {
                    Coverage = MetricCoverage.Partial,
                    IntegerValue = verifiedCount,
                });
            }
        }
        // In the audited 1.7 Core Def XML Silver is a ThingWithComps,
        // and the three vanilla medicine defs are RimWorld.Medicine.
        // The streaming reader only admits matching Class+Def pairs; these
        // direct on-map stacks are NOT the full contents of a colony's holders.
        if (stock is { Count: > 0 } && !stockOverflowed && stock.Count <= MaxSavedStockItems)
        {
            long silver = 0, medicine = 0;
            foreach (var group in stock.Where(t => !string.IsNullOrWhiteSpace(t.ThingId))
                .GroupBy(t => t.ThingId!, StringComparer.Ordinal))
            {
                var first = group.First();
                // Conflicting duplicate IDs, hostile objects, forbidden objects,
                // invalid stack counts and non-home maps do not provide evidence.
                if (!group.All(t => t.Valid && t.MapId is { } id && confirmedMapIds.Contains(id)
                    && (t.Faction is null || t.Faction == playerReference)
                    && !t.Forbidden && t.StackCount > 0 && t.StackCount <= MaxTrustedStockStack
                    && t.DefName == first.DefName && t.StackCount == first.StackCount))
                    continue;
                if (first.DefName == "Silver")
                    silver += first.StackCount;
                else if (first.DefName is "MedicineHerbal" or "MedicineIndustrial" or "MedicineUltratech")
                    medicine += first.StackCount;
            }
            // Do not emit a zero from incomplete map/holder/forbidden data.
            if (silver > 0)
            {
                var stockKey = StockSnapshot.SilverLatest;
                result[stockKey] = MetricEntry.Of(MetricKeys.Revision(stockKey), new MetricValue
                {
                    Coverage = MetricCoverage.Partial, IntegerValue = silver,
                });
            }
            if (medicine > 0)
            {
                var medicineKey = StockSnapshot.MedicinePeak;
                result[medicineKey] = MetricEntry.Of(MetricKeys.Revision(medicineKey), new MetricValue
                {
                    Coverage = MetricCoverage.Partial, IntegerValue = medicine,
                });
            }
        }
        // Deliberately positive-only, conservative direct-map human roster:
        // 1.7 actual Pawn API: IsFreeNonSlaveColonist = free && !IsSlave;
        // IsPrisonerOfColony = guest.IsPrisoner && guest.HostFaction.IsPlayer;
        // IsSlaveOfColony = IsSlave && Faction.IsPlayer.
        // joinStatus is NOT guestStatus: even free colonists can have JoinAsSlave.
        // World pawns, caravans, MOD races and nested holders are not traversed,
        // so an unobserved category remains Unknown rather than measured zero.
        if (pawns is { Count: > 0 } && !pawnsOverflowed && pawns.Count <= MaxSavedPawns)
        {
            long free = 0, prisoners = 0, slaves = 0;
            foreach (var group in pawns.Where(p => !string.IsNullOrWhiteSpace(p.ThingId))
                .GroupBy(p => p.ThingId!, StringComparer.Ordinal))
            {
                var first = group.First();
                // A repeated ID with disagreeing classification is ambiguous.
                if (!group.All(p => p.Valid && p.MapId is { } id
                    && confirmedMapIds.Contains(id)
                    && p.RaceDef == "Human" && p.GuestPresent && p.HostFactionSeen
                    && p.HealthPresent && !p.Dead && p.Equals(first)))
                    continue;
                if (first.GuestStatus == "Prisoner" && first.HostFaction == playerReference)
                    prisoners++;
                else if (first.GuestStatus == "Slave" && first.Faction == playerReference)
                    slaves++;
                else if (first.Faction == playerReference && first.HostFaction is null
                    && first.KindDef == "Colonist"
                    && first.GuestStatus is (null or "Guest"))
                    free++;
            }
            void RecordIfPositive(string key, long n)
            {
                if (n > 0)
                    result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
                    {
                        Coverage = MetricCoverage.Partial,
                        IntegerValue = n,
                    });
            }
            RecordIfPositive(RosterSnapshot.Free, free);
            RecordIfPositive(RosterSnapshot.Prisoners, prisoners);
            RecordIfPositive(RosterSnapshot.Slaves, slaves);
        }

        return result;
    }
}
