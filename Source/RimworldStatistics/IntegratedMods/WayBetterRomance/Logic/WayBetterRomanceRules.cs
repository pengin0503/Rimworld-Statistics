namespace RimworldStatistics.WayBetterRomance;

internal static class WayBetterRomanceRules
{
    internal const string OwnerPackageId = "divineDerivative.Romance";
    internal const string DateBreakdownKey = "divinederivative.romance/Date";
    internal const string HangoutBreakdownKey = "divinederivative.romance/Hangout";

    internal static string? ResolveDateBreakdown(
        bool playerInvolved,
        bool participantsAlive,
        bool partnerWasFollowingExpectedJob,
        bool isDate)
    {
        if (!playerInvolved || !participantsAlive || !partnerWasFollowingExpectedJob)
            return null;

        return isDate ? DateBreakdownKey : HangoutBreakdownKey;
    }
}
