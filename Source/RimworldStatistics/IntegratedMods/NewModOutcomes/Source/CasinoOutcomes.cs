using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.NewModOutcomes;

internal static class CasinoOutcomes
{
    private const string Owner = "Adamas.HospitalityCasino";
    private const string Unit = "mod.adamas.hospitalitycasino.slotrounds";
    private static FieldInfo driverField = null!;
    private static FieldInfo running = null!;
    private static FieldInfo outcome = null!;

    internal static ModMetricAdapterRegistration Build() =>
        OutcomeMetric.Count(Owner, "slotrounds", "RWS_NewMod_Casino", MetricCategory.Economy,
            Install, breakdown: true);

    private static void Install(Harmony harmony)
    {
        const string assembly = "HospitalityCasino";
        var driver = OutcomeMetric.Bind(assembly + ".JobDriver_PlaySlotMachine", assembly);
        var manager = OutcomeMetric.Bind(assembly + ".ReelEventManager", assembly);
        var closure = driver.GetNestedType("<>c__DisplayClass1_0", BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Casino slot job callback changed");
        var tick = OutcomeMetric.Method(closure, "<MakeNewToils>b__1", typeof(void));
        driverField = AccessTools.DeclaredField(closure, "<>4__this");
        running = AccessTools.DeclaredField(manager, "gameStarted");
        outcome = AccessTools.DeclaredField(manager, "outcome");
        if (driverField?.FieldType != driver || running?.FieldType != typeof(bool) || outcome?.FieldType.IsEnum != true)
            throw new InvalidOperationException("Casino slot round state changed");
        var patch = AccessTools.DeclaredMethod(typeof(CasinoOutcomes), nameof(ObserveCompletion));
        try { harmony.Patch(tick, transpiler: new HarmonyMethod(patch)); }
        catch
        {
            harmony.Unpatch(tick, patch);
            throw;
        }
    }

    private static IEnumerable<CodeInstruction> ObserveCompletion(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        var store = body.Single(code => code.StoresField(running));
        int index = body.IndexOf(store);
        if (index == 0 || body[index - 1].opcode != OpCodes.Ldc_I4_0)
            throw new InvalidOperationException("Casino slot completion boundary changed");

        // Preserve the original field write, and give the observer the manager already on the stack.
        var copy = new CodeInstruction(OpCodes.Dup);
        body[index - 1].MoveLabelsTo(copy);
        body[index - 1].MoveBlocksTo(copy);
        body.Insert(index - 1, copy);
        body.Insert(index + 2, new CodeInstruction(OpCodes.Ldarg_0));
        body.Insert(index + 3, new CodeInstruction(OpCodes.Call,
            AccessTools.DeclaredMethod(typeof(CasinoOutcomes), nameof(RecordRound))));
        return body;
    }

    private static void RecordRound(object manager, object closure)
    {
        if (!OutcomeMetric.Observing(Unit))
            return;
        try
        {
            if (driverField.GetValue(closure) is not JobDriver driver ||
                !GameFacts.IsPlayer(driver.job?.GetTarget(TargetIndex.A).Thing) ||
                outcome.GetValue(manager)?.ToString() is not string name ||
                name is not ("Loss" or "Single" or "Double" or "Jackpot"))
                return;
            var key = OptionalTransitionRecording.QualifiedDefKey(Owner, name);
            GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                state, "completed", key: key, withBreakdown: true));
        }
        catch (Exception error) { LiveMetrics.Fail(Unit, error); }
    }
}
