using System.Reflection;
using HarmonyLib;
using Synthetic;

namespace SyntheticPatches;

// The control from #313: the right types for static int Run(int count).
[HarmonyPatch(typeof(Target), nameof(Target.Run))]
internal static class RunExact
{
    private static void Postfix(int count, ref int __result) { }
}

// By value, an argument or the result may be boxed to object; a static method may read a static field.
[HarmonyPatch(typeof(Target), nameof(Target.Run))]
internal static class RunBoxed
{
    private static bool Prefix(object __0, int ___total, MethodBase __originalMethod, object[] __args) => true;

    private static void Postfix(object __result, bool __runOriginal) { }
}

// The instance as itself, a base class, an interface or object; fields by value or by reference, an inherited one
// too; a by-reference parameter by reference or by value; the patch's own state; a finalizer's exception.
[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribeInstance
{
    private static void Prefix(Target __instance, ref float scale, ref int ___baseCount, out int __state) => __state = 0;

    private static void Postfix(ILabelled __instance, float scale, string ___label, ref string __result, int __state) { }

    private static System.Exception? Finalizer(object __instance, System.Exception? __exception) => __exception;
}

// A pass-through postfix returns the type of its first argument.
[HarmonyPatch(typeof(Target), nameof(Target.Describe))]
internal static class DescribePassThrough
{
    private static string Postfix(string __result, int count) => __result + count;
}

// A parameter as its own type or object.
[HarmonyPatch(typeof(Target), nameof(Target.Act))]
internal static class ActArgument
{
    private static void Prefix(Base other) { }

    private static void Postfix(object other, Base __instance) { }
}

// A struct instance by reference or boxed; a value result boxed into ref object.
[HarmonyPatch(typeof(Cell), nameof(Cell.Area))]
internal static class CellArea
{
    private static void Prefix(ref Cell __instance, ref int ___x) { }

    private static void Postfix(object __instance, int ___x, ref object __result) { }
}
