using RimworldStatistics.HospitalityAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class HospitalityAssignedBedTests
{
    [Fact]
    public void TwoGuestsSharingOneBedCountOneAssignedBed()
    {
        var count = new HospitalityAssignedBedCounter();
        count.MarkObservedMap();
        var bed = new object();
        Assert.True(count.Add(bed));
        Assert.True(count.Add(bed));
        Assert.True(count.Add(new object()));
        Assert.Equal(2, count.Value);
    }

    [Fact]
    public void NoPresentGuestsMeansZeroAssignedBedsWhenHomeMapWasObserved()
    {
        var count = new HospitalityAssignedBedCounter();
        count.MarkObservedMap();
        Assert.Equal(0, count.Value);
    }

    [Fact]
    public void MissingHomeMapOrCorruptGuestBedMeansUnknown()
    {
        var count = new HospitalityAssignedBedCounter();
        Assert.Null(count.Value);
        count.MarkObservedMap();
        Assert.False(count.Add(null));
        Assert.Null(count.Value);
    }
}
