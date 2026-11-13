namespace RimworldStatistics.VfeClassical;

internal static class VfeClassicalRules
{
    internal static bool FavorGained(bool before, bool after, bool normalPlay)
        => normalPlay && !before && after;
}
