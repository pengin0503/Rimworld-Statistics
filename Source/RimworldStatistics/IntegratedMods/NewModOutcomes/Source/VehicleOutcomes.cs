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
    private static PropertyInfo passengers = null!;
    private static FieldInfo tracker = null!;
    private static FieldInfo wrecks = null!;
    private static PropertyInfo jobVehicle = null!;
    private static PropertyInfo upgradeTree = null!;
    private static PropertyInfo unlocking = null!;
    private static FieldInfo nodeKey = null!;
    private static FieldInfo installedUpgrades = null!;
    private sealed record Upgrade(object Tree, string Key, Def Vehicle);
    private static Type vehicle = null!;
    private sealed record Wreck(Frame Frame, Map Map, IntVec3 Cell, ThingDef Vehicle);

    internal static ModMetricAdapterRegistration Boardings() =>
        OutcomeMetric.Count(Framework, "boarding", "RWS_NewMod_Boarding", MetricCategory.Exploration,
            InstallBoarding, breakdown: true);

    internal static ModMetricAdapterRegistration CompletedUpgrades() =>
        OutcomeMetric.Count(Framework, "workshopupgrades", "RWS_NewMod_Upgrades", MetricCategory.Work,
            InstallUpgrades, breakdown: true);

    internal static ModMetricAdapterRegistration Restorations() =>
        OutcomeMetric.Count("OskarPotocki.VanillaVehiclesExpanded", "wreckrestorations", "RWS_NewMod_Wrecks",
            MetricCategory.Work, InstallRestoration, breakdown: true,
            dependencies: [Framework, "OskarPotocki.VanillaFactionsExpanded.Core"]);

    private static void InstallBoarding(Harmony harmony)
    {
        var pawn = OutcomeMetric.Bind("Vehicles.VehiclePawn", "Vehicles");
        var handler = OutcomeMetric.Bind("Vehicles.VehicleRoleHandler", "Vehicles");
        passengers = AccessTools.DeclaredProperty(pawn, "AllPawnsAboard");
        if (!typeof(Pawn).IsAssignableFrom(pawn) || passengers?.PropertyType != typeof(List<Pawn>))
            throw new InvalidOperationException("Vehicle manifest signature changed");
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
            __state = passengers.GetValue(__instance) is List<Pawn> list && !list.Contains(__0);
        }
        catch (Exception error) { LiveMetrics.Fail(Boarding, error); }
    }

    private static void AfterBoard(Pawn __instance, Pawn __0, bool __runOriginal, bool __result, bool __state)
    {
        if (!__runOriginal || !__result || !__state)
            return;
        try
        {
            if (passengers.GetValue(__instance) is List<Pawn> list && list.Contains(__0))
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
        jobVehicle = AccessTools.Property(driver, "Vehicle");
        upgradeTree = AccessTools.DeclaredProperty(pawn, "CompUpgradeTree");
        unlocking = AccessTools.DeclaredProperty(tree, "NodeUnlocking");
        nodeKey = AccessTools.DeclaredField(node, "key");
        installedUpgrades = AccessTools.DeclaredField(tree, "upgrades");
        if (jobVehicle?.PropertyType != pawn || upgradeTree?.PropertyType != tree ||
            unlocking?.PropertyType != node || nodeKey?.FieldType != typeof(string) ||
            installedUpgrades?.FieldType != typeof(HashSet<string>))
            throw new InvalidOperationException("Vehicle upgrade state changed");
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
            if (jobVehicle.GetValue(__instance) is not Pawn { Spawned: true } target || !GameFacts.IsPlayer(target) ||
                upgradeTree.GetValue(target) is not { } tree || unlocking.GetValue(tree) is not { } node ||
                nodeKey.GetValue(node) is not string key || string.IsNullOrWhiteSpace(key) ||
                installedUpgrades.GetValue(tree) is not HashSet<string> current || current.Contains(key))
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
            if (installedUpgrades.GetValue(upgrade.Tree) is HashSet<string> current && current.Contains(upgrade.Key))
                OutcomeMetric.Record(Upgrades, upgrade.Vehicle, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Upgrades, error); }
    }

    private static void InstallRestoration(Harmony harmony)
    {
        const string assembly = "VanillaVehiclesExpanded";
        var useTracker = OutcomeMetric.Bind(assembly + ".GameComponent_VehicleUseTracker", assembly);
        tracker = AccessTools.DeclaredField(useTracker, "Instance");
        wrecks = AccessTools.DeclaredField(useTracker, "frameWrecks");
        vehicle = OutcomeMetric.Bind("Vehicles.VehiclePawn", "Vehicles");
        var construction = OutcomeMetric.Bind("Vehicles.Patch_Construction", "Vehicles");
        if (tracker?.FieldType != useTracker || !tracker.IsStatic ||
            wrecks?.FieldType != typeof(Dictionary<Frame, ThingDef>) || !typeof(Pawn).IsAssignableFrom(vehicle))
            throw new InvalidOperationException("Vehicle wreck provenance signature changed");
        var original = OutcomeMetric.Method(construction, "CompleteConstructionVehicle", typeof(bool), typeof(Pawn), typeof(Frame));
        var prefix = AccessTools.DeclaredMethod(typeof(VehicleOutcomes), nameof(BeforeRestoration));
        var postfix = AccessTools.DeclaredMethod(typeof(VehicleOutcomes), nameof(AfterRestoration));
        try
        {
            harmony.Patch(original, prefix: new HarmonyMethod(prefix) { priority = Priority.First, before = ["VanillaVehiclesExpanded.Mod"] },
                postfix: new HarmonyMethod(postfix));
        }
        catch
        {
            harmony.Unpatch(original, prefix);
            harmony.Unpatch(original, postfix);
            throw;
        }
    }

    private static void BeforeRestoration(Pawn __0, Frame __1, out Wreck? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Restoration) || DebugSettings.godMode || !GameFacts.IsPlayer(__0) ||
            !GameFacts.IsPlayer(__1) || !__1.Spawned)
            return;
        try
        {
            if (tracker.GetValue(null) is not { } instance ||
                wrecks.GetValue(instance) is not Dictionary<Frame, ThingDef> tracked || !tracked.ContainsKey(__1) ||
                __1.def.entityDefToBuild is not ThingDef blueprint ||
                AccessTools.Field(blueprint.GetType(), "thingToSpawn")?.GetValue(blueprint) is not ThingDef expected ||
                __1.Position.GetThingList(__1.Map).Any(thing => thing.def == expected))
                return;
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
            if (restored.Cell.GetThingList(restored.Map).Any(thing => vehicle.IsInstanceOfType(thing) &&
                thing.Spawned && thing.Position == restored.Cell && thing.def == restored.Vehicle && GameFacts.IsPlayer(thing)))
                OutcomeMetric.Record(Restoration, restored.Vehicle, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Restoration, error); }
    }
}
