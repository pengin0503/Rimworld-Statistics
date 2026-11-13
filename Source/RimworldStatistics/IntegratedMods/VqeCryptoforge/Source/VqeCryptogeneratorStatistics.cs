using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.VqeCryptoforge;

internal static class VqeCryptogeneratorStatistics
{
    private const string Owner = "vanillaquestsexpanded.cryptoforge";
    private const string Unit = "mod.vanillaquestsexpanded.cryptoforge.generatorrestart";
    private const string Offline = "VQE_FrozenCryptogenerator_Off";
    private const string Online = "VQE_FrozenCryptogenerator";
    private static FieldInfo? driverField;
    private static FieldInfo? timer;
    private static int threshold;
    private static MethodInfo? spawn;
    [ThreadStatic] private static Restart? active;

    private sealed class Restart(Building original)
    {
        public Building Original { get; } = original;
        public Map Map { get; } = original.Map;
        public IntVec3 Position { get; } = original.Position;
        public Thing? Replacement { get; set; }
    }

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.cryptogenerator",
        OwnerPackageId = Owner,
        FamilyId = "generatorrestart",
        MetricName = "completed",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Exploration,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_VqeCryptoforge_GeneratorRestarts",
        DescriptionKey = "RWS_VqeCryptoforge_GeneratorRestartsDesc",
    }, new LiveFamily
    {
        Name = Unit,
        Revision = 1,
        Build = output => output.PutCount("completed", Unit + ".completed"),
    }, Install);

    private static void Install(Harmony harmony)
    {
        const string assembly = "VanillaQuestsExpandedCryptoforge";
        var building = AccessTools.TypeByName(assembly + ".Restartable");
        var driver = building?.Assembly.GetType(assembly + ".JobDriver_RestartGenerator");
        var closure = driver?.GetNestedType("<>c__DisplayClass5_0", BindingFlags.NonPublic);
        var callback = closure is null ? null : AccessTools.DeclaredMethod(closure, "<MakeNewToils>b__3", []);
        var capturedDriver = closure is null ? null : AccessTools.DeclaredField(closure, "<>4__this");
        var restart = building is null ? null : AccessTools.DeclaredMethod(building, "Notify_Restarted", []);
        var ticks = driver is null ? null : AccessTools.DeclaredField(driver, "totalTimer");
        var limit = driver is null ? null : AccessTools.DeclaredField(driver, "totalTime");
        var spawnThing = AccessTools.DeclaredMethod(typeof(GenSpawn), nameof(GenSpawn.Spawn),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool)]);
        if (building?.Assembly.GetName().Name != assembly || !typeof(Building).IsAssignableFrom(building) ||
            driver is null || !typeof(JobDriver).IsAssignableFrom(driver) || capturedDriver?.FieldType != driver ||
            callback?.ReturnType != typeof(void) || restart?.ReturnType != typeof(void) ||
            ticks?.FieldType != typeof(int) || limit is not { IsLiteral: true } ||
            limit.GetRawConstantValue() is not int length || length <= 0 || spawnThing?.ReturnType != typeof(Thing) ||
            PatchProcessor.GetOriginalInstructions(callback).Count(code => code.Calls(restart)) != 1)
            throw new InvalidOperationException("Cryptoforge generator restart signature changed");

        driverField = capturedDriver;
        timer = ticks;
        threshold = length;
        spawn = spawnThing;
        try
        {
            harmony.Patch(restart, transpiler: new HarmonyMethod(typeof(VqeCryptogeneratorStatistics), nameof(Observe)));
            harmony.Patch(callback,
                prefix: new HarmonyMethod(typeof(VqeCryptogeneratorStatistics), nameof(Before)),
                postfix: new HarmonyMethod(typeof(VqeCryptogeneratorStatistics), nameof(After)),
                finalizer: new HarmonyMethod(typeof(VqeCryptogeneratorStatistics), nameof(Restore)));
        }
        catch
        {
            harmony.Unpatch(callback, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(restart, HarmonyPatchType.All, harmony.Id);
            throw;
        }
    }

    private static void Before(object __instance, out Restart? __state)
    {
        __state = active;
        active = null;
        if (!OptionalTransitionCallSite.Observing(Unit))
            return;
        try
        {
            if (driverField?.GetValue(__instance) is not JobDriver driver || !GameFacts.IsPlayer(driver.pawn) ||
                timer?.GetValue(driver) is not int elapsed || elapsed <= threshold ||
                driver.job?.GetTarget(TargetIndex.A).Thing is not Building { Spawned: true } original ||
                original.def.defName != Offline)
                return;
            active = new Restart(original);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static IEnumerable<CodeInstruction> Observe(IEnumerable<CodeInstruction> instructions)
        => OptionalTransitionCallSite.ObserveSpawn(instructions, spawn!,
            AccessTools.DeclaredMethod(typeof(VqeCryptogeneratorStatistics), nameof(Capture)));

    private static void Capture(Thing spawned)
    {
        if (active is { } restart && spawned?.def?.defName == Online)
            restart.Replacement = spawned;
    }

    private static void After(bool __runOriginal)
    {
        if (!__runOriginal || active is not { Replacement: { Spawned: true } replacement } restart ||
            restart.Original.Spawned || replacement.Map != restart.Map || replacement.Position != restart.Position)
            return;
        try { GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(state, "completed")); }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void Restore(Restart? __state) => active = __state;
}
