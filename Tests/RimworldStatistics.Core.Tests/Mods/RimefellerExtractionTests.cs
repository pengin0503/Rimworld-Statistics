using RimworldStatistics.Rimefeller;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class RimefellerExtractionTests
{
    [Theory]
    [InlineData(100d, 80d, true, 20d)]
    [InlineData(1d, 0.75d, true, 0.25d)]
    [InlineData(5d, 5d, true, 0d)]
    [InlineData(5d, 6d, true, 0d)]
    [InlineData(100d, 80d, false, 0d)]
    [InlineData(0d, 0d, true, 0d)]
    [InlineData(double.NaN, 1d, true, 0d)]
    [InlineData(double.PositiveInfinity, 1d, true, 0d)]
    [InlineData(2d, double.NaN, true, 0d)]
    [InlineData(2d, double.NegativeInfinity, true, 0d)]
    [InlineData(2d, -1d, true, 0d)]
    public void OnlyActualPlayerOilFieldDepletionCounts(double before, double after,
        bool playerOwnedPump, double expected)
        => Assert.Equal(expected, RimefellerExtractionRules.ExtractedFieldUnits(
            before, after, playerOwnedPump), 8);

    [Fact]
    public void RepeatedNoOpPumpsDoNotAddToPreviousObservedExtraction()
    {
        var state = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 600, Continuous = true };
        Assert.True(OptionalTransitionRecording.RecordReal(state, "extracted", 
            RimefellerExtractionRules.ExtractedFieldUnits(10, 9.5, true)));
        Assert.False(OptionalTransitionRecording.RecordReal(state, "extracted",
            RimefellerExtractionRules.ExtractedFieldUnits(9.5, 9.5, true)));
        Assert.True(OptionalTransitionRecording.RecordReal(state, "extracted",
            RimefellerExtractionRules.ExtractedFieldUnits(9.5, 9.25, true)));
        Assert.Equal(0.75d, state.Peek("extracted")!.Sum!.Value, 8);
    }
}
