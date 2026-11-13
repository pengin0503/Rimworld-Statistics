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

    [Fact]
    public void PositiveFractionalPipeDepositsAccumulateWithoutIntegerRounding()
    {
        var state = new FamilyState
            { Revision = 1, SinceTick = 0, ThroughTick = 10, Continuous = true };
        OptionalTransitionRecording.RecordReal(state, "stored", 0.4);
        OptionalTransitionRecording.RecordReal(state, "stored", 0.6);
        Assert.Equal(1d, state.Peek("stored")!.Sum!.Value, 8);
    }
}
