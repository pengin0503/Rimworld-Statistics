namespace RimworldStatistics.VfePirates;

internal static class VfePiratesRules
{
    internal static bool Fitted(bool owned, bool normalCompletedWeld,
        bool newArmor, bool newHelmet, bool newShoulderPads)
        => owned && normalCompletedWeld && newArmor && newHelmet && newShoulderPads;
}
