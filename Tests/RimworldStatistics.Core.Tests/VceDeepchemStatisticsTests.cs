using RimworldStatistics.VceDeepchem;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class VceDeepchemStatisticsTests
{
    [Theory]
    [InlineData(1f, true, true, 1d)]
    [InlineData(0.4f, true, true, 0.4d)]
    [InlineData(0f, true, true, 0d)]
    [InlineData(-1f, true, true, 0d)]
    [InlineData(float.NaN, true, true, 0d)]
    [InlineData(float.PositiveInfinity, true, true, 0d)]
    [InlineData(1f, false, true, 0d)]
    [InlineData(1f, true, false, 0d)]
    public void OnlyAcceptedFiniteDeepchemPipeUnitsCount(
        float stored, bool correctPipe, bool playerOwned, double expected)
        => Assert.Equal(expected, VceDeepchemRules.AcceptedUnits(
            stored, correctPipe, playerOwned), 6);
}
