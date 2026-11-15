using RimworldStatistics.Core;

namespace RimworldStatistics.MechanoidUpgrades;

internal static class MechanoidUpgradesRules
{
    internal const string OwnerPackageId = "GoGaTio.MechanoidUpgrades";

    internal static string? ResolveInstallBreakdown(
        int operationTypeValue,
        bool upgraderIsPlayer,
        bool mechAliveAndPlayerOwned,
        bool upgradePresentAfter,
        bool wasAlreadyPresentBefore,
        string? upgradeDefPackageId,
        string? upgradeDefName)
    {
        // MU.UpgradeOperationType: Add = 0, Remove = 1, Skip = 2
        if (operationTypeValue != 0 ||
            !upgraderIsPlayer ||
            !mechAliveAndPlayerOwned ||
            !upgradePresentAfter ||
            wasAlreadyPresentBefore ||
            string.IsNullOrWhiteSpace(upgradeDefName))
        {
            return null;
        }

        return OptionalTransitionRecording.QualifiedDefKey(
            string.IsNullOrWhiteSpace(upgradeDefPackageId) ? OwnerPackageId : upgradeDefPackageId,
            upgradeDefName);
    }
}
