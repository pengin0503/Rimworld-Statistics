using RimworldStatistics.PopularModOutcomes;

namespace RimworldStatistics.Core.Tests;

public sealed class PopularOutcomeTests
{
    [Theory]
    [InlineData(.4f, .44f, true)]
    [InlineData(1f, 1f, false)]
    [InlineData(1f, 1.04f, false)]
    [InlineData(.4f, .39f, false)]
    [InlineData(float.NaN, .4f, false)]
    [InlineData(.4f, float.PositiveInfinity, false)]
    public void SprinklerCountsOnlyPositiveAppliedGrowth(float before, float after, bool expected)
        => Assert.Equal(expected, PopularOutcomeRules.GrowthApplied(before, after));

    [Fact]
    public void LandformsCountEachSavedHomeOnceAndPreserveMissingEvidence()
    {
        var entry = PopularOutcomeRules.Landforms([
            (1, ["Cove", "Cove", "River"]),
            (1, ["Cove", "River"]),
            (2, ["Cove"]),
            (3, null),
            (4, []),
        ])!;
        Assert.Equal(3, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2, entry.Values["m00nl1ght.geologicallandforms/Cove"].IntegerValue);
        Assert.Equal(1, entry.Values["m00nl1ght.geologicallandforms/River"].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, entry.Values[MetricEntry.Scalar].Coverage);
        Assert.Null(PopularOutcomeRules.Landforms([(1, null)]));
    }
}
