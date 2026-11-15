using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VreRecycling;

/// <summary>
/// Vanilla Recycling Expanded: counts actual items placed or merged into output
/// stacks by CompSuperSimpleProcessor.DoAtomize (garbage compactor &amp; alloypack
/// splitter), broken down by output ThingDef. Input removal or blocked cells yield 0.
/// </summary>
internal static class VreRecyclingStatistics
{
    private const string Owner = "VanillaExpanded.Recycling";
    private const string Unit = "mod.vanillaexpanded.recycling.processed_items";
    private const string Key = Unit + ".completed";

    [ThreadStatic] private static AtomizeScope? activeAtomize;
    [ThreadStatic] private static SpawnCellScope? activeSpawn;

    private sealed class AtomizeScope
    {
        internal required Building Building { get; init; }
        internal required Map Map { get; init; }
        internal required AtomizeScope? Parent { get; init; }
        internal readonly Dictionary<ThingDef, long> Produced = new();
    }

    private sealed class SpawnCellScope
    {
        internal required AtomizeScope Owner { get; init; }
        internal required ThingDef ResultDef { get; init; }
        internal Thing? ExistingBefore;
        internal int ExistingBeforeCount;
        internal bool TryPlaceCalled;
        internal long PlacedActionCount;
        internal SpawnCellScope? Parent;
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        RequiredDlc = AcquisitionUnits.Biotech,
        Build = output => OptionalTransitionRecording.PutCount(output, "completed", Key, withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.vre_recycling",
            OwnerPackageId = Owner,
            FamilyId = "processed_items",
            MetricName = "completed",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredDlc = AcquisitionUnits.Biotech,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VreRecycling_ProcessedItems",
            DescriptionKey = "RWS_VreRecycling_ProcessedItemsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var proc = AccessTools.TypeByName("VanillaRecyclingExpanded.CompSuperSimpleProcessor");
        if (proc is null || !typeof(ThingComp).IsAssignableFrom(proc) ||
            !string.Equals(proc.Assembly.GetName().Name, "VanillaRecyclingExpanded", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected 1.7 VanillaRecyclingExpanded assembly not available");

        var doAtomize = AccessTools.DeclaredMethod(proc, "DoAtomize", Type.EmptyTypes);
        var trySpawn = AccessTools.DeclaredMethod(proc, "TrySpawnAtCell",
            [typeof(IntVec3), typeof(ThingDef), typeof(int)]);
        var ground = AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)])
            ?? AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>)]);
        if (doAtomize?.ReturnType != typeof(void) ||
            trySpawn?.ReturnType != typeof(bool) ||
            ground?.ReturnType != typeof(bool))
            throw new InvalidOperationException("VanillaRecyclingExpanded CompSuperSimpleProcessor signature changed");

        try
        {
            harmony.Patch(doAtomize,
                prefix: new HarmonyMethod(typeof(VreRecyclingStatistics), nameof(BeforeAtomize)),
                postfix: new HarmonyMethod(typeof(VreRecyclingStatistics), nameof(AfterAtomize)),
                finalizer: new HarmonyMethod(typeof(VreRecyclingStatistics), nameof(FinishAtomize)));
            harmony.Patch(trySpawn,
                prefix: new HarmonyMethod(typeof(VreRecyclingStatistics), nameof(BeforeTrySpawn)),
                postfix: new HarmonyMethod(typeof(VreRecyclingStatistics), nameof(AfterTrySpawn)),
                finalizer: new HarmonyMethod(typeof(VreRecyclingStatistics), nameof(FinishTrySpawn)));
            harmony.Patch(ground,
                prefix: new HarmonyMethod(typeof(VreRecyclingStatistics), nameof(BeforeGroundPlace)));
        }
        catch
        {
            harmony.Unpatch(doAtomize, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(trySpawn, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(ground, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeAtomize(object __instance, out AtomizeScope? __state)
    {
        __state = null;
        if (!CanObserve() ||
            __instance is not ThingComp { parent: Building { Spawned: true } building } ||
            building.Faction != Faction.OfPlayer ||
            building.Map is not { } map ||
            !string.Equals(building.def?.modContentPack?.PackageId, Owner, StringComparison.OrdinalIgnoreCase))
            return;

        __state = new AtomizeScope
        {
            Building = building,
            Map = map,
            Parent = activeAtomize,
        };
        activeAtomize = __state;
    }

    private static void BeforeTrySpawn(object __instance, IntVec3 __0, ThingDef? __1, int __2, out SpawnCellScope? __state)
    {
        __state = null;
        if (activeAtomize is not { } owner || __1 is null || __2 <= 0 || !CanObserve() ||
            __instance is not ThingComp { parent: { } parent } ||
            !ReferenceEquals(parent, owner.Building))
            return;

        try
        {
            var trueOutputCell = __0 != IntVec3.Invalid
                ? owner.Building.Position + __0.RotatedBy(owner.Building.Rotation)
                : owner.Building.InteractionCell;
            if (!trueOutputCell.Walkable(owner.Map))
                return;

            var existing = trueOutputCell.GetFirstThing(owner.Map, __1);
            __state = new SpawnCellScope
            {
                Owner = owner,
                ResultDef = __1,
                ExistingBefore = existing,
                ExistingBeforeCount = existing?.stackCount ?? 0,
                Parent = activeSpawn,
            };
            activeSpawn = __state;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforeGroundPlace(Thing __0, Map __2, ref Action<Thing, int>? __4)
    {
        if (activeSpawn is not { } scope || !CanObserve() ||
            !ReferenceEquals(__2, scope.Owner.Map) ||
            __0?.def is null || !ReferenceEquals(__0.def, scope.ResultDef))
            return;

        scope.TryPlaceCalled = true;
        var original = __4;
        __4 = (placedThing, placedCount) =>
        {
            original?.Invoke(placedThing, placedCount);
            try
            {
                if (ReferenceEquals(scope, activeSpawn) && placedCount > 0)
                    scope.PlacedActionCount = checked(scope.PlacedActionCount + placedCount);
            }
            catch (Exception e) { LiveMetrics.Fail(Unit, e); }
        };
    }

    private static void AfterTrySpawn(bool __runOriginal, SpawnCellScope? __state)
    {
        if (__state is null || !__runOriginal || !ReferenceEquals(activeSpawn, __state) || !CanObserve())
            return;

        try
        {
            var confirmed = VreRecyclingRules.ConfirmedOutputCount(
                spawned: __state.Owner.Building.Spawned,
                playerOwned: __state.Owner.Building.Faction == Faction.OfPlayer,
                tryPlaceCalled: __state.TryPlaceCalled,
                placedActionCount: __state.PlacedActionCount,
                existingBeforeCount: __state.ExistingBeforeCount,
                existingAfterCount: __state.ExistingBefore?.stackCount ?? 0,
                existingDestroyed: __state.ExistingBefore?.Destroyed ?? true);
            if (confirmed <= 0)
                return;

            __state.Owner.Produced.TryGetValue(__state.ResultDef, out var prior);
            __state.Owner.Produced[__state.ResultDef] = checked(prior + confirmed);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishTrySpawn(Exception? __exception, SpawnCellScope? __state)
    {
        if (__state is not null && ReferenceEquals(activeSpawn, __state))
            activeSpawn = __state.Parent;
        return __exception;
    }

    private static void AfterAtomize(bool __runOriginal, AtomizeScope? __state)
    {
        if (__state is null || !__runOriginal || !ReferenceEquals(activeAtomize, __state) ||
            __state.Produced.Count == 0 || !CanObserve())
            return;

        try
        {
            GameFacts.Record(Unit, state =>
            {
                foreach (var (def, amount) in __state.Produced)
                {
                    if (amount <= 0)
                        continue;
                    var key = OptionalTransitionRecording.QualifiedDefKey(
                        def.modContentPack?.PackageId, def.defName);
                    OptionalTransitionRecording.RecordCount(state, "completed", amount, key, withBreakdown: true);
                }
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishAtomize(Exception? __exception, AtomizeScope? __state)
    {
        if (__state is not null && ReferenceEquals(activeAtomize, __state))
            activeAtomize = __state.Parent;
        return __exception;
    }
}
