using RimworldStatistics.Core;

namespace RimworldStatistics.VqeDeadlife;

internal static class VqeDeadlifeRules
{
    internal const string OwnerPackageId = "vanillaquestsexpanded.deadlife";
    internal const string FrameworkPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    internal static string? ResolveSiloStudyBreakdown(
        bool actorIsPlayer,
        bool buildingValid,
        string? defPackageId,
        string? defName)
    {
        if (!actorIsPlayer || !buildingValid)
            return null;

        return OptionalTransitionRecording.QualifiedDefKey(defPackageId, defName);
    }

    internal static string? ResolvePowerKickstartBreakdown(
        bool actorIsPlayer,
        bool buildingValid,
        bool wasActiveBefore,
        bool isActiveAfter,
        string? defPackageId,
        string? defName)
    {
        if (!actorIsPlayer || !buildingValid || wasActiveBefore || !isActiveAfter)
            return null;

        return OptionalTransitionRecording.QualifiedDefKey(defPackageId, defName);
    }
}
