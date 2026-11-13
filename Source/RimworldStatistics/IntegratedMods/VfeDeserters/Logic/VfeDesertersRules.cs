namespace RimworldStatistics.VfeDeserters;

internal static class VfeDesertersRules
{
    internal static long IntelPaid(bool paymentAccepted, long before, long after, int cost)
        => paymentAccepted && cost > 0 && after >= 0 && before > after
            ? Math.Min(before - after, cost)
            : 0;
}
