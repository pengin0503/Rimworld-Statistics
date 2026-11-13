using RimworldStatistics.VfeEmpire;

namespace RimworldStatistics.Core.Tests;

public sealed class VfeEmpireStatisticsTests
{
    private sealed class Honor(int id)
    {
        public int Id { get; } = id;
    }

    [Fact]
    public void OnlyHonorsMovedFromPendingToBestowedCount()
    {
        var existing = new Honor(1);
        var newlyBestowedA = new Honor(2);
        var newlyBestowedB = new Honor(3);
        var unrelated = new Honor(4);
        var before = new[] { newlyBestowedA, newlyBestowedB };
        var afterPending = Array.Empty<Honor>();
        var afterHonors = new[] { existing, newlyBestowedA, newlyBestowedB, unrelated };
        Assert.Equal(2, VfeEmpireRules.Committed(before, afterPending, afterHonors).Count);
    }

    [Fact]
    public void PendingHonorThatRemainsPendingIsNotCounted()
    {
        var honor = new Honor(1);
        Assert.Empty(VfeEmpireRules.Committed([honor], [honor], [honor]));
        Assert.Empty(VfeEmpireRules.Committed([honor], [], []));
    }
}
