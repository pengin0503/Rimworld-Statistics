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
    public void CastsCountOnlyConfirmedPlayerFreeColonistCastsAndBreakDownByAbility()
    {
        var state = new FamilyState();
        VpeStatistics.RecordCast(state, "VPE_Skip", playerFreeColonist: true, confirmed: false);
        VpeStatistics.RecordCast(state, "VPE_Skip", playerFreeColonist: false, confirmed: true);
        VpeStatistics.RecordCast(state, "", playerFreeColonist: true, confirmed: true);
        VpeStatistics.RecordCast(state, "VPE_Skip", playerFreeColonist: true, confirmed: true);
        VpeStatistics.RecordCast(state, "VPE_Skip", playerFreeColonist: true, confirmed: true);
        VpeStatistics.RecordCast(state, "VPE_Cast", playerFreeColonist: true, confirmed: true);

        var values = new Dictionary<string, MetricEntry>();
        VpeStatistics.CastFamily.Build(new FamilyOutputContext(state, 0, values));
        var entry = values[VpeStatistics.Casts];
        Assert.Equal(3, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2, entry.Values["VPE_Skip"].IntegerValue);
        Assert.Equal(1, entry.Values["VPE_Cast"].IntegerValue);
    }

    [Fact]
    public void CastsBreakdownOverflowWeakensCoverageWithoutLosingScalar()
    {
        var state = new FamilyState { SinceTick = 0, ThroughTick = 20, Continuous = true };
        for (var i = 0; i <= FamilyOps.KeyCap; i++)
            VpeStatistics.RecordCast(state, "ability_" + i, true, true);

        var values = new Dictionary<string, MetricEntry>();
        VpeStatistics.CastFamily.Build(new FamilyOutputContext(state, 20, values));
        var entry = values[VpeStatistics.Casts];
        Assert.Equal(FamilyOps.KeyCap + 1, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(FamilyOps.KeyCap + 1, entry.Values.Count);
        Assert.Equal(MetricCoverage.Partial, entry.Values[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void VpeCastAcquisitionRemainsExperimentalAndIndependentOfLevelAcquisition()
    {
        Assert.Equal(AcquisitionStatus.Unverified, AcquisitionUnits.Find(VpeStatistics.CastUnit)?.Status);
        Assert.NotEqual(VpeStatistics.CastUnit, VpeStatistics.EventUnit);
        var def = MetricRegistry.Find(VpeStatistics.Casts);
        Assert.NotNull(def);
        Assert.True(def.HasBreakdown);
        Assert.False(def.Published);
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
