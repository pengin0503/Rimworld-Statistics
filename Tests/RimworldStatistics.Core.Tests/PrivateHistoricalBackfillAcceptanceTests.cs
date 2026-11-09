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

        var files = Directory.EnumerateFiles(sourceDir)
            .Where(path => ImportEngine.IsSaveFile(Path.GetFileName(path)))
            .ToArray();
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
        session.Flush();

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
    [Fact]
    public void PrivateLegacy17_ExplicitAllianceEvidenceMatchesIndependentXmlAudit_WhenAvailable()
    {
        // Optional single-file ground truth from a voluntarily supplied 1.7
        // original. Public CI does not hold this file. Only aggregate evidence
        // counts and quality states are compared; never publish names or IDs.
        var source = Environment.GetEnvironmentVariable("RWS_REAL_17_LEGACY_SAVE");
        if (string.IsNullOrWhiteSpace(source))
            Assert.Skip("Set RWS_REAL_17_LEGACY_SAVE to the owner's local legacy save copy.");
        Assert.True(File.Exists(source), "Private source save missing.");

        static string Digest(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
        }

        var before = Digest(source);
        var info = new FileInfo(source);
        var originalLength = info.Length;
        var originalWrite = info.LastWriteTimeUtc;
        var parsed = new SaveReader().ReadDetailed(source);
        Assert.Equal(ObservationStatus.Ok, parsed.Observation.Status);
        Assert.Equal("1.7.4995 rev309", parsed.Observation.GameVersion);
        Assert.Equal(348, parsed.Observation.ModCount);
        Assert.Null(parsed.Checkpoint); // archive predates this MOD's checkpoints
        Assert.NotNull(parsed.Metrics);

        var relations = parsed.Metrics!.Metrics[FactionStateSnapshot.Relations].Values;
        Assert.Equal(MetricCoverage.Partial, relations[MetricEntry.Scalar].Coverage);
        Assert.Equal(2L, relations["ally"].IntegerValue);
        Assert.Equal(0L, relations["hostile"].IntegerValue);
        Assert.Equal(MetricEvidence.Experimental, parsed.Metrics.EvidenceByAcquisition!["factions"]);
        var range = parsed.Metrics.Metrics[FactionStateSnapshot.GoodwillRange].Values;
        Assert.Equal(38L, range["min"].IntegerValue);
        Assert.Equal(64L, range["max"].IntegerValue);

        var after = new FileInfo(source);
        Assert.Equal(originalLength, after.Length);
        Assert.Equal(originalWrite, after.LastWriteTimeUtc);
        Assert.Equal(before, Digest(source));
    }

}
