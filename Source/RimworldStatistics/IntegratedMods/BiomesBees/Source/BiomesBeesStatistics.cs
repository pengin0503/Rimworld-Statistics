using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.BiomesBees;

/// <summary>
/// Biomes! Bees: counts confirmed bee pollination events when
/// BiomesBees.JobDriver_Pollinate advances growth on a player-relevant plant.
/// Already-mature plants and cancelled/interrupted jobs are excluded.
/// </summary>
internal static class BiomesBeesStatistics
{
    private const string AssemblyName = "BiomesBees";
    private const string Unit = "mod.biomesteam.biomesbees.pollinations";

    private static MethodInfo? plantGetter;

    private sealed record PollinationAttempt(Plant Plant, float BeforeGrowth, bool PlayerRelevant);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            owner: BiomesBeesRules.OwnerPackageId,
            family: "pollinations",
            label: "RWS_BiomesBees_Pollinations",
            description: "RWS_BiomesBees_PollinationsDesc",
            category: MetricCategory.Work,
            install: Install,
            breakdown: true,
            dependencies: BiomesBeesRules.FrameworkPackageId),
    ];

    private static void Install(Harmony harmony)
    {
        var driverType = OutcomeMetric.Bind("BiomesBees.JobDriver_Pollinate", AssemblyName);
        if (!typeof(JobDriver).IsAssignableFrom(driverType))
            throw new InvalidOperationException("BiomesBees.JobDriver_Pollinate type changed");

        var getter = AccessTools.PropertyGetter(driverType, "Plant");
        if (getter is null || !typeof(Thing).IsAssignableFrom(getter.ReturnType))
            throw new InvalidOperationException("BiomesBees.JobDriver_Pollinate.Plant property changed");

        var apply = OutcomeMetric.Method(driverType, "<MakeNewToils>b__5_0", typeof(void));

        plantGetter = getter;
        try
        {
            OutcomeMetric.Patch(harmony, apply, typeof(BiomesBeesStatistics), nameof(Before), nameof(After));
        }
        catch
        {
            plantGetter = null;
            throw;
        }
    }

    private static void Before(JobDriver? __instance, out PollinationAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Unit) || __instance is null || plantGetter is null)
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is null)
                return;

            if (plantGetter.Invoke(__instance, null) is not Plant plant || !plant.Spawned)
                return;

            var playerRelevant = plant.Map?.IsPlayerHome == true ||
                                 plant.Faction == player ||
                                 __instance.pawn?.Faction == player;
            if (!playerRelevant)
                return;

            __state = new PollinationAttempt(plant, plant.Growth, true);
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(Unit, e);
        }
    }

    private static void After(bool __runOriginal, PollinationAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(Unit))
            return;

        try
        {
            var def = __state.Plant.def;
            var key = BiomesBeesRules.ResolvePollinationBreakdown(
                __state.PlayerRelevant,
                __state.BeforeGrowth,
                __state.Plant.Growth,
                def?.modContentPack?.PackageId,
                def?.defName);

            if (key is not null)
            {
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
        }
    }
}
