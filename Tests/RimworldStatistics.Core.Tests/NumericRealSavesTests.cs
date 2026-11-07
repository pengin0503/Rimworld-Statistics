using System.Security.Cryptography;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Opt-in Phase A check against the author's 49 real saves (RWSTATS_REAL_SAVES_DIR, as RealSavesRegressionTests).
/// Only anonymous aggregates are asserted or reported; nothing from the saves is stored in the repository.
/// </summary>
public sealed class NumericRealSavesTests
{
    [Fact]
    public void RealSaves_GiveNumericObservations_WithoutChangingV1()
    {
        var dir = Environment.GetEnvironmentVariable("RWSTATS_REAL_SAVES_DIR");
        if (dir is null)
            Assert.Skip("RWSTATS_REAL_SAVES_DIR is not set");
        var ct = TestContext.Current.CancellationToken;
        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => DateTime.UtcNow);
        var hashesBefore = Directory.EnumerateFiles(dir).ToDictionary(f => f, f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));

        var r = new ImportEngine(new SaveReader()).Run(dir, session, null, ct);
        Assert.Equal((49, 0), (r.Read, r.Failed));

        // v1 totals are those of RealSavesRegressionTests.
        var s = session.GetView(new StatsOptions()).Stats;
        Assert.Equal((33, 135723149L, 190L, 29L), (s.ColonyCount, s.TotalTicks, s.Raids, s.Killed));
        Assert.Equal(4364583.06, s.PeakWealthSum, 0);

        var metrics = session.Read(p => p.MetricObservations.ToList());
        // Earlier saves superseded by a later one of the same identity are dropped; every identity keeps its latest.
        var identities = session.Read(p => p.SaveObservations.Where(o => o.Status == ObservationStatus.Ok)
            .Select(o => o.Identity.FingerprintHash() + "|" + string.Join(",", o.Identity.StartingPawnIds.Order(StringComparer.Ordinal))).ToHashSet());
        Assert.Equal(identities, metrics.Select(m => m.Identity.FingerprintHash() + "|" + string.Join(",", m.Identity.StartingPawnIds.Order(StringComparer.Ordinal))).ToHashSet());
        Assert.True(metrics.Sum(m => m.FileAliases.Count) <= 49);
        TestContext.Current.SendDiagnosticMessage($"{metrics.Count} numeric observations for {identities.Count} identities");
        Assert.All(metrics, m => Assert.False(m.IsQuarantined));
        Assert.All(metrics, m => Assert.Equal(64, m.Key.Length));

        // Every real save is start-aligned (1.5, 1.6 and 1.7): time outputs exist for all seven series.
        foreach (var m in metrics)
        {
            foreach (var key in new[]
            {
                MetricKeys.WealthTotalFirst, MetricKeys.WealthTotalPeakDay, MetricKeys.WealthTotalLongestGrowth,
                MetricKeys.WealthItemsLatest, MetricKeys.WealthBuildingsLatest, MetricKeys.WealthPawnsLatest,
                MetricKeys.FreeColonistsMean, MetricKeys.PrisonersPeak, MetricKeys.MoodMean,
                MetricKeys.RaidsPerYear, MetricKeys.DeathsPerYear, MetricKeys.ModsLatest, MetricKeys.QuestAccepted,
            })
            {
                if (key == MetricKeys.WealthTotalLongestGrowth && m.SaveTick < HistoryMetricReader.TicksPerPoint)
                    continue;   // one point: no interval yet
                Assert.True(m.Metrics.ContainsKey(key), $"{key} missing in a {HistoryMetricReader.MajorMinor(m.GameVersion)} save");
            }
        }
        var byVersion = metrics.GroupBy(m => HistoryMetricReader.MajorMinor(m.GameVersion)).OrderBy(g => g.Key)
            .Select(g => $"{g.Key}: {g.Count()} observations, partial mood {g.Count(m => m.Metrics[MetricKeys.MoodMean].Values[""].Coverage == MetricCoverage.Partial)}");
        TestContext.Current.SendDiagnosticMessage(string.Join("; ", byVersion));

        // Every numeric observation is attached to one of the 33 v1 colonies, and every colony has one.
        var view = session.GetView(new StatsOptions());
        Assert.Equal(0, view.Numeric.Unattributed);
        Assert.Equal(33, view.Numeric.Colonies.Count);
        Assert.All(view.Numeric.Colonies, c => Assert.True(c.Observations > 0));
        var accepted = view.Numeric.Overall.Single(r => r.Definition.Key == MetricKeys.QuestAccepted);
        Assert.Equal((33, 33), (accepted.Targets, accepted.Valid));
        var sw = System.Diagnostics.Stopwatch.StartNew();
        MetricCalculator.BuildView(session.Read(p => p), view.Colonies, new StatsOptions());
        TestContext.Current.SendDiagnosticMessage($"numeric view built in {sw.Elapsed.TotalMilliseconds:F1} ms");

        // The profile round-trips and the saves were only read.
        var json = ProfileJson.Serialize(session.Read(p => p));
        Assert.Equal(metrics.Count, ProfileJson.Deserialize(json).MetricObservations.Count);
        var reread = new ImportEngine(new SaveReader()).Run(dir, session, null, ct);
        Assert.Equal((0, 49), (reread.Read, reread.Skipped));
        foreach (var (f, h) in hashesBefore)
            Assert.Equal(h, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
    }
}
