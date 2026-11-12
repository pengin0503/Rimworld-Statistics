namespace RimworldStatistics.Core.Tests;

public sealed class VpeMetricTests
{
    [Fact]
    public void LevelsRequireConfirmedPositivePlayerChange()
    {
        var state = new FamilyState();
        VpeStatistics.RecordLevels(state, 1, 4, true);
        VpeStatistics.RecordLevels(state, 4, 4, true);
        VpeStatistics.RecordLevels(state, 4, 2, true);
        VpeStatistics.RecordLevels(state, 1, 9, false);
        var results = new Dictionary<string, MetricEntry>();
        VpeStatistics.Family.Build(new FamilyOutputContext(state, 0, results));
        Assert.Equal(3, results[VpeStatistics.LevelsGained].Values[MetricEntry.Scalar].IntegerValue);
    }

    [Fact]
    public void SnapshotCountsOnlyPsycastersAndDistinguishesNoMaximum()
    {
        var metrics = VpeStatistics.BuildSnapshot([2, 4]);
        Assert.Equal(2, metrics[VpeStatistics.Psycasters].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(4, metrics[VpeStatistics.MaxLevel].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(6, metrics[VpeStatistics.SumLevels].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable, VpeStatistics.BuildSnapshot([])[VpeStatistics.MaxLevel].Values[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void LastVpeCaptureSurvivesLaterSnapshotWithoutVpe()
    {
        var identity = new ColonyIdentity { ModCampaignId = "vpe-test" };
        var colony = new Colony { Key = "id:vpe-test" };
        colony.Observations.Add(new SaveObservation { FileName = "test.rws", Identity = identity, Status = ObservationStatus.Ok });
        var profile = new Profile();
        var measured = new MetricObservation
        {
            Key = "vpe", Identity = identity, Source = MetricObservationSource.SaveSnapshot,
            SaveTick = 100, Metrics = VpeStatistics.BuildSnapshot([3]),
            EvidenceByAcquisition = new() { [VpeStatistics.SnapshotUnit] = MetricEvidence.Experimental },
        };
        profile.MetricObservations.Add(measured);
        profile.MetricObservations.Add(new MetricObservation
        {
            Key = "later", Identity = identity, Source = MetricObservationSource.SaveSnapshot, SaveTick = 200,
        });
        var view = MetricCalculator.BuildView(profile, [colony], StatsCalculator.Calculate([colony], new StatsOptions()),
            MetricRegistry.Compose([], MetricDisplayPolicy.AllDetailsVisibleUnits));
        var value = Assert.Single(view.Colonies[0].Rows, row => row.Definition.Key == VpeStatistics.Psycasters).Value;
        Assert.Equal(1, value.Number);
        Assert.Equal(MetricCoverage.Partial, value.Coverage);
        Assert.Equal(MetricEvidence.Experimental, value.Evidence);
        Assert.Equal(100, value.ThroughTick);
    }

    [Fact]
    public void PublishedViewAndMetricIdsStayStable()
    {
        var definition = MetricRegistry.Find(VpeStatistics.Psycasters)!;
        Assert.False(definition.Published);
        Assert.DoesNotContain(MetricRegistry.All, d => d.Key == definition.Key);
        Assert.Contains(MetricRegistry.Compose([], MetricDisplayPolicy.AllDetailsVisibleUnits), d => d.Key == definition.Key);
    }
}
