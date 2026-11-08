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
