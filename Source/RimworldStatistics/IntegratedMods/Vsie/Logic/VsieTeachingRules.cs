namespace RimworldStatistics.Vsie;

internal static class VsieTeachingRules
{
    internal static bool Progressed(int beforeLevel, float beforeXp, int afterLevel, float afterXp)
        => afterLevel > beforeLevel || afterLevel == beforeLevel && afterXp > beforeXp;
}
