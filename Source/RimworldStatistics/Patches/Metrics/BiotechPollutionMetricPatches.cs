using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// PollutionGrid.SetPolluted is used by many non-player sources. A scoped
/// confirmation from the actual player cleanup JobDriver is required, so weather,
/// map regeneration and other factions never count as player cleanup.
/// </summary>
internal static class BiotechCleanupScope
{
    [ThreadStatic] private static PollutionGrid? activeGrid;
    [ThreadStatic] private static int depth;
    [ThreadStatic] private static int mapId;

    public static PollutionGrid? Current => depth > 0 ? activeGrid : null;
    public static int MapId => mapId;

    public static void Enter(PollutionGrid grid, int ownerMapId)
    {
        activeGrid = grid;
        mapId = ownerMapId;
        depth++;
    }

    public static void Exit()
    {
        if (depth > 0 && --depth == 0)
            activeGrid = null;
    }
}

[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(JobDriver_ClearPollution), "ClearPollutionAt", new[] { typeof(IntVec3), typeof(Map) })]
public static class BiotechPlayerCleanupJobMetricPatch
{
    public static void Prefix(JobDriver_ClearPollution __instance, Map map, out bool __state)
    {
        __state = LiveMetrics.Family(BiotechFamily.Name) is not null
            && __instance.pawn?.Faction == Faction.OfPlayer && map?.pollutionGrid is not null;
        if (__state)
            BiotechCleanupScope.Enter(map.pollutionGrid, map.uniqueID);
    }

    public static Exception? Finalizer(bool __state, Exception? __exception)
    {
        if (__state)
            BiotechCleanupScope.Exit();
        return __exception;
    }
}

[MetricAdapter(BiotechFamily.Name)]
[HarmonyPatch(typeof(PollutionGrid), nameof(PollutionGrid.SetPolluted))]
public static class BiotechPollutionCellClearedMetricPatch
{
    public static void Prefix(PollutionGrid __instance, IntVec3 cell, bool isPolluted, out bool __state)
        => __state = !isPolluted && ReferenceEquals(BiotechCleanupScope.Current, __instance)
            && __instance.IsPolluted(cell);

    public static void Postfix(PollutionGrid __instance, IntVec3 cell, bool isPolluted, bool __state)
    {
        if (!__state || isPolluted || __instance.IsPolluted(cell))
            return;
        var id = GameFacts.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "/map/" + BiotechCleanupScope.MapId.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "/cell/" + cell.ToString();
        GameFacts.Record(BiotechFamily.Name, s =>
            BiotechFamily.PollutionCellCleaned(s, id, playerCleanup: true,
                pollutedBefore: true, pollutedAfter: false, mapCell: true));
    }
}
