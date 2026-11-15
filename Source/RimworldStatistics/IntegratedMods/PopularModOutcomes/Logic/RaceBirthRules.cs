namespace RimworldStatistics.PopularModOutcomes;

/// <summary>Game-independent post-commit validation of a mod-specific live birth.</summary>
internal static class RaceBirthRules
{
    internal static bool Committed(bool spawnReturnedSuccess, bool newChild,
        bool originalPlayerHome, bool livePlayerHumanlike, bool finalOperationCompleted)
        => spawnReturnedSuccess && newChild && originalPlayerHome
           && livePlayerHumanlike && finalOperationCompleted;
}
