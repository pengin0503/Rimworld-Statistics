using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.NewModOutcomes;

internal static class GravshipOutcomes
{
    private const string Chapter1 = "vanillaexpanded.gravship";
    private const string Chapter2 = "vanillaexpanded.gravship2";
    private const string Odyssey = "Ludeon.RimWorld.Odyssey";
    private const string HackUnit = "mod.vanillaexpanded.gravship2.warcomputerhacks";
    private const string BoonUnit = "mod.vanillaexpanded.gravship.launchboons";
    private static Type extensionType = null!;
    private static FieldInfo replacementField = null!;
    private static MethodInfo applyBoon = null!;
    private static Action<object, Gravship> invokeBoon = null!;
    private static FieldInfo boonDef = null!;
    private sealed record Hack(Thing Original, Map Map, IntVec3 Cell, ThingDef Replacement);

    internal static ModMetricAdapterRegistration Hacking() =>
        OutcomeMetric.Count(Chapter2, "warcomputerhacks", "RWS_NewMod_GravHack", MetricCategory.Exploration,
            InstallHack, dlc: Odyssey, dependencies: [Chapter1, "OskarPotocki.VanillaFactionsExpanded.Core"]);

    internal static ModMetricAdapterRegistration Boons() =>
        OutcomeMetric.Count(Chapter1, "launchboons", "RWS_NewMod_GravBoon", MetricCategory.Exploration,
            InstallBoon, breakdown: true, dlc: Odyssey, dependencies: ["OskarPotocki.VanillaFactionsExpanded.Core"]);

    private static void InstallHack(Harmony harmony)
    {
        const string assembly = "VanillaGravshipExpanded2";
        var type = OutcomeMetric.Bind(assembly + ".CompHostileWarcomputer_Hackable", assembly);
        extensionType = OutcomeMetric.Bind(assembly + ".WreckedBuildingReplacementExtension", assembly);
        replacementField = AccessTools.DeclaredField(extensionType, "replacementBuilding");
        if (!typeof(ThingComp).IsAssignableFrom(type) || replacementField?.FieldType != typeof(ThingDef))
            throw new InvalidOperationException("Warcomputer replacement signature changed");
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(type, "OnHacked", typeof(void), typeof(Pawn), typeof(bool)),
            typeof(GravshipOutcomes), "BeforeHack", "AfterHack");
    }

    private static void BeforeHack(ThingComp __instance, Pawn? __0, out Hack? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(HackUnit) || !GameFacts.IsPlayer(__0))
            return;
        try
        {
            if (__instance.parent is { Spawned: true } original &&
                original.def.modExtensions?.FirstOrDefault(extensionType.IsInstanceOfType) is { } extension &&
                replacementField.GetValue(extension) is ThingDef replacement &&
                !original.Position.GetThingList(original.Map).Any(thing => thing.def == replacement))
                __state = new Hack(original, original.Map, original.Position, replacement);
        }
        catch (Exception error) { LiveMetrics.Fail(HackUnit, error); }
    }

    private static void AfterHack(bool __runOriginal, Hack? __state)
    {
        if (!__runOriginal || __state is not { Original.Destroyed: true } hack)
            return;
        try
        {
            if (hack.Cell.GetThingList(hack.Map).Any(thing =>
                thing.Spawned && thing.def == hack.Replacement && thing.Position == hack.Cell))
                OutcomeMetric.Record(HackUnit);
        }
        catch (Exception error) { LiveMetrics.Fail(HackUnit, error); }
    }

    private static void InstallBoon(Harmony harmony)
    {
        const string assembly = "VanillaGravshipExpanded";
        var worker = OutcomeMetric.Bind(assembly + ".LaunchBoonWorker", assembly);
        var landing = OutcomeMetric.Bind(assembly + ".WorldComponent_GravshipController_LandingEnded_Patch", assembly);
        applyBoon = OutcomeMetric.Method(worker, "ApplyBoon", typeof(void), typeof(Gravship));
        boonDef = AccessTools.Field(worker, "def");
        if (boonDef is null || !typeof(Def).IsAssignableFrom(boonDef.FieldType))
            throw new InvalidOperationException("Gravship launch boon Def changed");
        var instance = System.Linq.Expressions.Expression.Parameter(typeof(object));
        var ship = System.Linq.Expressions.Expression.Parameter(typeof(Gravship));
        invokeBoon = System.Linq.Expressions.Expression.Lambda<Action<object, Gravship>>(
            System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Convert(instance, worker), applyBoon, ship),
            instance, ship).Compile();
        var original = OutcomeMetric.Method(landing, "TryTriggerLaunchBoon", typeof(void),
            typeof(Gravship), typeof(bool).MakeByRefType());
        var transpiler = AccessTools.DeclaredMethod(typeof(GravshipOutcomes), nameof(ObserveBoons));
        try { harmony.Patch(original, transpiler: new HarmonyMethod(transpiler)); }
        catch
        {
            harmony.Unpatch(original, transpiler);
            throw;
        }
    }

    private static IEnumerable<CodeInstruction> ObserveBoons(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        var calls = body.Where(code => code.Calls(applyBoon)).ToArray();
        if (calls.Length != 2)
            throw new InvalidOperationException("Gravship boon call sites changed");
        var wrapper = AccessTools.DeclaredMethod(typeof(GravshipOutcomes), nameof(ApplyAndRecord));
        foreach (var call in calls)
        {
            call.opcode = OpCodes.Call;
            call.operand = wrapper;
        }
        return body;
    }

    private static void ApplyAndRecord(object worker, Gravship ship)
    {
        invokeBoon(worker, ship);
        try
        {
            if (OutcomeMetric.Observing(BoonUnit) && GameFacts.IsPlayer(ship.Engine) &&
                boonDef.GetValue(worker) is Def def)
                OutcomeMetric.Record(BoonUnit, def, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(BoonUnit, error); }
    }
}
