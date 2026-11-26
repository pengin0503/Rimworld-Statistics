namespace RimworldStatistics.Core.Tests;

/// <summary>U20: the title-screen menu label shows import progress only while a scan runs.</summary>
public sealed class ImportMenuLabelTests
{
    [Fact]
    public void Format_NoScan_IsExactlyTheBaseText()
    {
        var state = new ImportRunState(ImportRunPhase.NotRun, null, null, null);
        Assert.Equal("Lifetime stats", ImportMenuLabel.Format("Lifetime stats", state));
        Assert.Equal("Lifetime stats", ImportMenuLabel.Format("Lifetime stats", null, running: false));
    }

    [Fact]
    public void Format_Running_ShowsDoneOfTotal()
    {
        var state = new ImportRunState(ImportRunPhase.Running, null, null, new ImportProgress(10, 3, 0, "a.rws"))
        {
            Mode = ImportMode.Incremental,
        };
        Assert.Equal("Lifetime stats (3/10)", ImportMenuLabel.Format("Lifetime stats", state));
        Assert.Equal("Lifetime stats (3/10)", ImportMenuLabel.Format("Lifetime stats", state.Progress, running: true));
    }

    [Fact]
    public void Format_RunningBeforeTheFirstReport_ShowsZeroOfZero()
    {
        var state = new ImportRunState(ImportRunPhase.Running, null, null, null);
        Assert.Equal("Lifetime stats (0/0)", ImportMenuLabel.Format("Lifetime stats", state));
    }

    [Fact]
    public void Format_Finished_IsExactlyTheBaseText()
    {
        var result = new ImportResult { Mode = ImportMode.Incremental, Read = 2, PresentFiles = [] };
        var state = new ImportRunState(ImportRunPhase.Completed, result, null, new ImportProgress(2, 2, 0, null));
        Assert.Equal("Lifetime stats", ImportMenuLabel.Format("Lifetime stats", state));
    }
}
