namespace RimworldStatistics.VfeSettlers;

internal static class VfeSettlersBountyRules
{
    internal static long RewardReceived(bool wantedWasPresent, bool wantedRemoved,
        bool playerCaravan, long rewardOffered, long silverAdded)
    {
        if (!wantedWasPresent || !wantedRemoved || !playerCaravan ||
            rewardOffered <= 0 || silverAdded <= 0) return 0;
        return Math.Min(rewardOffered, silverAdded);
    }

    internal sealed class FirstTransfer
    {
        private bool consumed;
        internal bool ConsumeFirst()
        {
            if (consumed) return false;
            consumed = true;
            return true;
        }
    }
}
