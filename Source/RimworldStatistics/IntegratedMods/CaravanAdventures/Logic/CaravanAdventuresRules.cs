using RimworldStatistics.Core;

namespace RimworldStatistics.CaravanAdventures;

internal static class CaravanAdventuresRules
{
    internal const string OwnerPackageId = "iforgotmysocks.CaravanAdventures";

    internal static (long Delta, string? BreakdownKey) ResolveBountyCreditReward(
        bool mechValid,
        bool targetIsNonPlayer,
        float beforePoints,
        float afterPoints,
        string? defPackageId,
        string? defName)
    {
        if (!mechValid || !targetIsNonPlayer ||
            float.IsNaN(beforePoints) || float.IsInfinity(beforePoints) ||
            float.IsNaN(afterPoints) || float.IsInfinity(afterPoints))
            return (0, null);

        var delta = (long)Math.Round(afterPoints - beforePoints);
        if (delta <= 0)
            return (0, null);

        return (delta, OptionalTransitionRecording.QualifiedDefKey(defPackageId, defName));
    }

    internal static bool ResolveShrineCompletion(bool worldCompValid, int beforeCount, int afterCount)
        => worldCompValid && afterCount > beforeCount && afterCount - beforeCount == 1;
}
