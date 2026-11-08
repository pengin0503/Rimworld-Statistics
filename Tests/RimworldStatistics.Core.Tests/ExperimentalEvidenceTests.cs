using System.Text.Json;

namespace RimworldStatistics.Core.Tests;

public sealed class ExperimentalEvidenceTests
{
    [Fact]
    public void CombiningCaptureEvidenceDoesNotConfuseItWithCoverage()
    {
        Assert.Equal(MetricEvidence.Experimental, MetricEvidencePolicy.Combine(new[] { MetricEvidence.Experimental, MetricEvidence.Experimental }));
        Assert.Equal(MetricEvidence.Mixed, MetricEvidencePolicy.Combine(new[] { MetricEvidence.Experimental, MetricEvidence.Verified }));
        Assert.Equal(MetricEvidence.UnknownLegacy, MetricEvidencePolicy.Combine(new[] { MetricEvidence.UnknownLegacy, MetricEvidence.Verified }));
    }

    [Fact]
    public void FamilyEvidenceRoundTripsAndOldJsonRemainsUnknown()
    {
        var family = new FamilyState { Revision = 1, Evidence = MetricEvidence.Experimental };
        var payload = new CheckpointPayload { CheckpointId = "P", Families = { ["research"] = family } };
        var restored = CheckpointJson.Parse(CheckpointJson.Serialize(payload)).Payload!;
        Assert.Equal(MetricEvidence.Experimental, restored.Families["research"].Evidence);
        var old = CheckpointJson.Parse("""{"formatVersion":1,"checkpointId":"P","saveTick":0,"families":{"research":{"revision":1}}}""").Payload!;
        Assert.Null(old.Families["research"].Evidence);
    }

    [Fact]
    public void ResumingAnExperimentalCheckpointAsVerifiedCannotRewriteItsPast()
    {
        var active = new[] { new FamilySpec("research", 1) };
        var experimental = new HashSet<string>(StringComparer.Ordinal) { "research" };
        var initial = LiveMetricAccumulator.StartNew(active, 0, experimental);
        var saved = initial.PrepareSave(100, DateTime.UtcNow, "P")!;
        initial.Commit(saved);
        var later = LiveMetricAccumulator.Resume(saved.Json, active, 100, new HashSet<string>(StringComparer.Ordinal));
        var newSave = later.PrepareSave(150, DateTime.UtcNow, "A")!;
        Assert.Equal(MetricEvidence.Mixed, newSave.Payload!.Families["research"].Evidence);
        Assert.Equal(MetricEvidence.Experimental, CheckpointJson.Parse(saved.Json).Payload!.Families["research"].Evidence);
    }

    [Fact]
    public void ReimportingSameSnapshotDoesNotDiscardOlderCaptureConfidence()
    {
        var profile = new Profile();
        MetricObservation Snapshot(MetricEvidence evidence) => new()
        {
            Source = MetricObservationSource.SaveSnapshot, Key = "same-save",
            EvidenceByAcquisition = new Dictionary<string, MetricEvidence> { ["facilities"] = evidence },
            Metrics = new Dictionary<string, MetricEntry>
            {
                [FacilitiesSnapshot.OutdoorTempMax] = MetricEntry.Of(MetricKeys.Revision(FacilitiesSnapshot.OutdoorTempMax),
                    new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = 2 }),
            },
        };
        ProfileMutations.UpsertMetricObservation(profile, Snapshot(MetricEvidence.Experimental));
        ProfileMutations.UpsertMetricObservation(profile, Snapshot(MetricEvidence.Verified));
        Assert.Equal(MetricEvidence.Mixed,
            Assert.Single(profile.MetricObservations).EvidenceByAcquisition!["facilities"]);
    }

    [Fact]
    public void RecordingGapOnReenableKeepsHistoricalTotalsPartial()
    {
        var units = new[] { new FamilySpec("research", 1) };
        var experimental = new HashSet<string> { "research" };
        var parent = LiveMetricAccumulator.StartNew(units, 0, experimental);
        parent.Family("research")!.Count("events");
        var a = parent.PrepareSave(100, DateTime.UtcNow, "P")!;
        parent.Commit(a);
        var off = LiveMetricAccumulator.Resume(a.Json, Array.Empty<FamilySpec>(), 100, experimental);
        var draft = off.PrepareSave(150, DateTime.UtcNow, "off");
        Assert.False(draft?.IsNew ?? false);
        var resumed = LiveMetricAccumulator.Resume(draft?.Json ?? a.Json, units, 150, experimental);
        Assert.False(resumed.Family("research")!.Continuous);
        Assert.Equal(1, resumed.Family("research")!.Peek("events")?.N);
    }

    [Fact]
    public void CompactedSnapshotExtremaPreserveExperimentalCaptures()
    {
        var profile = new Profile();
        var id = new ColonyIdentity
        {
            SeedString = "experimental", PersistentRandomValue = 1,
            StartingTile = "5,0", GameStartAbsTick = 1, FirstWealthBits = 13,
            ModCampaignId = "test-campaign",
            StartingPawnIds = { "Thing_Human1" },
        };
        ProfileMutations.RegisterLiveCampaign(profile, id.ModCampaignId!, id, new DisplayInfo());
        for (var i = 0; i < 4; i++)
        {
            var obs = new MetricObservation
            {
                Key = "snap-" + i, Source = MetricObservationSource.SaveSnapshot,
                Identity = id.Clone(), SaveTick = i + 1,
                ObservedUtc = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i),
                EvidenceByAcquisition = new Dictionary<string, MetricEvidence>
                {
                    ["facilities"] = i == 2 ? MetricEvidence.Experimental : MetricEvidence.Verified,
                },
            };
            obs.Metrics[FacilitiesSnapshot.OutdoorTempMax] =
                MetricEntry.Of(MetricKeys.Revision(FacilitiesSnapshot.OutdoorTempMax),
                    new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = i + 1 });
            ProfileMutations.UpsertMetricObservation(profile, obs);
        }
        var colonies = CampaignMatcher.BuildColonies(profile);
        var def = Assert.Single(SnapshotCatalog.Definitions, d => d.Key == FacilitiesSnapshot.OutdoorTempMax);
        var view = MetricCalculator.BuildView(profile, colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()), new[] { def });
        var row = Assert.Single(Assert.Single(view.Colonies).Rows);
        Assert.Equal(4, row.Value.Number);
        Assert.Equal(MetricEvidence.Mixed, row.Value.Evidence);
    }

    [Fact]
    public void SnapshotObservationCarriesPerAcquisitionEvidence()
    {
        var o = new MetricObservation
        {
            Key = "snapshot",
            Source = MetricObservationSource.SaveSnapshot,
            EvidenceByAcquisition = new Dictionary<string, MetricEvidence>(StringComparer.Ordinal)
            {
                ["facilities"] = MetricEvidence.Experimental,
            },
        };
        var json = JsonSerializer.Serialize(o, ProfileJson.Options);
        var copy = JsonSerializer.Deserialize<MetricObservation>(json, ProfileJson.Options)!;
        Assert.Equal(MetricEvidence.Experimental, copy.EvidenceByAcquisition!["facilities"]);
    }
}
