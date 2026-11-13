using RimworldStatistics.VfeSecurity;

namespace RimworldStatistics.Core.Tests;

public sealed class IntegratedOptionalTransitionTests
{
    private const string FamilyId = "mod.vanillaexpanded.vfesecurity.traprearm";
    private const string MetricId = FamilyId + ".completed";

    private static FamilyState EmptyState() => new()
    {
        Revision = 1, SinceTick = 0, ThroughTick = 100, Continuous = true,
    };

    [Fact]
    public void FourThousandNinetySevenConfirmedActions_DoNotHitSdkEventIdCap()
    {
        var state = EmptyState();
        for (var n = 0; n < 4097; n++)
            Assert.True(OptionalTransitionRecording.RecordCount(state, "completed"));

        var metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(state, 100, metrics),
            "completed", MetricId);
        Assert.Equal(4097L, metrics[MetricId].Values[MetricEntry.Scalar].IntegerValue);
        Assert.False(state.Peek("completed")!.Overflow);
    }

    [Fact]
    public void TwoOwnersWithSameDefName_AreSeparateBreakdownEntries()
    {
        var state = EmptyState();
        var first = OptionalTransitionRecording.QualifiedDefKey("Example.OwnerA", "SameName");
        var second = OptionalTransitionRecording.QualifiedDefKey("Example.OwnerB", "SameName");
        Assert.NotEqual(first, second);
        Assert.True(OptionalTransitionRecording.RecordCount(state, "items", 3, first, true));
        Assert.True(OptionalTransitionRecording.RecordCount(state, "items", 2, second, true));

        var output = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(state, 100, output),
            "items", MetricId, withBreakdown: true);

        Assert.Equal(5L, output[MetricId].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(3L, output[MetricId].Values[first!].IntegerValue);
        Assert.Equal(2L, output[MetricId].Values[second!].IntegerValue);
    }

    [Fact]
    public void MissingBreakdownOwnership_RetainsTotalAndWeakensCoverage()
    {
        var state = EmptyState();
        Assert.Null(OptionalTransitionRecording.QualifiedDefKey(null, "SameName"));
        Assert.True(OptionalTransitionRecording.RecordCount(state, "items", 2,
            key: null, withBreakdown: true));

        var output = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(state, 100, output),
            "items", MetricId, withBreakdown: true);

        Assert.Equal(2L, output[MetricId].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, output[MetricId].Values[MetricEntry.Scalar].Coverage);
        Assert.Single(output[MetricId].Values);
    }

    [Fact]
    public void InvalidTransitionDoesNotCreateEventOrStoredQuantity()
    {
        var state = EmptyState();
        Assert.False(OptionalTransitionRecording.RecordCount(state, "items", 0));
        Assert.False(OptionalTransitionRecording.RecordCount(state, "items", -1));
        Assert.False(OptionalTransitionRecording.RecordReal(state, "volume", double.NaN));
        Assert.False(OptionalTransitionRecording.RecordReal(state, "volume", double.PositiveInfinity));
        Assert.False(OptionalTransitionRecording.RecordReal(state, "volume", -1));
        Assert.Null(state.Peek("items"));
        Assert.Null(state.Peek("volume"));

        Assert.True(OptionalTransitionRecording.RecordReal(state, "volume", 0.4));
        Assert.True(OptionalTransitionRecording.RecordReal(state, "volume", 0.6));
        Assert.Equal(1d, state.Peek("volume")!.Sum);
    }

    [Theory]
    [InlineData(false, true, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    public void TrapRearmOnlyCountsRealPlayerUnarmedToArmedTransition(
        bool previous, bool next, bool eligible, bool expected)
    {
        Assert.Equal(expected, VfeSecurityRearmPredicate.Completed(previous, next, eligible));
    }
}
