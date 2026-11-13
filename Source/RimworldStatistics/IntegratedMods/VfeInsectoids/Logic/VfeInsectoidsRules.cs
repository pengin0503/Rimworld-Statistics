namespace RimworldStatistics.VfeInsectoids;

/// <summary>Requiring a real world spawn avoids counting a hive list append as birth.</summary>
internal static class VfeInsectoidsRules
{
    internal static bool NewBirth(int before, int after, bool pawnSpawned,
        bool playerOwned, bool sourceMap, bool normalTickScope)
        => normalTickScope && playerOwned && sourceMap && pawnSpawned && after == before + 1;
}
