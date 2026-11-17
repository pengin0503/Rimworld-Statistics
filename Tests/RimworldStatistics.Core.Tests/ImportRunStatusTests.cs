namespace RimworldStatistics.Core.Tests;

/// <summary>Issue #337: the Import tab shows the latest scan's own outcome, never an earlier scan's counts as its own.</summary>
public sealed class ImportRunStatusTests
{
    private static ImportResult Result(int read, int failed = 0, bool cancelled = false) =>
        new() { Read = read, Skipped = 1, Failed = failed, Cancelled = cancelled, PresentFiles = Array.Empty<string>() };

    [Fact]
    public void ASecondScan_ShowsNoCountsUntilItFinishes()
    {
        var status = new ImportRunStatus();
        Assert.Equal(ImportRunPhase.NotRun, status.Current.Phase);

        var first = status.Begin();
        status.Complete(first, Result(5));
        Assert.Equal((ImportRunPhase.Completed, 5), (status.Current.Phase, status.Current.Result!.Read));

        var second = status.Begin();
        Assert.Equal(ImportRunPhase.Running, status.Current.Phase);
        Assert.Null(status.Current.Result);
        status.Report(second, new ImportProgress(10, 3, 0, "a.rws"));
        Assert.Equal(3, status.Current.Progress!.Done);
        status.Complete(second, Result(2));
        Assert.Equal((ImportRunPhase.Completed, 2), (status.Current.Phase, status.Current.Result!.Read));
    }

    [Fact]
    public void AFailedScan_ShowsItsErrorAndNotTheEarlierCounts()
    {
        var status = new ImportRunStatus();
        status.Complete(status.Begin(), Result(5));

        var failing = status.Begin();
        status.Report(failing, new ImportProgress(10, 4, 1, "b.rws"));
        var error = new IOException("folder vanished");
        status.Fail(failing, error);
        Assert.Equal(ImportRunPhase.Failed, status.Current.Phase);
        Assert.Same(error, status.Current.Error);
        Assert.Null(status.Current.Result);

        var third = status.Begin();
        Assert.Null(status.Current.Error);
        status.Complete(third, Result(7));
        Assert.Equal((ImportRunPhase.Completed, 7, (Exception?)null),
            (status.Current.Phase, status.Current.Result!.Read, status.Current.Error));
    }

    [Fact]
    public void CancelledScans_ScansWithFailedSaves_AndScansThatCouldNotStart_AreToldApart()
    {
        var status = new ImportRunStatus();
        status.Complete(status.Begin(), Result(3, cancelled: true));
        Assert.Equal(ImportRunPhase.Cancelled, status.Current.Phase);
        Assert.Equal(3, status.Current.Result!.Read);

        status.Complete(status.Begin(), Result(4, failed: 2));
        Assert.Equal(ImportRunPhase.Completed, status.Current.Phase);   // the scan finished; two saves did not read
        Assert.Equal(2, status.Current.Result!.Failed);

        status.Fail(status.Begin(), new UnauthorizedAccessException("saves folder"));
        Assert.Equal(ImportRunPhase.Failed, status.Current.Phase);
    }

    [Fact]
    public void ReportsOfAnEarlierScan_DoNotOverwriteTheLatest()
    {
        var status = new ImportRunStatus();
        var earlier = status.Begin();
        var latest = status.Begin();
        status.Complete(earlier, Result(9));
        status.Fail(earlier, new IOException("late"));
        status.Report(earlier, new ImportProgress(1, 1, 0, null));
        Assert.Equal(ImportRunPhase.Running, status.Current.Phase);
        Assert.Null(status.Current.Progress);

        status.Complete(latest, Result(1));
        status.Fail(latest, new IOException("after completion"));   // a finished scan stays finished
        Assert.Equal((ImportRunPhase.Completed, 1), (status.Current.Phase, status.Current.Result!.Read));
    }
}
