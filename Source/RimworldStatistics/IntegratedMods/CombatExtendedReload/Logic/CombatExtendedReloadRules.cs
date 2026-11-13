namespace RimworldStatistics.CombatExtendedReload;

internal static class CombatExtendedReloadRules
{
    internal static bool AmmoAdded(int stackBefore, int stackAfter, bool ammoDestroyed,
        int magazineAfter)
        => stackBefore > 0 && (ammoDestroyed || stackAfter < stackBefore) && magazineAfter > 0;

    internal sealed class ReloadCounter
    {
        private bool loaded;
        private bool counted;

        internal void Observe(bool successful) => loaded |= successful;
        internal bool CountOnce()
        {
            if (counted || !loaded) return false;
            counted = true;
            return true;
        }
    }
}
