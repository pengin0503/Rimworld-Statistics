using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RimworldStatistics.Metrics;

internal static class OptionalTransitionCallSite
{
    internal static bool Observing(string unit)
        => MetricGates.IsRecording(unit) && Verse.Current.ProgramState == Verse.ProgramState.Playing
           && Verse.Scribe.mode == Verse.LoadSaveMode.Inactive && Verse.Find.TickManager is not null;

    internal static IEnumerable<CodeInstruction> ObserveSpawn(
        IEnumerable<CodeInstruction> instructions, MethodInfo spawn, MethodInfo observe)
    {
        var body = instructions.ToList();
        var call = body.Single(instruction => instruction.Calls(spawn));
        body.InsertRange(body.IndexOf(call) + 1,
            [new CodeInstruction(OpCodes.Dup), new CodeInstruction(OpCodes.Call, observe)]);
        return body;
    }

    internal static IEnumerable<CodeInstruction> Replace(
        IEnumerable<CodeInstruction> instructions, MethodInfo target, MethodInfo replacement, int count)
    {
        var body = instructions.ToList();
        var matches = body.Where(instruction => instruction.Calls(target)).ToArray();
        if (matches.Length != count)
            throw new InvalidOperationException($"Expected {count} calls to {target.DeclaringType?.Name}.{target.Name}");
        // Only the first matching call is wrapped. Preserve labels and exception blocks.
        matches[0].opcode = OpCodes.Call;
        matches[0].operand = replacement;
        return body;
    }
}
