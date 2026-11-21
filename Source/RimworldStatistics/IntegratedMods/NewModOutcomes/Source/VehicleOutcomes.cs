using System.Collections.Concurrent;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse.AI;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class VehicleOutcomes
{
    private const string Framework = "SmashPhil.VehicleFramework";
    private const string Boarding = "mod.smashphil.vehicleframework.boarding";
    private const string Upgrades = "mod.smashphil.vehicleframework.workshopupgrades";
    private const string Restoration = "mod.oskarpotocki.vanillavehiclesexpanded.wreckrestorations";
    private static Func<object, List<Pawn>?> getPassengers = null!;
    private static Func<object, object?> getTracker = null!;
    private static Func<object, Dictionary<Frame, ThingDef>?> getWrecks = null!;
    private static Func<object, Pawn?> getJobVehicle = null!;
    private static Func<object, object?> getUpgradeTree = null!;
    private static Func<object, object?> getUnlocking = null!;
    private static Func<object, string?> getNodeKey = null!;
    private static Func<object, HashSet<string>?> getInstalledUpgrades = null!;
    private static readonly ConcurrentDictionary<Type, Func<object, ThingDef?>?> ThingToSpawnCache = new();
    private sealed record Upgrade(object Tree, string Key, Def Vehicle);
    private static Type vehicle = null!;
    private sealed record Wreck(Frame Frame, Map Map, IntVec3 Cell, ThingDef Vehicle);

    internal static ModMetricAdapterRegistration Boardings() =>
        OutcomeMetric.Count(Framework, "boarding", "RWS_NewMod_Boarding", "RWS_NewMod_BoardingDesc",
            MetricCategory.Exploration,
            InstallBoarding, breakdown: true);

    internal static ModMetricAdapterRegistration CompletedUpgrades() =>
        OutcomeMetric.Count(Framework, "workshopupgrades", "RWS_NewMod_Upgrades", "RWS_NewMod_UpgradesDesc",
            MetricCategory.Work,
            InstallUpgrades, breakdown: true);

    internal static ModMetricAdapterRegistration Restorations() =>
        OutcomeMetric.Count("OskarPotocki.VanillaVehiclesExpanded", "wreckrestorations", "RWS_NewMod_Wrecks", "RWS_NewMod_WrecksDesc",
            MetricCategory.Work, InstallRestoration, breakdown: true,
            dependencies: [Framework, "OskarPotocki.VanillaFactionsExpanded.Core"]);

    private static void InstallBoarding(Harmony harmony)
    {
        var pawn = OutcomeMetric.Bind("Vehicles.VehiclePawn", "Vehicles");
        var handler = OutcomeMetric.Bind("Vehicles.VehicleRoleHandler", "Vehicles");
        var passengers = AccessTools.DeclaredProperty(pawn, "AllPawnsAboard");
        if (!typeof(Pawn).IsAssignableFrom(pawn) || passengers?.PropertyType != typeof(List<Pawn>))
            throw new InvalidOperationException("Vehicle manifest signature changed");
        getPassengers = OptionalTransitionCallSite.CompilePropertyGetter<List<Pawn>?>(passengers);
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(pawn, "TryAddPawn", typeof(bool), typeof(Pawn), handler),
            typeof(VehicleOutcomes), "BeforeBoard", "AfterBoard");
    }

    private static void BeforeBoard(Pawn __instance, Pawn __0, out bool __state)
    {
        __state = false;
        if (!OutcomeMetric.Observing(Boarding) || !GameFacts.IsPlayer(__instance) || !GameFacts.IsPlayer(__0))
            return;
        try
        {
            __state = getPassengers(__instance) is { } list && !list.Contains(__0);
        }
        catch (Exception error) { LiveMetrics.Fail(Boarding, error); }
    }

    private static void AfterBoard(Pawn __instance, Pawn __0, bool __runOriginal, bool __result, bool __state)
    {
        if (!__runOriginal || !__result || !__state)
            return;
        try
        {
            if (getPassengers(__instance) is { } list && list.Contains(__0))
                OutcomeMetric.Record(Boarding, __instance.def, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Boarding, error); }
    }

    private static void InstallUpgrades(Harmony harmony)
    {
        const string assembly = "Vehicles";
        var driver = OutcomeMetric.Bind(assembly + ".JobDriver_UpgradeVehicle", assembly);
        var pawn = OutcomeMetric.Bind(assembly + ".VehiclePawn", assembly);
        var tree = OutcomeMetric.Bind(assembly + ".CompUpgradeTree", assembly);
        var node = OutcomeMetric.Bind(assembly + ".UpgradeNode", assembly);
        var jobVehicle = AccessTools.Property(driver, "Vehicle");
        var upgradeTree = AccessTools.DeclaredProperty(pawn, "CompUpgradeTree");
        var unlocking = AccessTools.DeclaredProperty(tree, "NodeUnlocking");
        var nodeKey = AccessTools.DeclaredField(node, "key");
        var installedUpgrades = AccessTools.DeclaredField(tree, "upgrades");
        if (jobVehicle?.PropertyType != pawn || upgradeTree?.PropertyType != tree ||
            unlocking?.PropertyType != node || nodeKey?.FieldType != typeof(string) ||
            installedUpgrades?.FieldType != typeof(HashSet<string>))
            throw new InvalidOperationException("Vehicle upgrade state changed");
        getJobVehicle = OptionalTransitionCallSite.CompilePropertyGetter<Pawn?>(jobVehicle);
        getUpgradeTree = OptionalTransitionCallSite.CompilePropertyGetter<object?>(upgradeTree);
        getUnlocking = OptionalTransitionCallSite.CompilePropertyGetter<object?>(unlocking);
        getNodeKey = OptionalTransitionCallSite.CompileFieldGetter<string?>(nodeKey);
        getInstalledUpgrades = OptionalTransitionCallSite.CompileFieldGetter<HashSet<string>?>(installedUpgrades);
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(driver, "WorkComplete", typeof(void), typeof(Pawn)),
            typeof(VehicleOutcomes), "BeforeUpgrade", "AfterUpgrade");
    }

    private static void BeforeUpgrade(JobDriver __instance, Pawn __0, out Upgrade? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Upgrades) || !GameFacts.IsPlayer(__0))
            return;
        try
        {
            if (getJobVehicle(__instance) is not { Spawned: true } target || !GameFacts.IsPlayer(target) ||
                getUpgradeTree(target) is not { } tree || getUnlocking(tree) is not { } node ||
                getNodeKey(node) is not { } key || string.IsNullOrWhiteSpace(key) ||
                getInstalledUpgrades(tree) is not { } current || current.Contains(key))
                return;
            __state = new Upgrade(tree, key, target.def);
        }
        catch (Exception error) { LiveMetrics.Fail(Upgrades, error); }
    }

    private static void AfterUpgrade(bool __runOriginal, Upgrade? __state)
    {
        if (!__runOriginal || __state is not { } upgrade)
            return;
        try
        {
            if (getInstalledUpgrades(upgrade.Tree) is { } current && current.Contains(upgrade.Key))
                OutcomeMetric.Record(Upgrades, upgrade.Vehicle, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Upgrades, error); }
    }

    private static void InstallRestoration(Harmony harmony)
    {
        const string assembly = "VanillaVehiclesExpanded";
        var useTracker = OutcomeMetric.Bind(assembly + ".GameComponent_VehicleUseTracker", assembly);
        var tracker = AccessTools.DeclaredField(useTracker, "Instance");
        var wrecks = AccessTools.DeclaredField(useTracker, "frameWrecks");
        vehicle = OutcomeMetric.Bind("Vehicles.VehiclePawn", "Vehicles");
        var construction = OutcomeMetric.Bind("Vehicles.Patch_Construction", "Vehicles");
        if (tracker?.FieldType != useTracker || !tracker.IsStatic ||
            wrecks?.FieldType != typeof(Dictionary<Frame, ThingDef>) || !typeof(Pawn).IsAssignableFrom(vehicle))
            throw new InvalidOperationException("Vehicle wreck provenance signature changed");
        getTracker = OptionalTransitionCallSite.CompileFieldGetter<object?>(tracker);
        getWrecks = OptionalTransitionCallSite.CompileFieldGetter<Dictionary<Frame, ThingDef>?>(wrecks);
        var original = OutcomeMetric.Method(construction, "CompleteConstructionVehicle", typeof(bool), typeof(Pawn), typeof(Frame));
        var prefix = AccessTools.DeclaredMethod(typeof(VehicleOutcomes), nameof(BeforeRestoration));
        var postfix = AccessTools.DeclaredMethod(typeof(VehicleOutcomes), nameof(AfterRestoration));
        var patcher = new ScopedHarmonyPatcher(harmony);
        patcher.Patch(original,
            prefix: new HarmonyMethod(prefix) { priority = Priority.First, before = ["VanillaVehiclesExpanded.Mod"] },
            postfix: new HarmonyMethod(postfix));
    }

    private static ThingDef? ResolveThingToSpawn(ThingDef blueprint)
    {
        var getter = ThingToSpawnCache.GetOrAdd(blueprint.GetType(), static type =>
        {
            var field = AccessTools.Field(type, "thingToSpawn");
            return field is not null && typeof(ThingDef).IsAssignableFrom(field.FieldType)
                ? OptionalTransitionCallSite.CompileFieldGetter<ThingDef?>(field)
                : null;
        });
        return getter?.Invoke(blueprint);
    }

    private static void BeforeRestoration(Pawn __0, Frame __1, out Wreck? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Restoration) || DebugSettings.godMode || !GameFacts.IsPlayer(__0) ||
            !GameFacts.IsPlayer(__1) || !__1.Spawned)
            return;
        try
        {
            if (getTracker(null!) is not { } instance ||
                getWrecks(instance) is not { } tracked || !tracked.ContainsKey(__1) ||
                __1.def.entityDefToBuild is not ThingDef blueprint ||
                ResolveThingToSpawn(blueprint) is not { } expected)
                return;
            var things = __1.Position.GetThingList(__1.Map);
            for (var i = 0; i < things.Count; i++)
            {
                if (things[i].def == expected)
                    return;
            }
            __state = new Wreck(__1, __1.Map, __1.Position, expected);
        }
        catch (Exception error) { LiveMetrics.Fail(Restoration, error); }
    }

    private static void AfterRestoration(bool __result, bool __runOriginal, Wreck? __state)
    {
        if (!__runOriginal || __result || __state is not { Frame.Destroyed: true } restored)
            return;
        try
        {
            var things = restored.Cell.GetThingList(restored.Map);
            for (var i = 0; i < things.Count; i++)
            {
                var thing = things[i];
                if (vehicle.IsInstanceOfType(thing) &&
                    thing.Spawned && thing.Position == restored.Cell &&
                    thing.def == restored.Vehicle && GameFacts.IsPlayer(thing))
                {
                    OutcomeMetric.Record(Restoration, restored.Vehicle, breakdown: true);
                    return;
                }
            }
        }
        catch (Exception error) { LiveMetrics.Fail(Restoration, error); }
    }
}
