namespace RimworldStatistics.AlphaAnimals;

/// <summary>
/// Validates Alpha Animals larvae/swarmling cocoon metamorphoses and cocoon
/// hatchings (separate from PawnUtility.TrySpawnHatchedOrBornPawn and VFE Insectoids hives).
/// </summary>
internal static class AlphaAnimalsRules
{
    internal static bool MetamorphosisCommitted(
        bool sourceSpawnedAndPlayer,
        bool spawnedValid,
        bool sameMap,
        bool parentDestroyed,
        bool expectedCocoonDef)
        => sourceSpawnedAndPlayer && spawnedValid && sameMap && parentDestroyed && expectedCocoonDef;

    internal static bool CocoonHatchCommitted(
        bool playerCocoonBefore,
        bool cocoonDestroyed,
        bool livePawnSpawned,
        bool playerPawnSpawned,
        bool sameMap)
        => playerCocoonBefore && cocoonDestroyed && livePawnSpawned && playerPawnSpawned && sameMap;
}
