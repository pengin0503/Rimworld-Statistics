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

    public static int PlayerCaravans() => Find.WorldObjects.Caravans.Count(c => c.IsPlayerControlled);
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(CaravanExitMapUtility), nameof(CaravanExitMapUtility.ExitMapAndCreateCaravan),
    new[] { typeof(IEnumerable<Pawn>), typeof(Faction), typeof(PlanetTile), typeof(PlanetTile), typeof(PlanetTile), typeof(bool) })]
public static class CaravanDepartMetricPatch
{
    public static void Postfix(Caravan __result)
    {
        if (__result is not { IsPlayerControlled: true })
            return;
        var id = CaravanProbe.Id(__result);
        var tick = GameFacts.Tick;
        GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Departed(s, id, tick));
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(Caravan_PathFollower), "TryEnterNextPathTile")]
public static class CaravanStepMetricPatch
{
    public static void Prefix(Caravan ___caravan, out PlanetTile __state) => __state = ___caravan?.Tile ?? PlanetTile.Invalid;

    public static void Postfix(Caravan ___caravan, PlanetTile __state)
    {
        if (___caravan is not { IsPlayerControlled: true } || !__state.Valid || ___caravan.Tile == __state)
            return;
        var now = ___caravan.Tile;
        var adjacent = Find.WorldGrid.IsNeighbor(__state, now);
        var id = CaravanProbe.Id(___caravan);
        var tile = ColonyIdentity.NormalizeTile(now.ToString()) ?? now.ToString();
        GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Moved(s, id, tile, adjacent));
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(CaravanEnterMapUtility), nameof(CaravanEnterMapUtility.Enter),
    new[] { typeof(Caravan), typeof(Map), typeof(Func<Pawn, IntVec3>), typeof(CaravanDropInventoryMode), typeof(bool) })]
public static class CaravanReturnMetricPatch
{
    public static void Prefix(Caravan caravan, Map map)
    {
        if (caravan is not { IsPlayerControlled: true } || map is not { IsPlayerHome: true })
            return;
        var id = CaravanProbe.Id(caravan);
        var tick = GameFacts.Tick;
        GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Returned(s, id, tick));
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(Caravan), nameof(Caravan.Notify_MemberDied))]
public static class CaravanMemberDiedMetricPatch
{
    public static void Postfix(Caravan __instance)
    {
        if (!__instance.IsPlayerControlled || __instance.PawnsListForReading.Any(p => !p.Dead))
            return;
        var id = CaravanProbe.Id(__instance);
        GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Lost(s, id));
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Add))]
public static class CaravanAddedMetricPatch
{
    public static void Postfix(WorldObject o)
    {
        if (o is not Caravan { IsPlayerControlled: true })
            return;
        var n = CaravanProbe.PlayerCaravans();
        GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Concurrent(s, n));
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(WorldObjectsHolder), nameof(WorldObjectsHolder.Remove))]
public static class CaravanRemovedMetricPatch
{
    public static void Prefix(WorldObject o)
    {
        if (o is not Caravan { IsPlayerControlled: true } c)
            return;
        var id = CaravanProbe.Id(c);
        GameFacts.Record(CaravanFamily.Name, s => CaravanFamily.Aborted(s, id));   // a return or a loss already closed it
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
        if (__result && __instance?.def != null && Ambushes.Contains(__instance.def.defName) && parms?.target is Caravan { IsPlayerControlled: true })
            GameFacts.Record(CaravanFamily.Name, CaravanFamily.Ambushed);
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(SettlementDefeatUtility), nameof(SettlementDefeatUtility.CheckDefeated))]
public static class SettlementDefeatedMetricPatch
{
    public static void Prefix(Settlement factionBase, out bool __state)
        => __state = factionBase is { Destroyed: false } && factionBase.Faction != null && Faction.OfPlayer != null && factionBase.Faction.HostileTo(Faction.OfPlayer);

    public static void Postfix(Settlement factionBase, bool __state)
    {
        if (__state && factionBase.Destroyed)
            GameFacts.Record(CaravanFamily.Name, CaravanFamily.SiteCleared);
    }
}

/// <summary>Voluntary abandonment: the dialog's Abandon, which passes wasGravshipLaunch: false (1.7.4985).</summary>
[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(SettlementAbandonUtility), "Abandon")]
public static class AbandonMetricPatch
{
    public static void Postfix(MapParent settlement)
    {
        if (settlement != null)
            GameFacts.Record(CaravanFamily.Name, CaravanFamily.Abandoned);
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(SettleInEmptyTileUtility), nameof(SettleInEmptyTileUtility.Settle))]
public static class SettleEmptyMetricPatch
{
    public static void Postfix() => GameFacts.Record(CaravanFamily.Name, CaravanFamily.Founded);
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(SettleInExistingMapUtility), nameof(SettleInExistingMapUtility.Settle))]
public static class SettleExistingMetricPatch
{
    public static void Postfix() => GameFacts.Record(CaravanFamily.Name, CaravanFamily.Founded);
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

    private static readonly Dictionary<int, List<(CellRect Rect, string Key)>> Watched = new();

    public static bool Empty => Watched.Count == 0;

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

    public static void RegisterMap(FamilyState s, Map map, int tick)
    {
        foreach (var t in map.listerThings.ThingsOfDef(ThingDefOf.RectTrigger))
            if (IsShrineTrigger(t))
                Register(s, (RectTrigger)t, tick);
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

    /// <summary>The lookup of watched shrines by map, from the family's own state.</summary>
    public static void Rebuild(FamilyState? s)
    {
        Watched.Clear();
        if (s is null)
            return;
        foreach (var key in CaravanFamily.WatchedShrines(s))
        {
            if (!CaravanFamily.TryParseShrineKey(key, out var mapId, out var minX, out var minZ, out var maxX, out var maxZ)
                || !mapId.StartsWith("Map_", StringComparison.Ordinal)
                || !int.TryParse(mapId.AsSpan(4), System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var uid))
                continue;
            if (!Watched.TryGetValue(uid, out var list))
                Watched[uid] = list = new List<(CellRect, string)>();
            list.Add((CellRect.FromLimits(minX, minZ, maxX, maxZ), key));
        }
    }

    /// <summary>After <c>FogGrid.Unfog(c)</c>: a watched shrine around <paramref name="c"/> whose centre is clear now was opened.</summary>
    public static void Unfogged(Map map, IntVec3 c)
    {
        if (!Watched.TryGetValue(map.uniqueID, out var list))
            return;
        List<string>? opened = null;
        foreach (var (rect, key) in list)
            if (rect.Contains(c) && !map.fogGrid.IsFogged(rect.CenterCell))
                (opened ??= new List<string>()).Add(key);
        if (opened is null)
            return;
        GameFacts.Record(CaravanFamily.Name, s =>
        {
            foreach (var key in opened)
                CaravanFamily.ShrineUnsealed(s, key);
        });
        Rebuild(LiveMetrics.Family(CaravanFamily.Name));
    }

    public static void Reset() => Watched.Clear();
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(FogGrid), nameof(FogGrid.Unfog))]
public static class ShrineUnfogMetricPatch
{
    public static void Postfix(Map ___map, IntVec3 c)
    {
        if (ShrineProbe.Empty || ___map == null)
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
        if (__instance is not RectTrigger trigger || !ShrineProbe.IsShrineTrigger(trigger) || !trigger.Spawned)
            return;
        var tick = GameFacts.Tick;
        GameFacts.Record(CaravanFamily.Name, s =>
        {
            ShrineProbe.Register(s, trigger, tick);
            ShrineProbe.Rebuild(s);
        });
    }
}

/// <summary>A map generated during play (a new settlement) may carry shrines: they are registered once its fog is in place.</summary>
[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
public static class ShrineMapGeneratedMetricPatch
{
    public static void Postfix(Map __result)
    {
        if (__result is null)
            return;
        var tick = GameFacts.Tick;
        GameFacts.Record(CaravanFamily.Name, s =>
        {
            ShrineProbe.RegisterMap(s, __result, tick);
            ShrineProbe.Rebuild(s);
        });
    }
}

[MetricAdapter(CaravanFamily.Name)]
[HarmonyPatch(typeof(Game), nameof(Game.DeinitAndRemoveMap))]
public static class ShrineMapRemovedMetricPatch
{
    public static void Prefix(Map map)
    {
        if (map == null)
            return;
        var id = GameFacts.MapId(map);
        GameFacts.Record(CaravanFamily.Name, s =>
        {
            CaravanFamily.ShrinesOnMapGone(s, id);
            ShrineProbe.Rebuild(s);
        });
    }
}
