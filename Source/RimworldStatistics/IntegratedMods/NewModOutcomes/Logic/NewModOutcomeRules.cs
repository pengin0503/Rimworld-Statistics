namespace RimworldStatistics.NewModOutcomes;

internal static class NewModOutcomeRules
{
    internal static bool WinnerAdded(int before, int after)
        => before >= 0 && (long)after - before == 1;

    internal static bool RoadImproved(bool forward, bool reverse)
        => forward && reverse;
}
