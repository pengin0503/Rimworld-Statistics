namespace RimworldStatistics.Stage2ModOutcomes;

internal static class EmpireTaxStage2Rules
{
    internal const string OwnerPackageId = "Matathias.Empire";

    internal static bool IsEligibleTaxDeliveryEvent(string? defName, bool isDelayed, string? customDescription)
        => string.Equals(defName, "taxColony", StringComparison.Ordinal)
           || (string.Equals(defName, "deliveryArrival", StringComparison.Ordinal)
               && isDelayed
               && !string.IsNullOrWhiteSpace(customDescription));

    internal static long ComputeDeliveredItemSilverValue(
        bool wasRescheduled,
        bool isDestroyed,
        bool isSpawnedOnPlayerHome,
        bool hasHoldingOwner,
        bool isSilverDef,
        int stackCount,
        float unitMarketValue)
    {
        if (wasRescheduled || isDestroyed || stackCount <= 0 || (!isSpawnedOnPlayerHome && !hasHoldingOwner))
            return 0;

        if (isSilverDef)
            return stackCount;

        if (!float.IsFinite(unitMarketValue) || unitMarketValue <= 0f)
            return 0;

        var total = Math.Round((double)unitMarketValue * stackCount, MidpointRounding.AwayFromZero);
        return total >= long.MaxValue ? long.MaxValue : Math.Max(0L, (long)total);
    }
}
