namespace RimworldStatistics.Core.Tests;

public sealed class NumericDlcSnapshotTests
{
    [Fact]
    public void RoyaltyLevelsExcludeInvalidEntriesAndLeaveEmptyPopulationUnknown()
    {
        var m = RoyaltyStateSnapshot.Build(new[] { 0, 3, 6 });
        Assert.Equal(6, m[RoyaltyStateSnapshot.PsylinkPeak].Values[""].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable,
            RoyaltyStateSnapshot.Build(Array.Empty<int>())[RoyaltyStateSnapshot.PsylinkPeak].Values[""].Coverage);
        Assert.Equal(MetricCoverage.Partial,
            RoyaltyStateSnapshot.Build(new[] { 3, -1 })[RoyaltyStateSnapshot.PsylinkPeak].Values[""].Coverage);
    }

    [Fact]
    public void BiotechUsesHomeMapSumAndSingleMechanitorMax()
    {
        var m = BiotechStateSnapshot.Build(new[] { 21, 9 }, new[] { 6, 3, 12 });
        Assert.Equal(30, m[BiotechStateSnapshot.PollutionCellsPeak].Values[""].IntegerValue);
        Assert.Equal(12, m[BiotechStateSnapshot.BandwidthUsedPeak].Values[""].IntegerValue);
        var empty = BiotechStateSnapshot.Build(Array.Empty<int>(), Array.Empty<int>());
        Assert.Equal(0, empty[BiotechStateSnapshot.PollutionCellsPeak].Values[""].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable, empty[BiotechStateSnapshot.BandwidthUsedPeak].Values[""].Coverage);
    }

    [Fact]
    public void DlcSnapshotOutputsStayUnverifiedAndDlcScoped()
    {
        Assert.Equal(AcquisitionUnits.Royalty, MetricRegistry.Find(RoyaltyStateSnapshot.PsylinkPeak)!.RequiredDlc);
        Assert.Equal(AcquisitionUnits.Biotech, MetricRegistry.Find(BiotechStateSnapshot.PollutionCellsPeak)!.RequiredDlc);
        Assert.False(MetricRegistry.Find(BiotechStateSnapshot.PollutionCellsPeak)!.Published);
    }
}
