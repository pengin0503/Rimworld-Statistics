using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.PopularModOutcomes;

internal static class FishCatchOutcomes
{
    private const string Unit = "mod.vanillaexpanded.vcef.fishing_catches";
    private static FieldInfo capturedDriver = null!;

    internal static ModMetricAdapterRegistration Build()
        => OutcomeMetric.Count("VanillaExpanded.VCEF", "fishing_catches", "RWS_VFECatches", "RWS_VFECatchesDesc",
            MetricCategory.Work, Install, breakdown: true);

    private static void Install(Harmony harmony)
    {
        const string assembly = "VCE-Fishing";
        var driver = OutcomeMetric.Bind("VCE_Fishing.JobDriver_Fish", assembly);
        var closure = AccessTools.Inner(driver, "<>c__DisplayClass13_0")
            ?? throw new InvalidOperationException("Vanilla Fishing Expanded catch closure changed");
        capturedDriver = AccessTools.DeclaredField(closure, "<>4__this") is { } field && field.FieldType == driver
            ? field : throw new InvalidOperationException("Vanilla Fishing Expanded catch driver changed");
        var catchFish = OutcomeMetric.Method(closure, "<MakeNewToils>b__3", typeof(void));
        OutcomeMetric.Transpile(harmony, catchFish, typeof(FishCatchOutcomes), nameof(ObserveCatch));
    }

    private static IEnumerable<CodeInstruction> ObserveCatch(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var spawn = AccessTools.DeclaredMethod(typeof(GenSpawn), nameof(GenSpawn.Spawn),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(WipeMode)]);
        var index = code.FindIndex(item => item.Calls(spawn));
        if (index < 0 || code.FindLastIndex(item => item.Calls(spawn)) != index)
            throw new InvalidOperationException("Vanilla Fishing Expanded catch spawn changed");
        var call = code[index];
        var loadClosure = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(call).MoveBlocksFrom(call);
        call.opcode = OpCodes.Call;
        call.operand = AccessTools.DeclaredMethod(typeof(FishCatchOutcomes), nameof(SpawningCatch));
        code.InsertRange(index, [loadClosure, new CodeInstruction(OpCodes.Ldfld, capturedDriver)]);
        return code;
    }

    private static Thing SpawningCatch(Thing thing, IntVec3 cell, Map map, WipeMode mode, JobDriver driver)
    {
        var result = GenSpawn.Spawn(thing, cell, map, mode);
        if (ReferenceEquals(thing, result) && result.Spawned && result.Map == map && GameFacts.IsPlayer(driver.pawn) &&
            OutcomeMetric.Observing(Unit))
            OutcomeMetric.Record(Unit, result.def, breakdown: true, amount: result.stackCount);
        return result;
    }
}
