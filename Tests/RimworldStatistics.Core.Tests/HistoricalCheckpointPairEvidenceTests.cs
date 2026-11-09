using System.Security.Cryptography;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Two independently audited private 1.7 saves carry valid checkpoints and
/// continuous counters, but the two checkpoint IDs are NOT a direct parent
/// edge. No private save data or original checkpoint identifiers are fixtures.
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
            var read = new SaveReader().ReadDetailed(
                new MemoryStream(bytes, writable: false), name, bytes.Length, Stamp);
            Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
            Assert.NotNull(read.Metrics);
            Assert.NotNull(read.Checkpoint);
            Assert.Equal(MetricObservationSource.Checkpoint, read.Checkpoint!.Source);
            ProfileMutations.UpsertMetricObservation(profile, read.Checkpoint);
            ProfileMutations.UpsertMetricObservation(profile, read.Metrics!);
            Assert.Equal(bytes, bytes.ToArray()); // input is never mutated
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
}
