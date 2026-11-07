using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Skill levels (evidence #lifetime): SkillRecord.Learn runs often (every working tick), so the prefix only copies the level and
/// the postfix returns unless the level changed.
/// </summary>
[MetricAdapter(LifetimeFamily.Name)]
[HarmonyPatch(typeof(SkillRecord), nameof(SkillRecord.Learn))]
public static class SkillLearnMetricPatch
{
    public static void Prefix(int ___levelInt, out int __state) => __state = ___levelInt;

    public static void Postfix(SkillRecord __instance, int ___levelInt, Pawn ___pawn, int __state)
    {
        if (___levelInt == __state || ___pawn == null || __instance.def == null || !GameFacts.IsMember(___pawn))
            return;
        var id = GameFacts.Id(___pawn);
        var skill = __instance.def.defName;
        var after = ___levelInt;
        GameFacts.Record(LifetimeFamily.Name, s => LifetimeFamily.SkillChanged(s, id, skill, __state, after));
    }
}
