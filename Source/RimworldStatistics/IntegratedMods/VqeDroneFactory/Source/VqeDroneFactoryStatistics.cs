using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VqeDroneFactory;

/// <summary>
/// Independent drone-production sources: a completed automated assembler cycle
/// and a successful egg hatch. A timer/egg destruction alone is not production.
/// The assembler allocates a before-set only when its timer is already ready.
/// </summary>
internal static class VqeDroneFactoryStatistics
{
    private const string Owner = "vanillaquestsexpanded.dronefactory";
    private const string AssemblyUnit = "mod.vanillaquestsexpanded.dronefactory.assembly";
    private const string HatchUnit = "mod.vanillaquestsexpanded.dronefactory.hatching";
    private static Func<object, int>? getAssemblerCounter;
    private static Func<object, int>? getAssemblerTimer;
    private static Func<object, PawnKindDef?>? getAssemblerKind;
    private static Type? droneCompType;
    private static Func<object, bool>? getDroneOwnership;
    [ThreadStatic] private static HatchScope? hatching;

    private sealed record AssemblerBefore(int CountBefore, int Target, PawnKindDef Kind, Map Map,
        HashSet<Pawn> Existing);
    private sealed class HatchScope
    {
        internal required Thing Egg { get; init; }
        internal required HatchScope? Parent { get; init; }
        internal Pawn? NewPawn;
        internal bool SpawnSucceeded;
    }

    private static readonly LiveFamily AssemblyFamily = new()
    {
        Name = AssemblyUnit,
        Revision = 1,
        Build = o => OptionalTransitionRecording.PutCount(
            o, "drones", AssemblyUnit + ".drones_spawned", withBreakdown: true),
    };

    private static readonly LiveFamily HatchFamily = new()
    {
        Name = HatchUnit,
        Revision = 1,
        Build = o => OptionalTransitionRecording.PutCount(
            o, "drones", HatchUnit + ".drones_spawned", withBreakdown: true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor D(string family, string label, string desc) => new()
        {
            ProviderId = "rws." + family,
            OwnerPackageId = Owner,
            FamilyId = family,
            MetricName = "drones_spawned",
            Source = MetricObservationSource.Checkpoint,
            Revision = 1,
            Category = MetricCategory.Population,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = label,
            DescriptionKey = desc,
        };
        return
        [
            new ModMetricAdapterRegistration(
                D("assembly", "RWS_VqeDrones_Assembled", "RWS_VqeDrones_AssembledDesc"),
                AssemblyFamily, InstallAssembler),
            new ModMetricAdapterRegistration(
                D("hatching", "RWS_VqeDrones_Hatched", "RWS_VqeDrones_HatchedDesc"),
                HatchFamily, InstallHatcher),
        ];
    }

    private static bool IsExpectedModType(Type type) =>
        string.Equals(type.Assembly.GetName().Name, "VanillaQuestsExpandedDroneFactory",
            StringComparison.OrdinalIgnoreCase);

    private static void InstallAssembler(Harmony harmony)
    {
        var type = AccessTools.TypeByName("VanillaQuestsExpandedDroneFactory.Building_AutomatedDroneAssembler");
        if (type is null || !typeof(Building).IsAssignableFrom(type) ||
            !IsExpectedModType(type))
            throw new InvalidOperationException("VQE Drone Factory active assembler assembly missing");
        var tick = AccessTools.DeclaredMethod(type, "TickInterval", [typeof(int)]);
        var counter = AccessTools.DeclaredField(type, "tickCounter");
        var timer = AccessTools.DeclaredField(type, "currentTimer");
        var kind = AccessTools.DeclaredField(type, "currentPawn");
        if (tick?.ReturnType != typeof(void) ||
            counter?.FieldType != typeof(int) || timer?.FieldType != typeof(int) ||
            kind?.FieldType != typeof(PawnKindDef))
            throw new InvalidOperationException("VQE Drone Factory active assembler signature changed");
        getAssemblerCounter = OptionalTransitionCallSite.CompileFieldGetter<int>(counter);
        getAssemblerTimer = OptionalTransitionCallSite.CompileFieldGetter<int>(timer);
        getAssemblerKind = OptionalTransitionCallSite.CompileFieldGetter<PawnKindDef?>(kind);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(tick, typeof(VqeDroneFactoryStatistics), nameof(BeforeAssemblerTick), nameof(AfterAssemblerTick));
        }
        catch
        {
            getAssemblerCounter = null;
            getAssemblerTimer = null;
            getAssemblerKind = null;
            throw;
        }
    }

    private static void InstallHatcher(Harmony harmony)
    {
        var type = AccessTools.TypeByName("VanillaQuestsExpandedDroneFactory.CompDroneHatcher");
        var compType = AccessTools.TypeByName("VanillaQuestsExpandedDroneFactory.CompDrone");
        if (type is null || compType is null || !IsExpectedModType(type) ||
            !ReferenceEquals(type.Assembly, compType.Assembly) ||
            !typeof(ThingComp).IsAssignableFrom(type))
            throw new InvalidOperationException("VQE Drone Factory active hatcher assembly missing");
        var hatch = AccessTools.DeclaredMethod(type, "Hatch", Type.EmptyTypes);
        var owned = AccessTools.DeclaredField(compType, "isPlayerDrone");
        var spawn = AccessTools.DeclaredMethod(typeof(PawnUtility),
            nameof(PawnUtility.TrySpawnHatchedOrBornPawn), [typeof(Pawn), typeof(Thing), typeof(IntVec3?)])
            ?? AccessTools.DeclaredMethod(typeof(PawnUtility),
            nameof(PawnUtility.TrySpawnHatchedOrBornPawn), [typeof(Pawn), typeof(Thing)]);
        if (hatch?.ReturnType != typeof(void) || owned?.FieldType != typeof(bool) ||
            spawn?.ReturnType != typeof(bool))
            throw new InvalidOperationException("VQE hatcher or vanilla successful-spawn signature changed");
        droneCompType = compType;
        getDroneOwnership = OptionalTransitionCallSite.CompileFieldGetter<bool>(owned);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(hatch, typeof(VqeDroneFactoryStatistics),
                prefix: nameof(BeforeHatch), postfix: nameof(AfterHatch), finalizer: nameof(FinishHatch));
            patcher.Patch(spawn, typeof(VqeDroneFactoryStatistics),
                postfix: nameof(AfterSpawnHatchedPawn));
        }
        catch
        {
            droneCompType = null;
            getDroneOwnership = null;
            throw;
        }
    }

    private static bool CanObserve(string unit) => OptionalTransitionCallSite.Observing(unit);

    private static void BeforeAssemblerTick(object __instance, out AssemblerBefore? __state)
    {
        __state = null;
        if (!CanObserve(AssemblyUnit) || __instance is not Building { Spawned: true } building ||
            !GameFacts.IsPlayerFaction(building.Faction) || building.Map is not { } map)
            return;
        try
        {
            if (getAssemblerCounter is null || getAssemblerTimer is null)
                return;
            var before = getAssemblerCounter(__instance);
            var target = getAssemblerTimer(__instance);
            if (before <= target || getAssemblerKind?.Invoke(__instance) is not { } kind)
                return;
            __state = new AssemblerBefore(before, target, kind, map,
                new HashSet<Pawn>(map.mapPawns.AllPawnsSpawned, ReferenceEqualityComparer.Instance));
        }
        catch (Exception e) { LiveMetrics.Fail(AssemblyUnit, e); }
    }

    private static void AfterAssemblerTick(object __instance, bool __runOriginal,
        AssemblerBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve(AssemblyUnit) ||
            __instance is not Building { Spawned: true } building ||
            building.Map != __state.Map || !GameFacts.IsPlayerFaction(building.Faction))
            return;
        try
        {
            if (getAssemblerCounter is null)
                return;
            var after = getAssemblerCounter(__instance);
            if (after >= __state.CountBefore)
                return;
            var spawned = __state.Map.mapPawns.AllPawnsSpawned;
            var producedCount = 0;
            for (var i = 0; i < spawned.Count; i++)
            {
                var p = spawned[i];
                if (!__state.Existing.Contains(p)
                    && p.Spawned && p.Map == __state.Map &&
                    GameFacts.IsPlayerFaction(p.Faction) &&
                    ReferenceEquals(p.kindDef, __state.Kind))
                {
                    producedCount++;
                    if (producedCount > 1)
                        break;
                }
            }
            if (!VqeDroneRules.AssemblerSuccess(
                    __state.CountBefore, __state.Target,
                    producedCount == 1, matchesKind: true, playerOwned: true))
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(
                __state.Kind.modContentPack?.PackageId, __state.Kind.defName);
            GameFacts.Record(AssemblyUnit, s => OptionalTransitionRecording.RecordCount(
                s, "drones", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(AssemblyUnit, e); }
    }

    private static void BeforeHatch(object __instance, out HatchScope? __state)
    {
        __state = null;
        if (!CanObserve(HatchUnit) || __instance is not ThingComp { parent: { } egg } ||
            egg.MapHeld is not { IsPlayerHome: true })
            return;
        __state = new HatchScope { Egg = egg, Parent = hatching };
        hatching = __state;
    }

    private static void AfterSpawnHatchedPawn(Pawn __0, Thing __1, bool __result)
    {
        if (!__result || hatching is not { } scope ||
            !ReferenceEquals(scope.Egg, __1) || !CanObserve(HatchUnit))
            return;
        scope.NewPawn = __0;
        scope.SpawnSucceeded = true;
    }

    private static void AfterHatch(bool __runOriginal, HatchScope? __state)
    {
        if (!__runOriginal || __state?.NewPawn is not Pawn child ||
            !__state.SpawnSucceeded || !CanObserve(HatchUnit))
            return;
        try
        {
            var player = GameFacts.IsPlayerFaction(child.Faction);
            var born = child.Spawned && child.MapHeld?.IsPlayerHome == true;
            // The upstream hatcher tags the child as a player drone *after*
            // the successful vanilla spawn call, before Hatch returns.
            var markedAsDrone = false;
            if (droneCompType is not null && getDroneOwnership is not null && child.AllComps is { } comps)
            {
                for (var i = 0; i < comps.Count; i++)
                {
                    var c = comps[i];
                    if (droneCompType.IsInstanceOfType(c) && getDroneOwnership(c))
                    {
                        markedAsDrone = true;
                        break;
                    }
                }
            }
            if (!VqeDroneRules.HatchSuccess(__state.SpawnSucceeded, born, player && markedAsDrone))
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(
                child.kindDef?.modContentPack?.PackageId, child.kindDef?.defName);
            GameFacts.Record(HatchUnit, s => OptionalTransitionRecording.RecordCount(
                s, "drones", key: key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(HatchUnit, e); }
    }

    private static Exception? FinishHatch(Exception? __exception, HatchScope? __state)
    {
        if (__state is not null && ReferenceEquals(hatching, __state))
            hatching = __state.Parent;
        return __exception;
    }
}
