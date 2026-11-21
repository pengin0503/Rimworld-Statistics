using RimworldStatistics.HospitalityAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class HospitalityPurchaseTests
{
    [Fact]
    public void CountsMergedStacksAndReplacementItemsWithoutSubtractingOldApparel()
    {
        var before = new Dictionary<int, int> { [1] = 3, [2] = 1 };
        var after = new Dictionary<int, int> { [1] = 5, [3] = 1 };
        Assert.Equal((3L, 12L), HospitalityPurchaseMath.Completed(before, after, 25, 13, eligible: true));
    }

    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, 3)]
    public void FailedOrIneligiblePurchasesDoNotRecordSilver(bool eligible, int after)
    {
        Assert.Equal((0L, 0L), HospitalityPurchaseMath.Completed(new Dictionary<int, int> { [1] = 3 },
            new Dictionary<int, int> { [1] = after }, 25, 13, eligible));
    }

    [Fact]
    public void ShoppingEventIdentityIsStableAcrossReplay()
    {
        var id = HospitalityPurchaseMath.EventId(15, 987);
        Assert.Equal(id, HospitalityPurchaseMath.EventId(15, 987));
        Assert.NotEqual(id, HospitalityPurchaseMath.EventId(16, 987));
        Assert.Null(HospitalityPurchaseMath.EventId(-1, 987));
    }
}
