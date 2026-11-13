namespace RimworldStatistics.VreAndroid;

internal static class VreAndroidAwakeningRules
{
    internal static bool Completed(bool before, bool after, bool player, bool naturalTick)
        => !before && after && player && naturalTick;
}
