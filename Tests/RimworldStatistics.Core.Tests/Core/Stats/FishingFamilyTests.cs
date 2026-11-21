namespace RimworldStatistics.Core.Tests;

/// <summary>GEN-FISH: accepted placements, not attempts or generated candidate catches.</summary>
public sealed class FishingFamilyTests
{
    private static readonly DateTime SavedAt = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    private static (LiveMetricAccumulator Acc, FamilyState State) New()
    {
        var a = LiveMetricAccumulator.StartNew(new[] { FishingFamily.Family.Spec }, 0);
        return (a, a.Family(FishingFamily.Name)!);
    }

    private static MetricObservation Save(LiveMetricAccumulator a, int tick = 20000)
    {
        var d = a.PrepareSave(tick, SavedAt, "fish-" + tick)!;
        a.Commit(d);
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json,
            new ColonyIdentity { ModCampaignId = "fishing-campaign" }, null, null);
    }

    private static long V(MetricObservation o, string key) =>
        o.Scalar(key, MetricKeys.Revision(key))!.IntegerValue!.Value;

    [Fact]
    public void Registry_RequiresOdysseyAndProvidesCrossColonyRankings()
    {
        var unit = AcquisitionUnits.Find(FishingFamily.Name)!;
        Assert.Equal(AcquisitionKind.Family, unit.Kind);
        Assert.Equal(AcquisitionStatus.Unverified, unit.Status);
        Assert.Equal(AcquisitionUnits.Odyssey, unit.RequiredDlc);
        Assert.NotNull(MetricRegistry.Find(FishingFamily.SuccessfulActions));
        Assert.NotNull(MetricRegistry.Find(FishingFamily.AcceptedUnitsByDef));
        Assert.NotNull(MetricRegistry.Find(CrossColonyCatalog.TopPrefix + FishingFamily.AcceptedUnitsByDef));
        Assert.NotNull(MetricRegistry.Find(CrossColonyCatalog.MaxPrefix + FishingFamily.SuccessfulActions));
    }

    [Fact]
    public void UnsuccessfulAndInterruptedJobsHaveNoAcceptedCatch()
    {
        var (a,s) = New();
        FishingFamily.RecordAccepted(s, "job-1", "main", new Dictionary<string,int>(), 100);
        FishingFamily.RecordAccepted(s, "job-1", "finish-0", new Dictionary<string,int> { ["Fish"] = 0 }, 100);
        FishingFamily.RecordAccepted(s, "", "main", new Dictionary<string,int> { ["Fish"] = 1 }, 100);
        var o = Save(a);
        Assert.Equal(0, V(o, FishingFamily.SuccessfulActions));
        Assert.Equal(0, V(o, FishingFamily.AcceptedUnitsByDef));
    }

    [Fact]
    public void CountOnlyAcceptedPerDefQuantitiesAndDeduplicateEachStage()
    {
        var (a,s) = New();
        var placed = new Dictionary<string,int> { ["Fish_Salmon"] = 4, ["Fish_Bass"] = 2 };
        FishingFamily.RecordAccepted(s, "job-1", "main", placed, 100);
        FishingFamily.RecordAccepted(s, "job-1", "main", placed, 100); // double invocation
        FishingFamily.RecordAccepted(s, "job-1", "finish-0", new Dictionary<string,int> { ["VCEF_Arapaima"] = 10 }, 101);
        FishingFamily.RecordAccepted(s, "job-2", "main", new Dictionary<string,int> { ["Fish_Salmon"] = 1 }, 200);
        var o = Save(a);
        Assert.Equal(2, V(o, FishingFamily.SuccessfulActions)); // one action per job, including bonus
        Assert.Equal(17, V(o, FishingFamily.AcceptedUnitsByDef));
        var split = o.Metrics[FishingFamily.AcceptedUnitsByDef].Values;
        Assert.Equal(5, split["Fish_Salmon"].IntegerValue);
        Assert.Equal(2, split["Fish_Bass"].IntegerValue);
        Assert.Equal(10, split["VCEF_Arapaima"].IntegerValue);
    }

    [Fact]
    public void SaveReloadRetainsDedupKeysAndOldCountsAfterUnitDisabled()
    {
        var (a,s) = New();
        FishingFamily.RecordAccepted(s, "job-11", "main", new Dictionary<string,int> { ["Fish"] = 3 }, 100);
        var d = a.PrepareSave(300, SavedAt, "a")!;
        a.Commit(d);
        var resumed = LiveMetricAccumulator.Resume(d.Json, new[] { FishingFamily.Family.Spec }, 300);
        FishingFamily.RecordAccepted(resumed.Family(FishingFamily.Name)!, "job-11", "main",
            new Dictionary<string,int> { ["Fish"] = 3 }, 300);
        var o = Save(resumed, 400);
        Assert.Equal(1, V(o, FishingFamily.SuccessfulActions));
        Assert.Equal(3, V(o, FishingFamily.AcceptedUnitsByDef));
        var missing = LiveMetricAccumulator.Resume(d.Json, Array.Empty<FamilySpec>(), 300);
        var parse = CheckpointJson.Parse(missing.PrepareSave(500, SavedAt, "b")!.Json);
        Assert.Equal(CheckpointParseStatus.Ok, parse.Status);
        Assert.Contains(FishingFamily.Name, parse.Payload!.Families.Keys);
    }

    [Fact]
    public void DistinctJobsCountOnlyAfterAcceptedItemNotFromAStartOrFailure()
    {
        var (a,s) = New();
        FishingFamily.RecordAccepted(s, "job-3", "main",
            new Dictionary<string,int> { [""] = 100, ["Fish"] = -4 }, 150);
        FishingFamily.RecordAccepted(s, "job-3", "main",
            new Dictionary<string,int> { ["Fish"] = 5 }, 151);
        var o = Save(a);
        Assert.Equal(1, V(o, FishingFamily.SuccessfulActions));
        Assert.Equal(5, V(o, FishingFamily.AcceptedUnitsByDef));
    }
}
