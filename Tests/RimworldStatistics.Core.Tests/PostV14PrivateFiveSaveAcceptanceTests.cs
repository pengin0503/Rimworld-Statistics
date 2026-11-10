using System.Security.Cryptography;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Opt-in acceptance against the five *new* post-v1.4 private originals.
/// Paths never enter the repository or public CI; this test makes no snapshots
/// of names, IDs, full MOD lists, hashes, or save contents.
/// </summary>
public sealed class PostV14PrivateFiveSaveAcceptanceTests
{
    [Fact]
    public void OptionalNewFiveOriginals_AreReadOnly_DeduplicatedAndSplitConsistent()
    {
        var paths = new[]
        {
            Environment.GetEnvironmentVariable("RWS_PRIVATE_POST14_A"),
            Environment.GetEnvironmentVariable("RWS_PRIVATE_POST14_B"),
            Environment.GetEnvironmentVariable("RWS_PRIVATE_POST14_C"),
            Environment.GetEnvironmentVariable("RWS_PRIVATE_POST14_D"),
            Environment.GetEnvironmentVariable("RWS_PRIVATE_POST14_E"),
        };
        if (paths.Any(string.IsNullOrWhiteSpace))
            Assert.Skip("Set all five RWS_PRIVATE_POST14_[A-E] paths locally; original saves are never stored in CI.");
        Assert.All(paths, p => Assert.True(File.Exists(p), "A private input is unavailable."));
        var original = paths.Select(p =>
        {
            var info = new FileInfo(p!);
            using var file = File.OpenRead(p!);
            return (info.Length, info.LastWriteTimeUtc, Sha: SHA256.HashData(file));
        }).ToArray();

        var reader = new SaveReader();
        var reads = paths.Select(p => reader.ReadDetailed(p!)).ToArray();
        Assert.All(reads, r =>
        {
            Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
            Assert.NotNull(r.Metrics);
            Assert.NotNull(r.Checkpoint);
            Assert.Equal("1.7.5000 rev1275", r.Observation.GameVersion);
            Assert.Equal(192, r.Observation.ModCount);
            Assert.Equal(r.Observation.Values.TicksGame, r.Checkpoint!.SaveTick);
        });
        var earliest = reads[0];
        Assert.All(reads, r =>
        {
            Assert.Equal(earliest.Observation.Identity.ModCampaignId, r.Observation.Identity.ModCampaignId);
            Assert.Equal(earliest.Observation.Identity.FingerprintHash(), r.Observation.Identity.FingerprintHash());
            Assert.Equal(earliest.Metrics!.ModSetHash, r.Metrics!.ModSetHash);
        });
        var uniqueIds = reads.Select(r => r.Checkpoint!.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(5, uniqueIds.Count);
        // Same campaign/tick order is not proof of parentage. Exactly one
        // direct-parent ID is proven among the five new originals.
        Assert.Single(reads.Where(r => r.Checkpoint!.ParentKey is { } parent && uniqueIds.Contains(parent)));

        var splitKeys = new[]
        {
            WorkFamily.Sown_, TradeFamily.SalesUnits, TradeFamily.BuysUnits,
            WorkDetailsFamily.CraftedByDef, WorkDetailsFamily.MinedByDef,
        };
        foreach (var read in reads)
        {
            foreach (var key in splitKeys)
            {
                Assert.True(read.Checkpoint!.Metrics.TryGetValue(key, out var entry),
                    "An expected numeric family output is absent.");
                Assert.True(entry!.Values.TryGetValue(MetricEntry.Scalar, out var total));
                Assert.NotNull(total!.Number);
                var pieces = entry.Values.Where(x => x.Key != MetricEntry.Scalar)
                    .Select(x => x.Value.Number).ToArray();
                Assert.All(pieces, n => Assert.NotNull(n));
                Assert.InRange(Math.Abs(total.Number!.Value - pieces.Sum(n => n!.Value)),
                    0, Math.Max(0.000001, Math.Abs(total.Number.Value) * 0.000000001));
            }
        }

        var profile = new Profile();
        foreach (var read in reads)
        {
            ProfileMutations.UpsertMetricObservation(profile, read.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, read.Metrics!);
            ProfileMutations.AddSaveObservation(profile, read.Observation);
        }
        var colonies = CampaignMatcher.BuildColonies(profile);
        Assert.Single(colonies);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        var view = MetricCalculator.BuildView(profile, colonies, stats, MetricRegistry.Catalog);
        Assert.Single(view.Colonies);
        var rank = view.Overall.Single(r => r.Definition.Key ==
            CrossColonyCatalog.TopPrefix + TradeFamily.SalesUnits);
        Assert.Equal(1, rank.Targets);
        Assert.Equal(1, rank.Valid);
        Assert.NotEmpty(rank.Breakdowns);
        Assert.All(rank.Breakdowns, b => Assert.Single(b.Breakdowns)); // never five saved-file contributions

        foreach (var read in reads.Reverse())
        {
            ProfileMutations.UpsertMetricObservation(profile, read.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, read.Metrics!);
            ProfileMutations.AddSaveObservation(profile, read.Observation);
        }
        var repeated = MetricCalculator.BuildView(profile, CampaignMatcher.BuildColonies(profile),
            StatsCalculator.Calculate(CampaignMatcher.BuildColonies(profile), new StatsOptions()), MetricRegistry.Catalog);
        var repeatedTop = repeated.Overall.Single(r => r.Definition.Key ==
            CrossColonyCatalog.TopPrefix + TradeFamily.SalesUnits);
        Assert.Equal(rank.Value.Number, repeatedTop.Value.Number);
        Assert.Equal(rank.Breakdowns.Count, repeatedTop.Breakdowns.Count);

        for (var i = 0; i < paths.Length; i++)
        {
            var file = new FileInfo(paths[i]!);
            Assert.Equal(original[i].Length, file.Length);
            Assert.Equal(original[i].LastWriteTimeUtc, file.LastWriteTimeUtc);
            using var stream = File.OpenRead(paths[i]!);
            Assert.Equal(original[i].Sha, SHA256.HashData(stream));
        }
    }
}
