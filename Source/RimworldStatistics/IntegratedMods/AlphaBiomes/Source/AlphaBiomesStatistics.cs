using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.AlphaBiomes;

/// <summary>
/// Alpha Biomes: (1) records a SaveSnapshot of active player home maps located
/// in Alpha Biomes BiomeDefs, and (2) counts stone chunks genuinely placed by
/// player-owned AlphaBiomes.CompCoreSampleDrill.TryProducePortion.
/// </summary>
internal static class AlphaBiomesStatistics
{
    private const string DrillUnit = "mod.sarg.alphabiomes.core_drill";
    private const string DrillKey = DrillUnit + ".completed";

    [ThreadStatic] private static DrillScope? activeDrill;

    private sealed class DrillScope
    {
        internal required Thing Building { get; init; }
        internal required DrillScope? Parent { get; init; }
        internal readonly Dictionary<ThingDef, long> Produced = new();
    }

    private static readonly LiveFamily DrillFamily = new()
    {
        Name = DrillUnit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "completed", DrillKey, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.alpha_biomes",
            OwnerPackageId = AlphaBiomesRules.OwnerPackageId,
            FamilyId = "biomes",
            MetricName = "home_maps",
            Revision = 1,
            Category = MetricCategory.Colony,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.SaveSnapshot,
            Overall = OverallAggregation.Max,
            HasBreakdown = true,
            Selection = ColonySelection.ObservationMaxByBreakdown,
            RequiredPackages =
            [
                new ModPackageRequirement(AlphaBiomesRules.OwnerPackageId),
                new ModPackageRequirement(AlphaBiomesRules.FrameworkPackageId),
            ],
            LabelKey = "RWS_AlphaBiomes_HomeMaps",
            DescriptionKey = "RWS_AlphaBiomes_HomeMapsDesc",
        }, CaptureSnapshot: CaptureBiomes),
        new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.alpha_biomes_drill",
            OwnerPackageId = AlphaBiomesRules.OwnerPackageId,
            FamilyId = "core_drill",
            MetricName = "completed",
            Revision = 1,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.Checkpoint,
            Overall = OverallAggregation.Sum,
            HasBreakdown = true,
            Selection = ColonySelection.Representative,
            RequiredPackages =
            [
                new ModPackageRequirement(AlphaBiomesRules.OwnerPackageId),
                new ModPackageRequirement(AlphaBiomesRules.FrameworkPackageId),
            ],
            LabelKey = "RWS_AlphaBiomes_CoreDrill",
            DescriptionKey = "RWS_AlphaBiomes_CoreDrillDesc",
        }, DrillFamily, InstallDrill),
    ];

    private static MetricEntry? CaptureBiomes()
    {
        var maps = Find.Maps;
        if (maps is null)
            return null;

        return AlphaBiomesRules.HomeBiomes(maps
            .Where(map => map is { IsPlayerHome: true, IsPocketMap: false } &&
                          map.Tile.Valid &&
                          map.Tile.Layer is { IsRootSurface: true })
            .Select(map => (map.uniqueID, map.Biome?.modContentPack?.PackageId, map.Biome?.defName)));
    }

    private static void InstallDrill(Harmony harmony)
    {
        var drill = OutcomeMetric.Bind("AlphaBiomes.CompCoreSampleDrill", "AlphaBiomes");
        if (!typeof(ThingComp).IsAssignableFrom(drill))
            throw new InvalidOperationException("AlphaBiomes.CompCoreSampleDrill type changed");

        var produce = OutcomeMetric.Method(drill, "TryProducePortion", typeof(void), typeof(float));
        var place = AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)])
            ?? AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>)]);
        if (place?.ReturnType != typeof(bool))
            throw new InvalidOperationException("GenPlace.TryPlaceThing signature changed");

        try
        {
            harmony.Patch(produce,
                prefix: new HarmonyMethod(typeof(AlphaBiomesStatistics), nameof(BeforeProduce)),
                postfix: new HarmonyMethod(typeof(AlphaBiomesStatistics), nameof(AfterProduce)),
                finalizer: new HarmonyMethod(typeof(AlphaBiomesStatistics), nameof(FinishProduce)));
            harmony.Patch(place,
                postfix: new HarmonyMethod(typeof(AlphaBiomesStatistics), nameof(AfterPlace)));
        }
        catch
        {
            harmony.Unpatch(produce, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(place, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static bool CanObserveDrill()
        => MetricGates.IsRecording(DrillUnit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeProduce(ThingComp __instance, out DrillScope? __state)
    {
        __state = null;
        if (!CanObserveDrill())
            return;

        try
        {
            if (__instance.parent is not { Spawned: true } building ||
                Faction.OfPlayerSilentFail is not { } player ||
                building.Faction != player)
                return;

            var scope = new DrillScope { Building = building, Parent = activeDrill };
            activeDrill = scope;
            __state = scope;
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(DrillUnit, e);
        }
    }

    private static void AfterPlace(Thing __0, bool __result)
    {
        if (!__result || activeDrill is not { } scope || __0?.def is null)
            return;

        try
        {
            var count = Math.Max(1, __0.stackCount);
            scope.Produced[__0.def] = scope.Produced.GetValueOrDefault(__0.def) + count;
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(DrillUnit, e);
        }
    }

    private static void AfterProduce(bool __runOriginal, DrillScope? __state)
    {
        if (!__runOriginal || __state is null || __state.Produced.Count == 0 || !CanObserveDrill())
            return;

        try
        {
            foreach (var (def, count) in __state.Produced)
            {
                if (count <= 0)
                    continue;
                var key = OptionalTransitionRecording.QualifiedDefKey(def.modContentPack?.PackageId, def.defName);
                GameFacts.Record(DrillUnit, state =>
                    OptionalTransitionRecording.RecordCount(state, "completed", count, key: key, withBreakdown: true));
            }
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(DrillUnit, e);
        }
    }

    private static void FinishProduce(DrillScope? __state)
    {
        if (__state is not null && ReferenceEquals(activeDrill, __state))
            activeDrill = __state.Parent;
    }
}
