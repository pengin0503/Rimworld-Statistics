namespace RimworldStatistics.Gastronomy;

internal static class GastronomyPaymentRules
{
    internal static long Collected(int debtBefore, int actualTransfer, bool playerRestaurant)
        => playerRestaurant && debtBefore > 0 && actualTransfer > 0
            ? Math.Min(debtBefore, actualTransfer) : 0;
}
