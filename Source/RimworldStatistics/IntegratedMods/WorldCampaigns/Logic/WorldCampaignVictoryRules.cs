using RimworldStatistics.Core;

namespace RimworldStatistics.WorldCampaigns;

internal static class WorldCampaignVictoryRules
{
    internal static bool Record(FamilyState state, bool before, bool after)
        => !before && after && (state.Peek("completed")?.N ?? 0) == 0 &&
            OptionalTransitionRecording.RecordCount(state, "completed");
}
