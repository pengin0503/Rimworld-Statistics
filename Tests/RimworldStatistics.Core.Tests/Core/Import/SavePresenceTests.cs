namespace RimworldStatistics.Core.Tests;

/// <summary>S7: current save records are present on disk or archived.</summary>
public sealed class SavePresenceTests
{
    private static SaveObservation Record(string name, bool superseded = false, ObservationStatus status = ObservationStatus.Ok)
        => new() { FileName = name, Superseded = superseded, Status = status };

    [Fact]
    public void Classify_CountsPresentAndMissingFiles()
    {
        var profile = new Profile();
        profile.SaveObservations.Add(Record("present.rws"));
        profile.SaveObservations.Add(Record("gone.rws"));
        var summary = SavePresence.Classify(profile, new[] { "present.rws" });
        Assert.Equal((2, 1, 1), (summary.Total, summary.Present, summary.Archived));
        Assert.Equal(new[] { "gone.rws" }, summary.MissingFiles);
        Assert.Equal(1, summary.Missing);
    }

    [Fact]
    public void Classify_IgnoresSupersededHistory()
    {
        var profile = new Profile();
        profile.SaveObservations.Add(Record("current.rws"));
        profile.SaveObservations.Add(Record("old.rws", superseded: true));
        var summary = SavePresence.Classify(profile, Array.Empty<string>());
        Assert.Equal((1, 0, 1), (summary.Total, summary.Present, summary.Archived));
        Assert.Equal(new[] { "current.rws" }, summary.MissingFiles);
    }

    [Fact]
    public void Classify_DuplicateCurrentNamesCountOnce()
    {
        var profile = new Profile();
        profile.SaveObservations.Add(Record("same.rws"));
        profile.SaveObservations.Add(Record("same.rws"));
        var summary = SavePresence.Classify(profile, new[] { "same.rws" });
        Assert.Equal((1, 1, 0), (summary.Total, summary.Present, summary.Archived));
        Assert.Empty(summary.MissingFiles);
    }

    [Fact]
    public void Classify_MissingNamesAreSortedOrdinal()
    {
        var profile = new Profile();
        profile.SaveObservations.Add(Record("b.rws"));
        profile.SaveObservations.Add(Record("a.rws"));
        profile.SaveObservations.Add(Record("C.rws"));
        var summary = SavePresence.Classify(profile, null);
        Assert.Equal(new[] { "C.rws", "a.rws", "b.rws" }, summary.MissingFiles);
    }

    [Fact]
    public void Classify_FailedCurrentRecordIsCountedLikeAnyOtherRecord()
    {
        var profile = new Profile();
        profile.SaveObservations.Add(Record("bad.rws", status: ObservationStatus.Error));
        var summary = SavePresence.Classify(profile, Array.Empty<string>());
        Assert.Equal((1, 0, 1), (summary.Total, summary.Present, summary.Archived));
    }
}
