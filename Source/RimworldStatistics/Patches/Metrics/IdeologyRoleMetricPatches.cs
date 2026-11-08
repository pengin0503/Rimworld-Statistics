using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// RoleSingle and RoleMulti each implement abstract Precept_Role.Assign in 1.7.4985.
/// A successful assignment is a false-to-true IsAssigned transition, never an attempted
/// UI action, an existing role reapplication, or removing a role.
/// </summary>
internal static class RoleAssignmentMetricFacts
{
    public static bool WasAssigned(Precept_Role role, Pawn pawn)
    {
        if (pawn is null || role is null || LiveMetrics.Family(IdeologyFamily.Name) is null)
            return false;
        try { return role.IsAssigned(pawn); }
        catch (Exception e) { LiveMetrics.Fail(IdeologyFamily.Name, e); return false; }
    }

    public static void Assigned(Precept_Role role, Pawn pawn, bool wasAssigned)
    {
        if (wasAssigned || pawn is null || role is null || !pawn.IsFreeNonSlaveColonist
            || LiveMetrics.Family(IdeologyFamily.Name) is null)
            return;
        try
        {
            if (!role.IsAssigned(pawn))
                return;
            GameFacts.Record(IdeologyFamily.Name, s =>
                IdeologyFamily.RoleAppointed(s, GameFacts.Id(pawn), null, "assigned", eligible: true, confirmed: true));
        }
        catch (Exception e) { LiveMetrics.Fail(IdeologyFamily.Name, e); }
    }
}

[MetricAdapter(IdeologyFamily.Name)]
[HarmonyPatch(typeof(Precept_RoleSingle), nameof(Precept_RoleSingle.Assign))]
public static class AssignSingleIdeologyRoleMetricPatch
{
    public static void Prefix(Precept_RoleSingle __instance, Pawn p, out bool __state)
        => __state = RoleAssignmentMetricFacts.WasAssigned(__instance, p);

    public static void Postfix(Precept_RoleSingle __instance, Pawn p, bool __state)
        => RoleAssignmentMetricFacts.Assigned(__instance, p, __state);
}

[MetricAdapter(IdeologyFamily.Name)]
[HarmonyPatch(typeof(Precept_RoleMulti), nameof(Precept_RoleMulti.Assign))]
public static class AssignMultiIdeologyRoleMetricPatch
{
    public static void Prefix(Precept_RoleMulti __instance, Pawn p, out bool __state)
        => __state = RoleAssignmentMetricFacts.WasAssigned(__instance, p);

    public static void Postfix(Precept_RoleMulti __instance, Pawn p, bool __state)
        => RoleAssignmentMetricFacts.Assigned(__instance, p, __state);
}
