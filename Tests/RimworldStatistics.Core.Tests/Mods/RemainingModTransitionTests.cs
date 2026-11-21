using RimworldStatistics.Vsie;

namespace RimworldStatistics.Core.Tests;

public sealed class RemainingModTransitionTests
{
    [Theory]
    [InlineData(6, 500f, 6, 600f, true)]
    [InlineData(6, 500f, 7, 10f, true)]
    [InlineData(6, 500f, 6, 500f, false)]
    [InlineData(6, 500f, 6, 499f, false)]
    [InlineData(6, 500f, 5, 900f, false)]
    [InlineData(6, 500f, 6, float.NaN, false)]
    public void TeachingRequiresPositivePupilProgress(
        int beforeLevel, float beforeXp, int afterLevel, float afterXp, bool expected)
        => Assert.Equal(expected, VsieTeachingRules.Progressed(beforeLevel, beforeXp, afterLevel, afterXp));
}
