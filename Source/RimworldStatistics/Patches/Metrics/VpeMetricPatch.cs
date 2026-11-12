using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

[MetricAdapter(VpeStatistics.EventUnit)]
[HarmonyPatch]
internal static class VpeMetricPatch
{
    internal static Type? ImplantType => Contract.Value.Type;
    internal static bool HasLevelTarget => Contract.Value.Method is not null;
    private static readonly Lazy<(Type? Type, MethodInfo? Method)> Contract = new(() =>
    {
        if (!ModsConfig.IsActive(VpeStatistics.PackageId))
            return (null, null);
        var type = AccessTools.TypeByName(VpeStatistics.ImplantType);
        return type is not null && typeof(Hediff_Level).IsAssignableFrom(type)
            ? (type, AccessTools.DeclaredMethod(type, nameof(Hediff_Level.ChangeLevel), [typeof(int)]))
            : (null, null);
    });

    private static MethodBase TargetMethod() => Contract.Value.Method
        ?? throw new MissingMethodException(VpeStatistics.ImplantType, nameof(Hediff_Level.ChangeLevel));

    [HarmonyPrefix]
    private static void Prefix(Hediff_Level __instance, out int __state) => __state = __instance.level;

    [HarmonyPostfix]
    private static void Postfix(Hediff_Level __instance, int __0, int __state)
    {
        var level = __instance.level;
        if (__0 > 0 && level > __state && GameFacts.IsPlayer(__instance.pawn))
            GameFacts.Record(VpeStatistics.EventUnit, state => VpeStatistics.RecordLevels(state, __state, level, true));
    }
}
