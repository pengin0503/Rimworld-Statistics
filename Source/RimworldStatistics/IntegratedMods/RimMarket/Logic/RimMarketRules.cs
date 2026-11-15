using RimworldStatistics.Core;

namespace RimworldStatistics.RimMarket;

internal static class RimMarketRules
{
    internal const string OwnerPackageId = "forti.rimmarket";

    internal static string? ResolveStallSaleBreakdown(
        bool succeeded,
        bool isColonyStall,
        bool buildingSpawned,
        bool buildingIsPlayer,
        bool buyerIsNonPlayer,
        int price,
        string? defPackageId,
        string? defName)
    {
        if (!succeeded || !isColonyStall || !buildingSpawned || !buildingIsPlayer || !buyerIsNonPlayer || price <= 0)
            return null;

        return OptionalTransitionRecording.QualifiedDefKey(defPackageId, defName);
    }

    internal static int ResolveMarketFeeDelta(
        bool visitValid,
        bool marketIsPlayer,
        int totalFeesBefore,
        int totalFeesAfter)
    {
        if (!visitValid || !marketIsPlayer || totalFeesAfter <= totalFeesBefore)
            return 0;

        return totalFeesAfter - totalFeesBefore;
    }
}
