using RimworldStatistics.Rimefeller;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class RimefellerFuelConsumptionTests
{
    [Theory]
    [InlineData(100d, 95d, true, 5d)]
    [InlineData(12.5d, 12.25d, true, 0.25d)]
    [InlineData(100d, 100d, true, 0d)]
    [InlineData(100d, 102d, true, 0d)]
    [InlineData(100d, 90d, false, 0d)]
    [InlineData(-2d, -4d, true, 0d)]
    [InlineData(double.NaN, 10d, true, 0d)]
    [InlineData(100d, double.PositiveInfinity, true, 0d)]
    public void OnlyActualFuelDebitsFromPlayerConsumersCount(double before, double after,
        bool owned, double expected)
        => Assert.Equal(expected, RimefellerFuelUsageRules.Debited(
            before, after, owned), 8);

    [Fact]
    public void SeparateRefineryAndPowerGenerationConsumptionAccumulate()
    {
        var state = new FamilyState { Revision=1, SinceTick=0, ThroughTick=600, Continuous=true };
        Assert.True(OptionalTransitionRecording.RecordReal(state, "refinery_fuel",
            RimefellerFuelUsageRules.Debited(10, 8.5, true)));
        Assert.True(OptionalTransitionRecording.RecordReal(state, "plant_fuel",
            RimefellerFuelUsageRules.Debited(30, 29.25, true)));
        Assert.False(OptionalTransitionRecording.RecordReal(state, "refinery_fuel",
            RimefellerFuelUsageRules.Debited(8.5, 8.5, true)));
        Assert.Equal(1.5, state.Peek("refinery_fuel")!.Sum!.Value, 8);
        Assert.Equal(0.75, state.Peek("plant_fuel")!.Sum!.Value, 8);
    }
}