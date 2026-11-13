namespace RimworldStatistics.VfeMedieval2;

internal static class VfeMedieval2Rules
{
    internal static long HoneyPlaced(int placed, bool playerOwned, bool matchingApiary)
        => placed > 0 && playerOwned && matchingApiary ? placed : 0;
}
