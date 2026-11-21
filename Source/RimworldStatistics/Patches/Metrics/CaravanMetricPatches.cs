using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Caravans and settlements (evidence #caravan). A departure is a caravan created by leaving a map; walking is a path step to an
/// adjacent tile; an expedition completes when the caravan enters a player home map; any other removal of a tracked caravan
/// (a merge, a split's source being emptied, settling) ends it without a sample.
/// </summary>
internal static class CaravanProbe
{
    public static string Id(Caravan c) => "Caravan_" + c.ID;

    public static int PlayerCaravans()
    {
        var count = 0;
        var caravans = Find.WorldObjects.Caravans;
        for (var i = 0; i < caravans.Count; i++)
            if (caravans[i].IsPlayerControlled)
                count++;
        return count;
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan),
    new[] { typeof(IEnumerable<Pawn>), typeof(Faction), typeof(PlanetTile), typeof(PlanetTile), typeof(PlanetTile), typeof(bool) })]
public static class CaravanDepartMetricPatch
{
    public static void Postfix(Caravan __result)
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (__result is not { IsPlayerControlled: true } || !LiveMetrics.CanCaptureEvent(CaravanFamily.Name))
                return;
            var id = CaravanProbe.Id(__result);
            var tick = GameFacts.Tick;
            GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Departed(s, id, tick));
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(Caravan_PathFollower), "TryEnterNextPathTile")]
public static class CaravanStepMetricPatch
{
    public static void Prefix(Caravan ___caravan, out PlanetTile __state)
    {
        __state = PlanetTile.Invalid;
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try { __state = ___caravan is { IsPlayerControlled: true } ? ___caravan.Tile : PlanetTile.Invalid; }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }

    public static void Postfix(Caravan ___caravan, PlanetTile __state)
    {
        if (!__state.Valid || !LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (___caravan is not { IsPlayerControlled: true } || ___caravan.Tile == __state
                || !LiveMetrics.CanCaptureEvent(CaravanFamily.Name))
                return;
            var now = ___caravan.Tile;
            var adjacent = Find.WorldGrid.IsNeighbor(__state, now);
            var id = CaravanProbe.Id(___caravan);
            var tile = ColonyIdentity.NormalizeTile(now.ToString()) ?? now.ToString();
            GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Moved(s, id, tile, adjacent));
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter),
    new[] { typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool) })]
public static class CaravanReturnMetricPatch
{
    public static void Prefix(Caravan caravan, Map map)
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (caravan is not { IsPlayerControlled: true } || map is not { IsPlayerHome: true }
                || !LiveMetrics.CanCaptureEvent(CaravanFamily.Name))
                return;
            var id = CaravanProbe.Id(caravan);
            var tick = GameFacts.Tick;
            GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Returned(s, id, tick));
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(Caravan), nameof(Caravan.Notify_MemberDied))]
public static class CaravanMemberDiedMetricPatch
{
    public static void Postfix(Caravan __instance)
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (__instance is not { IsPlayerControlled: true } || !LiveMetrics.CanCaptureEvent(CaravanFamily.Name))
                return;
            var pawns = __instance.PawnsListForReading;
            for (var i = 0; i < pawns.Count; i++)
                if (!pawns[i].Dead)
                    return;
            var id = CaravanProbe.Id(__instance);
            GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Lost(s, id));
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Add))]
public static class CaravanAddedMetricPatch
{
    public static void Postfix(WorldObject o)
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (o is not Caravan { IsPlayerControlled: true } || !LiveMetrics.CanCaptureEvent(CaravanFamily.Name))
                return;
            var n = CaravanProbe.PlayerCaravans();
            GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Concurrent(s, n));
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Remove))]
public static class CaravanRemovedMetricPatch
{
    public static void Prefix(WorldObject o)
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (o is not Caravan { IsPlayerControlled: true } c || !LiveMetrics.CanCaptureEvent(CaravanFamily.Name))
                return;
            var id = CaravanProbe.Id(c);
            GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Aborted(s, id));   // a return or a loss already closed it
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
public static class CaravanAmbushMetricPatch
{
    /// <summary>IncidentDefs of 1.7.4985 Core.</summary>
    private static readonly HashSet<string> Ambushes = new(StringComparer.Ordinal) { "Ambush", "ManhunterAmbush" };

    public static void Postfix(bool __result, IncidentWorker __instance, IncidentParms parms)
    {
        if (!__result || !LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (__instance?.def != null && Ambushes.Contains(__instance.def.defName) && parms?.target is Caravan { IsPlayerControlled: true })
                GameFacts.Record(CaravanFamily.Name, CaravanFamily.Ambushed);
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
public static class SettlementDefeatedMetricPatch
{
    public static void Prefix(Settlement factionBase, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (factionBase is not { Destroyed: false } || factionBase.Faction == null)
                return;
            __state = GameFacts.IsHostileToPlayer(factionBase.Faction);
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }

    public static void Postfix(Settlement factionBase, bool __state)
    {
        if (!__state || !LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (factionBase is { Destroyed: true })
                GameFacts.Record(CaravanFamily.Name, CaravanFamily.SiteCleared);
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

/// <summary>Voluntary abandonment: the dialog's Abandon, which passes wasGravshipLaunch: false (1.7.4985).</summary>
[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(SettlementAbandonUtility), "Abandon")]
public static class AbandonMetricPatch
{
    public static void Postfix(MapParent settlement)
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (settlement != null)
                GameFacts.Record(CaravanFamily.Name, CaravanFamily.Abandoned);
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.Settle))]
public static class SettleEmptyMetricPatch
{
    public static void Postfix()
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try { GameFacts.Record(CaravanFamily.Name, CaravanFamily.Founded); }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(SettleInExistingMapUtility), nameof(SettleInExistingMapUtility.Settle))]
public static class SettleExistingMetricPatch
{
    public static void Postfix()
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try { GameFacts.Record(CaravanFamily.Name, CaravanFamily.Founded); }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

/// <summary>
/// Q-014 (1.7.4985): an ancient danger is the temple <c>GenStep_ScatterShrines</c> puts on player settlement maps, marked by a
/// <c>RectTrigger</c> with an "ancientTempleApproached-" signal tag and <c>destroyIfUnfogged</c> (the game's own "revealed" test is
/// its rect's centre cell being unfogged). Shrines are registered while that centre is fogged: at the start of recording, after
/// a map is generated, and when a colonist approaches (the trigger is destroyed then). The opening is the moment
/// <c>FogGrid.Unfog</c> clears a watched centre. The lookup by map is rebuilt from the family's watched keys after each change.
/// </summary>
internal static class ShrineProbe
{
    public const string SignalPrefix = "ancientTempleApproached-";

    private static readonly object Gate = new();
    private static volatile Dictionary<int, (CellRect Rect, string Key)[]> watched = new();
    private static readonly Dictionary<int, (CellRect Rect, string Key)[]> StagedMaps = new();

    public static bool Empty => watched.Count == 0;

    public static bool IsShrineTrigger(Thing t)
        => t is RectTrigger { destroyIfUnfogged: true, signalTag: { } tag } && tag.StartsWith(SignalPrefix, StringComparison.Ordinal);

    private static string Key(Map map, CellRect r) => CaravanFamily.ShrineKey(GameFacts.MapId(map), r.minX, r.minZ, r.maxX, r.maxZ);

    private static bool Sealed(Map map, CellRect r) => r.CenterCell.InBounds(map) && map.fogGrid.IsFogged(r.CenterCell);

    /// <summary>Registers the shrine of <paramref name="trigger"/> when its centre is still fogged.</summary>
    public static void Register(FamilyState s, RectTrigger trigger, int tick)
    {
        if (trigger.Map is not { } map || !Sealed(map, trigger.Rect))
            return;
        CaravanFamily.ShrineSealed(s, Key(map, trigger.Rect), tick);
    }

    public static List<(CellRect Rect, string Key)> CaptureSealedEntries(Map map)
    {
        var entries = new List<(CellRect Rect, string Key)>();
        foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.RectTrigger))
            if (IsShrineTrigger(t) && t is RectTrigger trigger && Sealed(map, trigger.Rect))
                entries.Add((trigger.Rect, Key(map, trigger.Rect)));
        return entries;
    }

    public static void StageMapEntries(int mapUniqueId, List<(CellRect Rect, string Key)> entries)
    {
        lock (Gate)
        {
            var array = entries.Count == 0 ? Array.Empty<(CellRect Rect, string Key)>() : entries.ToArray();
            StagedMaps[mapUniqueId] = array;
            var next = new Dictionary<int, (CellRect Rect, string Key)[]>(watched);
            if (array.Length == 0)
                next.Remove(mapUniqueId);
            else
                next[mapUniqueId] = array;
            watched = next;
        }
    }

    public static void RemoveStagedMap(int mapUniqueId)
    {
        lock (Gate)
        {
            StagedMaps.Remove(mapUniqueId);
            if (!watched.ContainsKey(mapUniqueId))
                return;
            var next = new Dictionary<int, (CellRect Rect, string Key)[]>(watched);
            next.Remove(mapUniqueId);
            watched = next;
        }
    }

    public static void ClearStagedMap(int mapUniqueId)
    {
        lock (Gate)
            StagedMaps.Remove(mapUniqueId);
    }

    public static void RegisterMap(FamilyState s, Map map, int tick)
    {
        foreach (var (_, key) in CaptureSealedEntries(map))
            CaravanFamily.ShrineSealed(s, key, tick);
    }

    /// <summary>At the start of recording (a new game or a load): every map's sealed shrines, and the watched ones found open.</summary>
    public static void Startup(FamilyState s, int tick)
    {
        foreach (var map in Find.Maps)
            RegisterMap(s, map, tick);
        var maps = Find.Maps.ToDictionary(GameFacts.MapId, m => m, StringComparer.Ordinal);
        foreach (var key in CaravanFamily.WatchedShrines(s))
            if (CaravanFamily.TryParseShrineKey(key, out var mapId, out var minX, out var minZ, out var maxX, out var maxZ))
            {
                if (!maps.TryGetValue(mapId, out var map))
                    CaravanFamily.ShrinesOnMapGone(s, mapId);
                else if (!Sealed(map, CellRect.FromLimits(minX, minZ, maxX, maxZ)))
                    CaravanFamily.ShrineFoundOpen(s, key);
            }
        Rebuild(s);
    }

    /// <summary>The lookup of watched shrines by map, from the family's own state (preserving pending async-staged maps).</summary>
    public static void Rebuild(FamilyState? s)
    {
        if (s is null)
        {
            Reset();
            return;
        }
        var staging = new Dictionary<int, List<(CellRect, string)>>();
        foreach (var key in CaravanFamily.WatchedShrines(s))
        {
            if (!CaravanFamily.TryParseShrineKey(key, out var mapId, out var minX, out var minZ, out var maxX, out var maxZ)
                || !mapId.StartsWith("Map_", StringComparison.Ordinal)
                || !int.TryParse(mapId.AsSpan(4), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var uid))
                continue;
            if (!staging.TryGetValue(uid, out var list))
                staging[uid] = list = new List<(CellRect, string)>();
            list.Add((CellRect.FromLimits(minX, minZ, maxX, maxZ), key));
        }
        var next = staging.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
        lock (Gate)
        {
            foreach (var (uid, entries) in StagedMaps)
            {
                if (entries.Length == 0)
                    next.Remove(uid);
                else if (!next.ContainsKey(uid))
                    next[uid] = entries;
            }
            watched = next;
        }
    }

    /// <summary>After <c>FogGrid.Unfog(c)</c>: a watched shrine around <paramref name="c"/> whose centre is clear now was opened.</summary>
    public static void Unfogged(Map map, IntVec3 c)
    {
        var snapshot = watched;
        if (!snapshot.TryGetValue(map.uniqueID, out var list) || !LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        List<string>? opened = null;
        foreach (var (rect, key) in list)
            if (rect.Contains(c) && !map.fogGrid.IsFogged(rect.CenterCell))
                (opened ??= new List<string>()).Add(key);
        if (opened is null)
            return;
        lock (Gate)
        {
            if (watched.TryGetValue(map.uniqueID, out var currentList))
            {
                var remainingList = new List<(CellRect Rect, string Key)>(currentList.Length);
                for (var i = 0; i < currentList.Length; i++)
                    if (!opened.Contains(currentList[i].Key))
                        remainingList.Add(currentList[i]);
                var next = new Dictionary<int, (CellRect Rect, string Key)[]>(watched);
                if (remainingList.Count == 0)
                {
                    next.Remove(map.uniqueID);
                    if (StagedMaps.ContainsKey(map.uniqueID))
                        StagedMaps[map.uniqueID] = Array.Empty<(CellRect, string)>();
                }
                else
                {
                    var remArray = remainingList.ToArray();
                    next[map.uniqueID] = remArray;
                    if (StagedMaps.ContainsKey(map.uniqueID))
                        StagedMaps[map.uniqueID] = remArray;
                }
                watched = next;
            }
        }
        LiveMetrics.RecordOrDefer(CaravanFamily.Name, s =>
        {
            foreach (var key in opened)
                CaravanFamily.ShrineUnsealed(s, key);
            Rebuild(s);
        });
    }

    public static void Reset()
    {
        lock (Gate)
        {
            watched = new Dictionary<int, (CellRect Rect, string Key)[]>();
            StagedMaps.Clear();
        }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(FogGrid), nameof(FogGrid.Unfog))]
public static class ShrineUnfogMetricPatch
{
    public static void Postfix(Map ___map, IntVec3 c)
    {
        if (ShrineProbe.Empty || ___map == null || !LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            ShrineProbe.Unfogged(___map, c);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(CaravanFamily.Name, e);
            ShrineProbe.Reset();
        }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(PawnTrigger), nameof(PawnTrigger.ActivatedBy))]
public static class ShrineApproachMetricPatch
{
    public static void Prefix(PawnTrigger __instance)
    {
        if (!LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            if (__instance is not RectTrigger trigger || !ShrineProbe.IsShrineTrigger(trigger) || !trigger.Spawned
                || !LiveMetrics.CanCaptureEvent(CaravanFamily.Name))
                return;
            var tick = GameFacts.Tick;
            GameFacts.Record(CaravanFamily.Name, s =>
            {
                ShrineProbe.Register(s, trigger, tick);
                ShrineProbe.Rebuild(s);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

/// <summary>A map generated during play (a new settlement) may carry shrines: they are registered once its fog is in place.</summary>
[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
public static class ShrineMapGeneratedMetricPatch
{
    public static void Postfix(Map __result)
    {
        if (__result is null || !LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            var entries = ShrineProbe.CaptureSealedEntries(__result);
            var mapUniqueId = __result.uniqueID;
            ShrineProbe.StageMapEntries(mapUniqueId, entries);
            var tick = GameFacts.Tick;
            LiveMetrics.RecordOrDefer(CaravanFamily.Name, s =>
            {
                foreach (var (_, key) in entries)
                    CaravanFamily.ShrineSealed(s, key, tick);
                ShrineProbe.ClearStagedMap(mapUniqueId);
                ShrineProbe.Rebuild(s);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap))]
public static class ShrineMapRemovedMetricPatch
{
    public static void Prefix(Map map)
    {
        if (map == null || !LiveMetrics.IsRecordingUnit(CaravanFamily.Name))
            return;
        try
        {
            ShrineProbe.RemoveStagedMap(map.uniqueID);
            var id = GameFacts.MapId(map);
            LiveMetrics.RecordOrDefer(CaravanFamily.Name, s =>
            {
                CaravanFamily.ShrinesOnMapGone(s, id);
                ShrineProbe.Rebuild(s);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(CaravanFamily.Name, e); }
    }
}
