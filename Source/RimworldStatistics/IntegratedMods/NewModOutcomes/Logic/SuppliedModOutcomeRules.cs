namespace RimworldStatistics.NewModOutcomes;

internal static class SuppliedModOutcomeRules
{
    internal static bool AdvancedSemester(int before, int after) => before > 0 && (long)after - before == 1;

    internal static bool CompletedEvent(bool finalized, bool completed, int activeTicks)
        => finalized && completed && activeTicks > 0;
}
