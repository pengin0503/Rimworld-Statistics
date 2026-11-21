using RimworldStatistics.WinstonWave;

namespace RimworldStatistics.Core.Tests;

public sealed class WinstonWaveTests
{
    [Fact]
    public void ARealClearCountsOncePerWaveAndMapAcrossReload()
    {
        var state = new FamilyState();
        Assert.False(WinstonWaveRules.RecordClear(state, "Map_1", 4, 5, sent: true, dispatchedPawns: 5, enemiesLeft: 2));
        Assert.False(WinstonWaveRules.RecordClear(state, "Map_1", 5, 6, sent: false, dispatchedPawns: 5, enemiesLeft: 0));
        Assert.False(WinstonWaveRules.RecordClear(state, "Map_1", 5, 6, sent: true, dispatchedPawns: 0, enemiesLeft: 0));
        Assert.False(WinstonWaveRules.RecordClear(state, "Map_1", 5, 5, sent: true, dispatchedPawns: 5, enemiesLeft: 0));
        Assert.True(WinstonWaveRules.RecordClear(state, "Map_1", 5, 6, sent: true, dispatchedPawns: 5, enemiesLeft: 0));

        var restored = state.Clone();
        Assert.False(WinstonWaveRules.RecordClear(restored, "Map_1", 5, 6, sent: true, dispatchedPawns: 5, enemiesLeft: 0));
        Assert.True(WinstonWaveRules.RecordClear(restored, "Map_2", 2, 3, sent: true, dispatchedPawns: 4, enemiesLeft: 0));
        Assert.Equal(2, restored.Peek("cleared")!.N);
        Assert.Equal(5, restored.Peek("cleared")!.Max);
        Assert.Equal(1, restored.Peek("boss")!.N);
        Assert.Equal(1, state.Peek("cleared")!.N);
    }
}
