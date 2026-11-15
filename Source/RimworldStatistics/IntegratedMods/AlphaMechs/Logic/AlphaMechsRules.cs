using RimworldStatistics.Core;

namespace RimworldStatistics.AlphaMechs;

internal static class AlphaMechsRules
{
    internal const string OwnerPackageId = "sarg.alphamechs";
    internal const string FrameworkPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    internal static string? ResolveConversionBreakdown(
        bool casterIsPlayer,
        bool targetIsMechanoid,
        bool targetWasPlayerOwnedBefore,
        bool targetIsAliveAndPlayerOwnedAfter,
        string? targetDefPackageId,
        string? targetDefName)
    {
        if (!casterIsPlayer ||
            !targetIsMechanoid ||
            targetWasPlayerOwnedBefore ||
            !targetIsAliveAndPlayerOwnedAfter ||
            string.IsNullOrWhiteSpace(targetDefName))
        {
            return null;
        }

        return OptionalTransitionRecording.QualifiedDefKey(targetDefPackageId, targetDefName);
    }
}
