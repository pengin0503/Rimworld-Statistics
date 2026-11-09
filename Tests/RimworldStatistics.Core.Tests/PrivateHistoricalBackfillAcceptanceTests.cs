using System.Diagnostics;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Optional field-acceptance gate for historical backfill on locally available
/// originals. Never copies, edits, uploads or logs names, identifiers, MOD lists,
/// profile JSON, or private .rws bytes.
/// </summary>
public sealed class PrivateHistoricalBackfillAcceptanceTests
{
    [Fact]
    public void PrivateSaveDirectory_ReadOnlyReimportAndPartialEvidence_WhenAvailable()
    {
        var sourceDir = Environment.GetEnvironmentVariable("RWS_PRIVATE_SAVES_DIR");
        if (string.IsNullOrWhiteSpace(sourceDir))
            Assert.Skip("Set RWS_PRIVATE_SAVES_DIR locally; no private saves are shipped to CI.");
        Assert.True(Directory.Exists(sourceDir), "Private save folder does not exist.");

        var files = Directory.GetFiles(sourceDir, "*.rws", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(files);
        var metadata = files.Select(path =>
        {
            var info = new FileInfo(path);
            return (Path: path, info.Length, info.LastWriteTimeUtc);
        }).ToArray();

        using var temp = new TempDir();
        var session = new ProfileSession(new ProfileStore(temp.Path), () => DateTime.UtcNow);
        var importer = new ImportEngine(new SaveReader());

        var clock = Stopwatch.StartNew();
        var first = importer.Run(sourceDir, session, null, TestContext.Current.CancellationToken);
        var coldMs = clock.Elapsed.TotalMilliseconds;
        Assert.Equal(0, first.Failed);
        Assert.Equal(files.Length, first.Read + first.Skipped);
        clock.Restart();
        var second = importer.Run(sourceDir, session, null, TestContext.Current.CancellationToken);
        var warmMs = clock.Elapsed.TotalMilliseconds;
        Assert.Equal(0, second.Failed);
        Assert.Equal(0, second.Read);
        Assert.Equal(files.Length, second.Skipped);

        var totals = session.Read(profile =>
        {
            var saveFile = profile.MetricObservations.Where(o =>
                !o.IsQuarantined && o.Source == MetricObservationSource.SaveFile).ToArray();
            int partialFactionReadings = 0;
            foreach (var observation in saveFile)
            {
                if (observation.ModSetHash is { } digest)
                    Assert.Matches("^[0-9a-f]{64}$", digest);
                if (!observation.Metrics.TryGetValue(FactionStateSnapshot.Relations, out var relation))
                    continue;
                partialFactionReadings++;
                Assert.Equal(MetricCoverage.Partial, relation.Values[MetricEntry.Scalar].Coverage);
                Assert.Equal(MetricEvidence.Experimental,
                    observation.EvidenceByAcquisition!["factions"]);
                if (observation.Metrics.TryGetValue(FactionStateSnapshot.GoodwillRange, out var goodwill))
                    foreach (var value in goodwill.Values.Where(kv => kv.Key != MetricEntry.Scalar))
                        Assert.Equal(MetricCoverage.Partial, value.Value.Coverage);
            }
            var intervals = HistoricalCounterIntervals.Evaluate(profile);
            Assert.All(intervals, row => Assert.Equal(MetricCoverage.Partial, row.Coverage));
            return (Observed: saveFile.Length, Factions: partialFactionReadings, Intervals: intervals.Count);
        });

        foreach (var saved in metadata)
        {
            var info = new FileInfo(saved.Path);
            Assert.Equal(saved.Length, info.Length);
            Assert.Equal(saved.LastWriteTimeUtc, info.LastWriteTimeUtc);
        }
        // Only aggregate non-identifying counts and elapsed times are emitted.
        // Absence of a faction reading is not a success of value correctness.
        TestContext.Current.SendDiagnosticMessage(
            $"Private archive read-only acceptance: files={files.Length}, firstRead={first.Read}, " +
            $"skippedOnRepeat={second.Skipped}, retainedSaveObservations={totals.Observed}, " +
            $"factionPartial={totals.Factions}, lineageIntervals={totals.Intervals}, " +
            $"coldMs={coldMs:F0}, warmMs={warmMs:F0}; original file metadata unchanged.");
    }
}
