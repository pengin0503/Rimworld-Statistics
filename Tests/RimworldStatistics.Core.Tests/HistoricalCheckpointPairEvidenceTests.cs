using System.Security.Cryptography;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Independently audited private 1.7 saves carry valid checkpoints and
/// continuous counters. The original outer pair is NOT a direct parent
/// edge, whereas the supplied middle saves do form two direct edges.
/// </summary>
public sealed class HistoricalCheckpointPairEvidenceTests
{
    private static readonly DateTime Stamp = new(2026, 11, 9, 11, 0, 0, DateTimeKind.Utc);

    private static byte[] SyntheticSave(string checkpointId, string missingParent,
        int tick, int raids, int deaths)
    {
        var checkpoint = new CheckpointPayload
        {
            CheckpointId = checkpointId,
            ParentCheckpointId = missingParent,
            SaveTick = tick,
            SavedUtc = Stamp.AddMinutes((tick - 60000) / 60000.0),
            Families = new Dictionary<string, FamilyState>(StringComparer.Ordinal)
            {
                ["raid"] = new FamilyState
                {
                    SinceTick = 1000, ThroughTick = tick,
                    Continuous = true, Evidence = MetricEvidence.Experimental,
                    Metrics = new Dictionary<string, Accumulator>(StringComparer.Ordinal)
                    {
                        ["arrivals"] = new Accumulator { N = raids },
                    },
                },
            },
        };
        return new SaveXmlBuilder()
            .Minimal()
            .MetaMods("1.7.5000 rev1275", "ludeon.rimworld", "ludeon.rimworld.royalty")
            .Tick(ticksGame: tick, start: 0)
            .Stats(raids: raids, killed: deaths)
            .Component(SaveReader.ComponentClassName,
                new XElement("campaignId", "synthetic-same-colony"),
                new XElement("numericStatistics", CheckpointJson.Serialize(checkpoint)))
            .Build();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoCheckpointsWithDifferentUnobservedParents_DoNotInventInterval(bool reverseImport)
    {
        // Reproduces the real pair's *shape* (not IDs, ticks, or contents):
        // consistent colony/mods, increasing tick and counters, identical
        // capture-since tick, but two different missing predecessor nodes.
        var firstBytes = SyntheticSave("first", "missing-a", 60000, 15, 1);
        var nextBytes = SyntheticSave("second", "missing-b", 120000, 16, 1);
        var inputs = reverseImport
            ? new[] { ("second.rws", nextBytes), ("first.rws", firstBytes) }
            : new[] { ("first.rws", firstBytes), ("second.rws", nextBytes) };
        var profile = new Profile();
        foreach (var (name, bytes) in inputs)
        {
            var original = bytes.ToArray();
            var read = new SaveReader().ReadDetailed(
                new MemoryStream(bytes, writable: false), name, bytes.Length, Stamp);
            Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
            Assert.NotNull(read.Metrics);
            Assert.NotNull(read.Checkpoint);
            Assert.Equal(MetricObservationSource.Checkpoint, read.Checkpoint!.Source);
            ProfileMutations.UpsertMetricObservation(profile, read.Checkpoint);
            ProfileMutations.UpsertMetricObservation(profile, read.Metrics!);
            Assert.Equal(original, bytes); // input bytes remain unchanged
        }
        Assert.Equal(2, profile.MetricObservations.Count(
            o => o.Source == MetricObservationSource.Checkpoint));
        Assert.Equal(2, profile.MetricObservations.Count(
            o => o.Source == MetricObservationSource.SaveFile));
        Assert.Empty(HistoricalCounterIntervals.Evaluate(profile));
    }

    [Fact]
    public void OptionalOriginalPair_ParsesAndRejectsUnprovenParentage_WhenAvailable()
    {
        var earlier = Environment.GetEnvironmentVariable("RWS_PRIVATE_CHECKPOINT_EARLIER");
        var later = Environment.GetEnvironmentVariable("RWS_PRIVATE_CHECKPOINT_LATER");
        if (string.IsNullOrWhiteSpace(earlier) || string.IsNullOrWhiteSpace(later))
            Assert.Skip("Set both private checkpoint pair paths locally; originals are not in CI.");

        Assert.True(File.Exists(earlier), "Earlier private save unavailable.");
        Assert.True(File.Exists(later), "Later private save unavailable.");

        static byte[] Digest(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return SHA256.HashData(stream);
        }

        var paths = new[] { earlier!, later! };
        var before = paths.Select(path =>
        {
            var info = new FileInfo(path);
            return (Length: info.Length, Write: info.LastWriteTimeUtc, Hash: Digest(path));
        }).ToArray();
        var reader = new SaveReader();
        var a = reader.ReadDetailed(paths[0]);
        var b = reader.ReadDetailed(paths[1]);

        Assert.Equal(ObservationStatus.Ok, a.Observation.Status);
        Assert.Equal(ObservationStatus.Ok, b.Observation.Status);
        Assert.NotNull(a.Metrics);
        Assert.NotNull(b.Metrics);
        Assert.NotNull(a.Checkpoint);
        Assert.NotNull(b.Checkpoint);
        Assert.Equal("1.7.5000 rev1275", a.Observation.GameVersion);
        Assert.True(a.Observation.GameVersion == b.Observation.GameVersion);
        Assert.True(a.Observation.ModCount == 192 && b.Observation.ModCount == 192);
        Assert.True(a.Metrics!.ModSetHash == b.Metrics!.ModSetHash);
        Assert.True(a.Metrics.ModSetHash is { Length: 64 });
        Assert.True(a.Observation.Identity.ModCampaignId is { Length: > 0 });
        Assert.True(a.Observation.Identity.ModCampaignId == b.Observation.Identity.ModCampaignId);
        Assert.True(a.Observation.Identity.FingerprintHash() is { } fingerprint
            && fingerprint == b.Observation.Identity.FingerprintHash());
        Assert.True(a.Observation.Identity.StartingPawnIds.ToHashSet(StringComparer.Ordinal)
            .SetEquals(b.Observation.Identity.StartingPawnIds));

        Assert.True(a.Observation.Values.TicksGame == 10037861);
        Assert.True(b.Observation.Values.TicksGame == 10482325);
        Assert.True(a.Checkpoint!.SaveTick == a.Observation.Values.TicksGame);
        Assert.True(b.Checkpoint!.SaveTick == b.Observation.Values.TicksGame);
        Assert.True(!string.IsNullOrWhiteSpace(a.Checkpoint.ParentKey));
        Assert.True(!string.IsNullOrWhiteSpace(b.Checkpoint.ParentKey));
        Assert.True(a.Checkpoint.Key != b.Checkpoint.Key);
        Assert.True(a.Checkpoint.Key != b.Checkpoint.ParentKey);
        Assert.True(a.Observation.Values.NumRaidsEnemy == 15);
        Assert.True(b.Observation.Values.NumRaidsEnemy == 16);
        Assert.True(a.Observation.Values.ColonistsKilled == 1);
        Assert.True(b.Observation.Values.ColonistsKilled == 1);

        var profile = new Profile();
        foreach (var read in new[] { a, b })
        {
            ProfileMutations.UpsertMetricObservation(profile, read.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, read.Metrics!);
        }
        Assert.Empty(HistoricalCounterIntervals.Evaluate(profile));

        for (var i = 0; i < paths.Length; i++)
        {
            var after = new FileInfo(paths[i]);
            Assert.True(before[i].Length == after.Length);
            Assert.True(before[i].Write == after.LastWriteTimeUtc);
            Assert.True(before[i].Hash.AsSpan().SequenceEqual(Digest(paths[i])));
        }
        TestContext.Current.SendDiagnosticMessage(
            "Private checkpoint pair: 2 valid distinct checkpoints, stable campaign and saved MOD set, " +
            "unobserved/nonmatching direct parents, zero provable intervals, originals unchanged.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FiveSavedCheckpoints_TwoConfirmedMiddleEdges_IgnoreMissingOuterParents(bool reverseImport)
    {
        // Five independently read checkpoint-bearing saves, modeled only on
        // the public/non-identifying graph shape of the actual 2026-11-09
        // private archive. The real checkpoint IDs and bytes are NOT fixtures.
        var sources = new[]
        {
            ("a.rws", SyntheticSave("a", "unobserved-earlier", 100000, 15, 1)),
            ("b.rws", SyntheticSave("b", "unobserved-middle", 400000, 16, 1)),
            ("c.rws", SyntheticSave("c", "b", 445000, 16, 1)),
            ("d.rws", SyntheticSave("d", "c", 490000, 16, 1)),
            ("e.rws", SyntheticSave("e", "unobserved-later", 545000, 16, 1)),
        };
        var profile = new Profile();
        var ordered = reverseImport ? sources.Reverse().ToArray() : sources;

        foreach (var (name, bytes) in ordered)
        {
            var parsed = new SaveReader().ReadDetailed(
                new MemoryStream(bytes, writable: false), name, bytes.Length, Stamp);
            Assert.Equal(ObservationStatus.Ok, parsed.Observation.Status);
            Assert.NotNull(parsed.Metrics);
            Assert.NotNull(parsed.Checkpoint);
            ProfileMutations.UpsertMetricObservation(profile, parsed.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, parsed.Metrics!);
        }

        var intervals = HistoricalCounterIntervals.Evaluate(profile)
            .OrderBy(x => x.StartTick).ToArray();
        Assert.Equal(5, profile.MetricObservations.Count(
            o => o.Source == MetricObservationSource.Checkpoint));
        Assert.Equal(5, profile.MetricObservations.Count(
            o => o.Source == MetricObservationSource.SaveFile));
        Assert.Equal(2, intervals.Length);
        Assert.Equal((400000, 445000), (intervals[0].StartTick, intervals[0].EndTick));
        Assert.Equal((445000, 490000), (intervals[1].StartTick, intervals[1].EndTick));
        Assert.All(intervals, row =>
        {
            Assert.Equal(MetricCoverage.Partial, row.Coverage);
            Assert.Equal(0L, row.RaidsIncrease);
            Assert.Equal(0L, row.ColonistDeathsIncrease);
        });
        // Repeated scans must never duplicate either checkpoint evidence or
        // confirmed intervals and must never create a bridge to the older save.
        foreach (var (name, bytes) in ordered)
        {
            var parsed = new SaveReader().ReadDetailed(
                new MemoryStream(bytes, writable: false), name, bytes.Length, Stamp);
            ProfileMutations.UpsertMetricObservation(profile, parsed.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, parsed.Metrics!);
        }
        Assert.Equal(2, HistoricalCounterIntervals.Evaluate(profile).Count);
        Assert.Equal(10, profile.MetricObservations.Count);
    }

    [Fact]
    public void OptionalFiveOriginalSaves_ProveOnlyTwoMiddleEdges_WhenAvailable()
    {
        // The two outer original files use the environment variables from the
        // previously merged opt-in pair acceptance; only three new variables
        // are needed for this positive-lineage test. No originals go to CI.
        var paths = new[]
        {
            Environment.GetEnvironmentVariable("RWS_PRIVATE_CHECKPOINT_EARLIER"),
            Environment.GetEnvironmentVariable("RWS_PRIVATE_CHECKPOINT_2110"),
            Environment.GetEnvironmentVariable("RWS_PRIVATE_CHECKPOINT_2113"),
            Environment.GetEnvironmentVariable("RWS_PRIVATE_CHECKPOINT_2117"),
            Environment.GetEnvironmentVariable("RWS_PRIVATE_CHECKPOINT_LATER"),
        };
        if (paths.Any(string.IsNullOrWhiteSpace))
            Assert.Skip("Supply all five private checkpoint save paths locally.");
        Assert.All(paths, path => Assert.True(File.Exists(path), "A private source save is missing."));

        static byte[] Digest(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return SHA256.HashData(stream);
        }

        var metadata = paths.Select(path =>
        {
            var file = new FileInfo(path!);
            return (file.Length, file.LastWriteTimeUtc, Hash: Digest(path!));
        }).ToArray();
        var reader = new SaveReader();
        var observations = paths.Select(path => reader.ReadDetailed(path!)).ToArray();
        var ticks = new[] { 10037861, 10336998, 10381998, 10426998, 10482325 };
        var raids = new[] { 15, 16, 16, 16, 16 };
        var campaign = observations[0].Observation.Identity.ModCampaignId;
        var fingerprint = observations[0].Observation.Identity.FingerprintHash();
        var mods = observations[0].Metrics?.ModSetHash;
        Assert.False(string.IsNullOrEmpty(campaign));
        Assert.False(string.IsNullOrEmpty(fingerprint));
        Assert.False(string.IsNullOrEmpty(mods));

        for (var i = 0; i < observations.Length; i++)
        {
            var read = observations[i];
            Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
            Assert.Equal("1.7.5000 rev1275", read.Observation.GameVersion);
            Assert.Equal(192, read.Observation.ModCount);
            Assert.Equal(ticks[i], read.Observation.Values.TicksGame);
            Assert.Equal(raids[i], read.Observation.Values.NumRaidsEnemy);
            Assert.Equal(1, read.Observation.Values.ColonistsKilled);
            Assert.NotNull(read.Metrics);
            Assert.NotNull(read.Checkpoint);
            Assert.Equal(read.Observation.Values.TicksGame, read.Checkpoint!.SaveTick);
            Assert.False(string.IsNullOrWhiteSpace(read.Checkpoint.ParentKey));
            Assert.Equal(campaign, read.Observation.Identity.ModCampaignId);
            Assert.Equal(fingerprint, read.Observation.Identity.FingerprintHash());
            Assert.Equal(mods, read.Metrics!.ModSetHash);
            Assert.Equal(MetricObservationSource.Checkpoint, read.Checkpoint.Source);
        }

        // These are explicit checkpoint references read from all five
        // originals; neither MOD list nor timestamp ordering establishes them.
        Assert.NotEqual(observations[0].Checkpoint!.Key, observations[1].Checkpoint!.ParentKey);
        Assert.Equal(observations[1].Checkpoint!.Key, observations[2].Checkpoint!.ParentKey);
        Assert.Equal(observations[2].Checkpoint!.Key, observations[3].Checkpoint!.ParentKey);
        Assert.NotEqual(observations[3].Checkpoint!.Key, observations[4].Checkpoint!.ParentKey);

        var profile = new Profile();
        foreach (var read in observations)
        {
            ProfileMutations.UpsertMetricObservation(profile, read.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, read.Metrics!);
        }
        var intervals = HistoricalCounterIntervals.Evaluate(profile)
            .OrderBy(interval => interval.StartTick).ToArray();
        Assert.Equal(2, intervals.Length);
        Assert.Equal((ticks[1], ticks[2]), (intervals[0].StartTick, intervals[0].EndTick));
        Assert.Equal((ticks[2], ticks[3]), (intervals[1].StartTick, intervals[1].EndTick));
        Assert.All(intervals, interval =>
        {
            Assert.Equal(MetricCoverage.Partial, interval.Coverage);
            Assert.Equal(0L, interval.RaidsIncrease);
            Assert.Equal(0L, interval.ColonistDeathsIncrease);
        });

        foreach (var read in observations)
        {
            ProfileMutations.UpsertMetricObservation(profile, read.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, read.Metrics!);
        }
        Assert.Equal(2, HistoricalCounterIntervals.Evaluate(profile).Count);
        Assert.Equal(10, profile.MetricObservations.Count);

        for (var i = 0; i < paths.Length; i++)
        {
            var file = new FileInfo(paths[i]!);
            Assert.Equal(metadata[i].Length, file.Length);
            Assert.Equal(metadata[i].LastWriteTimeUtc, file.LastWriteTimeUtc);
            Assert.True(metadata[i].Hash.AsSpan().SequenceEqual(Digest(paths[i]!)));
        }
        TestContext.Current.SendDiagnosticMessage(
            "Five private checkpoint saves: two direct middle edges, " +
            "zero confirmed raid/death counter increases, no inferred outer edges, " +
            "idempotent import and unchanged original files.");
    }
}
