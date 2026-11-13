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
    private static FieldInfo? assemblerCounter;
    private static FieldInfo? assemblerTimer;
    private static FieldInfo? assemblerKind;
    private static FieldInfo? droneOwnershipField;
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
        assemblerCounter = counter;
        assemblerTimer = timer;
        assemblerKind = kind;
        try
        {
            harmony.Patch(tick,
                prefix: new HarmonyMethod(typeof(VqeDroneFactoryStatistics), nameof(BeforeAssemblerTick)),
                postfix: new HarmonyMethod(typeof(VqeDroneFactoryStatistics), nameof(AfterAssemblerTick)));
        }
        catch
        {
            harmony.Unpatch(tick, HarmonyPatchType.All, harmony.Id);
            assemblerCounter = assemblerTimer = assemblerKind = null;
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
            "TrySpawnHatchedOrBornPawn", [typeof(Pawn), typeof(Thing)]);
        if (hatch?.ReturnType != typeof(void) || owned?.FieldType != typeof(bool) ||
            spawn?.ReturnType != typeof(bool))
            throw new InvalidOperationException("VQE hatcher or vanilla successful-spawn signature changed");
        droneOwnershipField = owned;
        try
        {
            harmony.Patch(hatch,
                prefix: new HarmonyMethod(typeof(VqeDroneFactoryStatistics), nameof(BeforeHatch)),
                postfix: new HarmonyMethod(typeof(VqeDroneFactoryStatistics), nameof(AfterHatch)),
                finalizer: new HarmonyMethod(typeof(VqeDroneFactoryStatistics), nameof(FinishHatch)));
            harmony.Patch(spawn,
                postfix: new HarmonyMethod(typeof(VqeDroneFactoryStatistics), nameof(AfterSpawnHatchedPawn)));
        }
        catch
        {
            harmony.Unpatch(hatch, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(spawn, HarmonyPatchType.All, harmony.Id);
            droneOwnershipField = null;
            throw;
        }
    }

    private static bool CanObserve(string unit)
        => MetricGates.IsRecording(unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeAssemblerTick(object __instance, out AssemblerBefore? __state)
    {
        __state = null;
        if (!CanObserve(AssemblyUnit) || __instance is not Building { Spawned: true } building ||
            building.Faction != Faction.OfPlayer || building.Map is not { } map)
            return;
        try
        {
            if (assemblerCounter?.GetValue(__instance) is not int before ||
                assemblerTimer?.GetValue(__instance) is not int target ||
                assemblerKind?.GetValue(__instance) is not PawnKindDef kind ||
                before <= target)
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
            building.Map != __state.Map || building.Faction != Faction.OfPlayer)
            return;
        try
        {
            if (assemblerCounter?.GetValue(__instance) is not int after || after >= __state.CountBefore)
                return;
            var produced = __state.Map.mapPawns.AllPawnsSpawned
                .Where(p => !__state.Existing.Contains(p)
                    && p.Spawned && p.Map == __state.Map &&
                    p.Faction == Faction.OfPlayer &&
                    ReferenceEquals(p.kindDef, __state.Kind)).ToArray();
            if (!VqeDroneRules.AssemblerSuccess(
                    __state.CountBefore, __state.Target,
                    produced.Length == 1, matchesKind: true, playerOwned: true))
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
            var player = child.Faction == Faction.OfPlayer;
            var born = child.Spawned && child.MapHeld?.IsPlayerHome == true;
            // The upstream hatcher tags the child as a player drone *after*
            // the successful vanilla spawn call, before Hatch returns.
            var markedAsDrone = child.AllComps.Any(c =>
                droneOwnershipField?.DeclaringType?.IsAssignableFrom(c.GetType()) == true
                    && droneOwnershipField.GetValue(c) is true);
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
