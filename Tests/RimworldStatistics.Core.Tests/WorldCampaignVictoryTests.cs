using RimworldStatistics.WorldCampaigns;

namespace RimworldStatistics.Core.Tests;

public sealed class WorldCampaignVictoryTests
{
    [Fact]
    public void OnlyAConfirmedTransitionRecordsOneVictoryPerCampaignCheckpoint()
    {
        var state = new FamilyState();
        var earlier = state.Clone();
        Assert.False(WorldCampaignVictoryRules.Record(state, false, false));
        Assert.False(WorldCampaignVictoryRules.Record(state, true, true));
        Assert.True(WorldCampaignVictoryRules.Record(state, false, true));
        Assert.False(WorldCampaignVictoryRules.Record(state.Clone(), false, true));
        Assert.True(WorldCampaignVictoryRules.Record(earlier, false, true));
        Assert.Equal(1, state.Peek("completed")!.N);
    }
}
