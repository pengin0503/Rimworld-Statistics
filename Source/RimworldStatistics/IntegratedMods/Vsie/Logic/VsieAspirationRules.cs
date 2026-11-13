namespace RimworldStatistics.Vsie;

internal static class VsieAspirationRules
{
    internal static bool Completed(int before, int after, int required, bool removed,
        bool inspirationMatches)
        => required > 0 && before < required && after >= required &&
           after > before && removed && inspirationMatches;
}
