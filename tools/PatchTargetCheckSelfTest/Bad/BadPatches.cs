using HarmonyLib;
using Synthetic;

namespace SyntheticPatches;

// Each class holds mismatches run.sh expects tools/PatchTargetCheck to report, one line each.

// The case from #313: names that exist with the wrong types (two failures).
[HarmonyPatch(typeof(Target), nameof(Target.Run))]
internal static class RunWrongTypes
{
    private static void Postfix(System.DateTime count, ref string __result) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Run))]
internal static class RunWrongPosition
{
    private static void Prefix(string __0) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Run))]
internal static class RunStaticInstance
{
    private static void Prefix(Target __instance) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Run))]
internal static class RunInstanceField
{
    private static void Postfix(string ___label) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Act))]
internal static class ActVoidResult
{
    private static void Postfix(object __result) { }
}

// Act takes any Base; the patch cannot assume a Target.
[HarmonyPatch(typeof(Target), nameof(Target.Act))]
internal static class ActDowncast
{
    private static void Prefix(Target other) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribeFieldType
{
    private static void Postfix(int ___label) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribeMissingField
{
    private static void Postfix(int ___missing) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribeRefType
{
    private static void Prefix(ref double scale) { }
}

// By reference, a float cannot be addressed as an object.
[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribeRefObject
{
    private static void Prefix(ref object scale) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribeUnrelatedInstance
{
    private static void Prefix(Cell __instance) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribeUnknownParameter
{
    private static void Prefix(int amount) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribeArgsType
{
    private static void Prefix(object __args) { }
}

[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribePrefixReturn
{
    private static int Prefix() => 0;
}

[HarmonyPatch(typeof(Cell), nameof(Cell.Area))]
internal static class CellResultType
{
    private static void Postfix(ref long __result) { }
}
