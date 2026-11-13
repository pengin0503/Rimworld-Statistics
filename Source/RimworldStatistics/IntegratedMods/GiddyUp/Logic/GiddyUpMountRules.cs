namespace RimworldStatistics.GiddyUp;

internal static class GiddyUpMountRules
{
    internal static bool Completed(bool before, bool after, bool boundToAnimal,
        bool ordinaryJob)
        => ordinaryJob && boundToAnimal && !before && after;
}
