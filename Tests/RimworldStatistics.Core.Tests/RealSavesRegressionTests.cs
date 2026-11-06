using System.Diagnostics;
using System.Security.Cryptography;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Opt-in check against the author's 49 real saves (spec §11.1-2). Set RWSTATS_REAL_SAVES_DIR to the folder
/// fetched by tools/fetch_dev_inputs.py; without it the test is skipped. Expected values are research §7's.
/// </summary>
public sealed class RealSavesRegressionTests
{
    [Fact]
    public void RealSaves_MatchResearchTotals()
    {
        var dir = Environment.GetEnvironmentVariable("RWSTATS_REAL_SAVES_DIR");
        if (dir is null)
            Assert.Skip("RWSTATS_REAL_SAVES_DIR is not set");
        var ct = TestContext.Current.CancellationToken;
        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => DateTime.UtcNow);
        var hashesBefore = Directory.EnumerateFiles(dir).ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

        var sw = Stopwatch.StartNew();
        var r = new ImportEngine(new SaveReader()).Run(dir, session, null, ct);
        sw.Stop();
        TestContext.Current.SendDiagnosticMessage($"import {r.Read} files in {sw.Elapsed.TotalSeconds:F1}s");

        Assert.Equal(49, r.Read);
        Assert.Equal(0, r.Failed);
        var s = session.GetView(new StatsOptions()).Stats;
        Assert.Equal(33, s.ColonyCount);
        Assert.Equal(135723149L, s.TotalTicks);
        Assert.Equal(950114.07, s.TotalRealPlay, 0);
        Assert.Equal((190, 311, 29, 0), (s.Raids, s.Threats, s.Killed, s.TotalDeparted));
        Assert.Equal(4364583.06, s.PeakWealthSum, 0);
        Assert.Equal(4294287.26, s.LastWealthSum, 0);
        Assert.Equal(20835213L, s.LongestTicks);
        Assert.Equal(925789.69, s.SingleMaxWealth, 0);
        Assert.Equal(20, s.MaxPopulation);
        Assert.Equal(0, s.TotalEndings);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60));

        var reread = new ImportEngine(new SaveReader()).Run(dir, session, null, ct);
        Assert.Equal((0, 49), (reread.Read, reread.Skipped));
        foreach (var (f, h) in hashesBefore)   // acceptance 9: saves are only read, never changed
            Assert.Equal(h, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
    }
}
