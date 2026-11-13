namespace RimworldStatistics.VqeAncients;

internal static class VqeAncientsRules
{
    internal static bool Rejected(bool playerOwned, bool released, bool comaAdded,
        bool outcomeIsRejection)
        => playerOwned && released && comaAdded && outcomeIsRejection;

    internal static bool Mutated(bool playerOwned, bool originalDestroyed,
        bool spawnedNewPawn, bool expectedPawnKind)
        => playerOwned && originalDestroyed && spawnedNewPawn && expectedPawnKind;
}
