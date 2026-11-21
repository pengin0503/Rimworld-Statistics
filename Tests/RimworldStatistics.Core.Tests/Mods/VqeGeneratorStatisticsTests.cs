using RimworldStatistics.VqeGenerator;

namespace RimworldStatistics.Core.Tests;

public sealed class VqeGeneratorStatisticsTests
{
    [Theory]
    [InlineData(true, true, true, true, true)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    [InlineData(true, true, false, true, false)]
    [InlineData(true, true, true, false, false)]
    public void CountsOnlyCommittedFrameReplacement(bool completedFrame,
        bool originalDestroyed, bool replacementPresent, bool distinctDefinition, bool expected)
        => Assert.Equal(expected, VqeGeneratorRules.Upgraded(completedFrame,
            originalDestroyed, replacementPresent, distinctDefinition));
}
