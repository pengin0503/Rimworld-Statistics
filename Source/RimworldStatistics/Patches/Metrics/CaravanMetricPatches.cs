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
