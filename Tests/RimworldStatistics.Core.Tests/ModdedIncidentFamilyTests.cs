namespace RimworldStatistics.Core.Tests;

/// <summary>Generic MOD IncidentDef capture, independent of any events expansion.</summary>
public sealed class ModdedIncidentFamilyTests
{
    private static readonly DateTime SavedAt = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    private static (LiveMetricAccumulator Acc, FamilyState State) NewGame()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { ModdedIncidentFamily.Family.Spec }, 0);
        return (acc, acc.Family(ModdedIncidentFamily.Name)!);
    }

    private static MetricObservation Save(LiveMetricAccumulator acc, int tick)
    {
        var draft = acc.PrepareSave(tick, SavedAt, "save_" + tick)!;
        acc.Commit(draft);
        return CheckpointMetrics.ToObservation(draft.Payload!, draft.Json, new ColonyIdentity { ModCampaignId = "incident-test" }, null, null);
    }

    [Fact]
    public void Registry_SeparatesNewExperimentalUnitFromTheThreeLegacyDisasters()
    {
        Assert.Equal(AcquisitionStatus.Unverified, AcquisitionUnits.Find(ModdedIncidentFamily.Name)!.Status);
        Assert.Equal(AcquisitionKind.Family, AcquisitionUnits.Find(ModdedIncidentFamily.Name)!.Kind);
        var definition = MetricRegistry.Find(ModdedIncidentFamily.ByDef)!;
        Assert.Equal(ModdedIncidentFamily.Name, definition.Acquisition);
        Assert.True(definition.HasBreakdown);
        Assert.NotNull(MetricRegistry.Find(CrossColonyCatalog.TopPrefix + ModdedIncidentFamily.ByDef));
        Assert.NotNull(MetricRegistry.Find(CrossColonyCatalog.MaxPrefix + ModdedIncidentFamily.ByDef));
    }

    [Fact]
    public void SourceIdentityRequiresAnExternalIncidentDef()
    {
        Assert.True(ModdedIncidentFamily.IsModded("vanillaexpanded.events", "EarthQuake"));
        Assert.False(ModdedIncidentFamily.IsModded("ludeon.rimworld", "CropBlight"));
        Assert.False(ModdedIncidentFamily.IsModded("LUDEON.RIMWORLD.ODYSSEY", "FishMigration"));
        Assert.False(ModdedIncidentFamily.IsModded(null, "EarthQuake"));
        Assert.False(ModdedIncidentFamily.IsModded("other.events", ""));
    }

    [Fact]
    public void OnlyConfirmedModdedHomeMapEventsCount_AndPackagesDoNotCollide()
    {
        var (acc, state) = NewGame();
        ModdedIncidentFamily.RecordSuccess(state, "vanillaexpanded.events", "EarthQuake", succeeded: false, playerHome: true);
        ModdedIncidentFamily.RecordSuccess(state, "vanillaexpanded.events", "EarthQuake", succeeded: true, playerHome: false);
        ModdedIncidentFamily.RecordSuccess(state, "ludeon.rimworld", "CropBlight", succeeded: true, playerHome: true);
        ModdedIncidentFamily.RecordSuccess(state, "ludeon.rimworld.odyssey", "FishMigration", succeeded: true, playerHome: true);
        ModdedIncidentFamily.RecordSuccess(state, null, "EarthQuake", succeeded: true, playerHome: true);
        ModdedIncidentFamily.RecordSuccess(state, "vanillaexpanded.events", "", succeeded: true, playerHome: true);
        ModdedIncidentFamily.RecordSuccess(state, "vanillaexpanded.events", "EarthQuake", succeeded: true, playerHome: true);
        ModdedIncidentFamily.RecordSuccess(state, "another.events", "EarthQuake", succeeded: true, playerHome: true);
        var obs = Save(acc, 100);
        var breakdown = obs.Metrics[ModdedIncidentFamily.ByDef].Values;
        Assert.Equal(2L, obs.Scalar(ModdedIncidentFamily.ByDef, MetricKeys.Revision(ModdedIncidentFamily.ByDef))!.IntegerValue);
        Assert.Equal(1L, breakdown["vanillaexpanded.events:EarthQuake"].IntegerValue);
        Assert.Equal(1L, breakdown["another.events:EarthQuake"].IntegerValue);
        Assert.Equal(3, breakdown.Count); // 2 packages and scalar
    }

    [Fact]
    public void RepeatedIndependentSuccessesAreCountedButLegacyCountersStayDistinct()
    {
        var (acc, state) = NewGame();
        ModdedIncidentFamily.RecordSuccess(state, "vanillaexpanded.events", "EarthQuake", succeeded: true, playerHome: true);
        ModdedIncidentFamily.RecordSuccess(state, "vanillaexpanded.events", "EarthQuake", succeeded: true, playerHome: true);
        var obs = Save(acc, 300);
        Assert.Equal(2L, obs.Scalar(ModdedIncidentFamily.ByDef, MetricKeys.Revision(ModdedIncidentFamily.ByDef))!.IntegerValue);
        Assert.False(obs.Metrics.ContainsKey(DisasterFamily.CropBlights));
        Assert.False(obs.Metrics.ContainsKey(DisasterFamily.Meteors));
        Assert.False(obs.Metrics.ContainsKey(DisasterFamily.ShortCircuits));
    }

    [Fact]
    public void RemovedEventsModKeepsHistoricalCheckpointValues()
    {
        var (acc, state) = NewGame();
        ModdedIncidentFamily.RecordSuccess(state, "vanillaexpanded.events", "EarthQuake", succeeded: true, playerHome: true);
        var saved = acc.PrepareSave(300, SavedAt, "original")!;
        acc.Commit(saved);
        var withoutUnit = LiveMetricAccumulator.Resume(saved.Json, Array.Empty<FamilySpec>(), 300);
        var retained = CheckpointJson.Parse(withoutUnit.PrepareSave(400, SavedAt, "next")!.Json).Payload!;
        var obs = CheckpointMetrics.ToObservation(retained, saved.Json,
            new ColonyIdentity { ModCampaignId = "incident-test" }, null, null);
        Assert.Equal(1L, obs.Scalar(ModdedIncidentFamily.ByDef, MetricKeys.Revision(ModdedIncidentFamily.ByDef))!.IntegerValue);
    }

    [Fact]
    public void BreakdownOverflowWeakensBreakdownButNotConfirmedTotal()
    {
        var (acc, state) = NewGame();
        for (var i = 0; i <= FamilyOps.KeyCap; i++)
            ModdedIncidentFamily.RecordSuccess(state, "test.events", "Custom" + i, succeeded: true, playerHome: true);
        var obs = Save(acc, 100);
        var entry = obs.Metrics[ModdedIncidentFamily.ByDef];
        Assert.Equal((long)FamilyOps.KeyCap + 1, entry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Complete, entry.Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(FamilyOps.KeyCap, entry.Values.Count - 1);
        Assert.All(entry.Values.Where(kv => kv.Key != MetricEntry.Scalar), kv => Assert.Equal(MetricCoverage.Partial, kv.Value.Coverage));
    }
}
