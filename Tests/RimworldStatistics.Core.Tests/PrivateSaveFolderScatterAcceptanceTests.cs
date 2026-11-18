using System.Diagnostics;
using System.Security.Cryptography;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Opt-in acceptance of the cross-colony scatter plot (V2-D) on a local folder of real saves (RWS_PRIVATE_SAVES_DIR).
/// Saves are only read; only counts and timings are printed, never names, identifiers or values.
/// </summary>
public sealed class PrivateSaveFolderScatterAcceptanceTests
{
    [Fact]
    public void PrivateSaveFolder_EveryPointIsTheRankedValueOfBothStatistics()
    {
        var folder = Environment.GetEnvironmentVariable("RWS_PRIVATE_SAVES_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            Assert.Skip("Set RWS_PRIVATE_SAVES_DIR locally; no private saves are shipped to CI.");
        var files = Directory.EnumerateFiles(folder!).Where(p => ImportEngine.IsSaveFile(Path.GetFileName(p)))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(files);
        var before = files.Select(Fingerprint).ToArray();

        using var profile = new TempDir();
        var session = new ProfileSession(new ProfileStore(profile.Path), () => DateTime.UtcNow,
            new FileCheckpointArchive(profile.File("archive")));
        var watch = Stopwatch.StartNew();
        var import = new ImportEngine(new SaveReader()).Run(folder!, session, null, TestContext.Current.CancellationToken);
        var importMs = watch.ElapsedMilliseconds;
        Assert.Equal(0, import.Failed);
        var view = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        Assert.True(view.Stats.Colonies.Count >= 2, "The folder should hold saves of at least two colonies.");

        var keys = LeaderboardBuilder.RankableKeys(view);
        Assert.All(keys, key => Assert.True(MetricScatterPlot.CanPlot(view, key)));
        var ranked = keys.ToDictionary(key => key, key => LeaderboardBuilder.Build(view, key, descending: true)
            .Entries.ToDictionary(e => e.ColonyKey, e => e.Value, StringComparer.Ordinal), StringComparer.Ordinal);

        var buildMs = new List<double>();
        long points = 0, partial = 0, differentDays = 0;
        var reasons = new Dictionary<ScatterExclusionReason, long>();
        foreach (var x in new[] { BasicMetrics.Days, BasicMetrics.PeakWealth })
            foreach (var y in keys)
            {
                watch.Restart();
                var plot = MetricScatterPlot.Build(view, x, y)!;
                buildMs.Add(watch.Elapsed.TotalMilliseconds);

                Assert.Equal(view.Stats.Colonies.Count, plot.ColonyCount);
                Assert.Equal(ranked[x].Keys.Intersect(ranked[y].Keys).Order(StringComparer.Ordinal),
                    plot.Points.Select(p => p.ColonyKey));
                foreach (var point in plot.Points)
                {
                    Assert.Equal(ranked[x][point.ColonyKey], point.X.Number);
                    Assert.Equal(ranked[y][point.ColonyKey], point.Y.Number);
                }
                foreach (var axis in new[] { plot.X, plot.Y })
                    Assert.All(axis.Ticks, t => Assert.True(double.IsFinite(t)));
                Assert.All(plot.Points, p => Assert.InRange(plot.Y.Position(p.Y.Number), 0d, 1d));

                points += plot.Points.Count;
                partial += plot.Points.Count(p => p.HasPartial);
                differentDays += plot.Points.Count(p => p.ObservedAtDifferentTicks);
                foreach (var exclusion in plot.Exclusions)
                    reasons[exclusion.Reason] = reasons.GetValueOrDefault(exclusion.Reason) + 1;
            }

        buildMs.Sort();
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"scatter acceptance: {files.Length} saves, {view.Stats.Colonies.Count} colonies, import {importMs} ms; "
            + $"{buildMs.Count} plots over {keys.Count} statistics, build median {buildMs[buildMs.Count / 2]:F2} ms, max {buildMs[^1]:F2} ms; "
            + $"{points} points ({partial} with a partial value, {differentDays} with values to different days), not plotted: "
            + string.Join(", ", reasons.OrderBy(r => r.Key).Select(r => $"{r.Key} {r.Value}")));
        Assert.InRange(buildMs[^1], 0d, 2000d);

        Assert.Equal(before, files.Select(Fingerprint).ToArray());
    }

    private static (long Length, DateTime Utc, string Sha) Fingerprint(string path)
    {
        var info = new FileInfo(path);
        using var file = File.OpenRead(path);
        return (info.Length, info.LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(file)));
    }
}
