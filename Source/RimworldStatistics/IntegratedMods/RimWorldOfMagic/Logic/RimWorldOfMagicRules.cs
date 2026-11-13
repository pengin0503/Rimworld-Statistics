namespace RimworldStatistics.RimWorldOfMagic;

internal static class RimWorldOfMagicRules
{
    internal static bool HealingCommitted(bool result, float before, float after, int freshBefore, int freshAfter)
        => result && (after < before || freshAfter < freshBefore);
}
