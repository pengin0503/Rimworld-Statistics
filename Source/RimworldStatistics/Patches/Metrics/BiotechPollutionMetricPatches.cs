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
    // The cleanup job may re-enter the clearance helper. A simple depth count
    // leaves the INNER grid active after the inner call returns; preserve each
    // scope's grid/map pair so an outer cleanup cannot be attributed to another map.
    [ThreadStatic] private static Stack<(PollutionGrid Grid, int MapId)>? scopes;

    public static PollutionGrid? Current
        => scopes is { Count: > 0 } ? scopes.Peek().Grid : null;

    public static int MapId
        => scopes is { Count: > 0 } ? scopes.Peek().MapId : -1;

    public static void Enter(PollutionGrid grid, int ownerMapId)
        => (scopes ??= new()).Push((grid, ownerMapId));

    public static void Exit()
    {
        if (scopes is not { Count: > 0 })
            return;
        scopes.Pop();
        if (scopes.Count == 0)
            scopes = null;
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
        if (__state && map?.pollutionGrid is { } grid)
            BiotechCleanupScope.Enter(grid, map.uniqueID);
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
