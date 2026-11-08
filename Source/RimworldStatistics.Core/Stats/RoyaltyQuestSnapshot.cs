namespace RimworldStatistics.Core;

/// <summary>
/// Phase D2 Royalty L-003: the retained quests (S-001's population) that ended in success after being accepted and that the game
/// itself shows as related to the Empire, counted once at each successful save. "Related" is exactly what the Quests tab lists
/// (1.7.4985 <c>MainTabWindow_Quests.DoFactionInfo</c>): a non-hidden, non-player faction of <c>Quest.InvolvedFactions</c>, unless
/// the quest's root hides that information (<c>hideInvolvedFactionsInfo</c>); here, a faction of the Empire's def. Hidden quests
/// are not listed by the game and are not counted. Revision 2 replaces the earlier allow-list of quest roots read from save
/// files, which could only give a lower bound.
/// </summary>
public static class RoyaltyQuestSnapshot
{
    public const string Success = "royalty.quest_success";

    /// <param name="successes">Accepted, successfully ended, explicitly Empire-related retained quests.</param>
    /// <param name="partial">Some quest could not be read; the count is then a lower bound.</param>
    public static Dictionary<string, MetricEntry> Build(int successes, bool partial)
        => new(StringComparer.Ordinal)
        {
            [Success] = MetricEntry.Of(MetricKeys.Revision(Success), new MetricValue
            {
                Coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly,
                IntegerValue = successes,
            }),
        };
}
