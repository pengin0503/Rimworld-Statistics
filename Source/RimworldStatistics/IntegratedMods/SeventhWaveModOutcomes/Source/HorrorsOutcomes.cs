using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.SeventhWaveModOutcomes;

/// <summary>
/// Metrics for Horrors (Continued) (Mlie.Horrors, 1.7/Assemblies/Horrors.dll):
/// 1. Hives and dens destroyed: observes HorrorHive.DeSpawn(DestroyMode) and
///    HorrorDen.DeSpawn(DestroyMode) when DestroyMode == DestroyMode.KillFinalize
///    on a map with colony presence (non-player-owned ThingWithComps structures,
///    so zero overlap with pawn kill metrics).
/// 2. Home surface maps in Horrors biomes (HorrorWastes): SaveSnapshot.
/// </summary>
internal static class HorrorsOutcomes
{
    private const string AssemblyName = "Horrors";
    private const string HivesDestroyedUnit = "mod.mlie.horrors.hives_destroyed";

    private sealed record HiveDeSpawnSnapshot(ThingWithComps Hive, DestroyMode Mode, bool HasColonyPresence);

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            HorrorsRules.OwnerPackageId,
            "hives_destroyed",
            "RWS_HorrorsHivesDestroyed",
            "RWS_HorrorsHivesDestroyedDesc",
            MetricCategory.Combat,
            InstallHivesDestroyed,
            breakdown: true);

        yield return new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.horrors_biomes",
            OwnerPackageId = HorrorsRules.OwnerPackageId,
            FamilyId = "biomes",
            MetricName = "home_maps",
            Revision = 1,
            Category = MetricCategory.Colony,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.SaveSnapshot,
            Overall = OverallAggregation.Max,
            HasBreakdown = true,
            Selection = ColonySelection.ObservationMaxByBreakdown,
            RequiredPackages = [new ModPackageRequirement(HorrorsRules.OwnerPackageId)],
            LabelKey = "RWS_Horrors_HomeMaps",
            DescriptionKey = "RWS_Horrors_HomeMapsDesc",
        }, CaptureSnapshot: CaptureBiomes);
    }

    private static MetricEntry? CaptureBiomes() =>
        Find.Maps is null
            ? null
            : BiomesIslandsRules.HomeBiomesForPackage(
                HorrorsRules.OwnerPackageId,
                BiomesIslandsOutcomes.EnumerateHomeSurfaceMaps());

    private static void InstallHivesDestroyed(Harmony harmony)
    {
        var hiveType = OutcomeMetric.Bind("Horrors.HorrorHive", AssemblyName);
        var denType = OutcomeMetric.Bind("Horrors.HorrorDen", AssemblyName);
        if (!typeof(ThingWithComps).IsAssignableFrom(hiveType) || !typeof(ThingWithComps).IsAssignableFrom(denType))
            throw new InvalidOperationException("Horrors HorrorHive or HorrorDen no longer derives from ThingWithComps");

        var hiveDeSpawn = OutcomeMetric.Method(hiveType, "DeSpawn", typeof(void), typeof(DestroyMode));
        var denDeSpawn = OutcomeMetric.Method(denType, "DeSpawn", typeof(void), typeof(DestroyMode));

        OutcomeMetric.Patch(harmony, hiveDeSpawn, typeof(HorrorsOutcomes), nameof(BeforeHiveDeSpawn), nameof(AfterHiveDeSpawn));
        OutcomeMetric.Patch(harmony, denDeSpawn, typeof(HorrorsOutcomes), nameof(BeforeHiveDeSpawn), nameof(AfterHiveDeSpawn));
    }

    private static bool HasColonyPresence(Map map)
    {
        if (map.IsPlayerHome)
            return true;
        var mapPawns = map.mapPawns;
        if (mapPawns is null)
            return false;
        if (mapPawns.AnyColonistSpawned || mapPawns.SlavesOfColonySpawned is { Count: > 0 })
            return true;
        return GameFacts.PlayerFaction is { } playerFaction &&
               mapPawns.SpawnedPawnsInFaction(playerFaction) is { Count: > 0 };
    }

    private static void BeforeHiveDeSpawn(ThingWithComps __instance, DestroyMode __0, out HiveDeSpawnSnapshot? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(HivesDestroyedUnit) ||
            __0 != DestroyMode.KillFinalize ||
            __instance is not { Spawned: true, Map: { } map } ||
            GameFacts.IsPlayerFaction(__instance.Faction))
            return;

        try
        {
            var hasColonyPresence = HasColonyPresence(map);
            if (hasColonyPresence)
                __state = new HiveDeSpawnSnapshot(__instance, __0, hasColonyPresence);
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(HivesDestroyedUnit, error);
        }
    }

    private static void AfterHiveDeSpawn(bool __runOriginal, HiveDeSpawnSnapshot? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(HivesDestroyedUnit))
            return;

        try
        {
            var key = HorrorsRules.ResolveDestroyedHiveKey(
                isKillFinalize: __state.Mode == DestroyMode.KillFinalize,
                wasSpawnedBefore: true,
                isSpawnedAfter: __state.Hive.Spawned,
                isPlayerFactionHive: GameFacts.IsPlayerFaction(__state.Hive.Faction),
                hasColonyPresenceOnMap: __state.HasColonyPresence,
                hivePackageId: __state.Hive.def?.modContentPack?.PackageId,
                hiveDefName: __state.Hive.def?.defName);
            if (key is not null)
            {
                GameFacts.Record(HivesDestroyedUnit, state => OptionalTransitionRecording.RecordCount(
                    state, "completed", 1, key: key, withBreakdown: true));
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(HivesDestroyedUnit, error);
        }
    }
}
