using System.Text.Json;
using RimworldStatistics.Api;

namespace RimworldStatistics.Core.Tests;

public sealed class ExternalSdkMetricTests
{
    private static ApiMetricDefinition Metric(string name = "checkouts", ApiEventMode mode = ApiEventMode.Count) => new()
    {
        ProviderId = "hotel.events", OwnerPackageId = "other.hospitality", FamilyId = "stays",
        MetricName = name, EventMode = mode,
        Overall = mode == ApiEventMode.Mean ? "Ratio" : "Sum",
    };

    [Fact]
    public void RegistryRejectsDuplicateConflictAndMalformedDefinitionsAndClosesAtStartup()
    {
        var registry = new ExternalSdkMetricRegistry();
        var d = Metric();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(d, null));
        Assert.Equal(ApiRegistrationResult.DuplicateMetric, registry.Register(d, null));
        Assert.Equal(ApiRegistrationResult.ConflictingFamily,
            registry.Register(Metric("fees") with { ProviderId = "different" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            registry.Register(Metric(" bad"), null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            registry.Register(Metric("fees") with { Overall = "Ratio" }, null));
        Assert.Single(registry.Seal());
        Assert.Equal(ApiRegistrationResult.Closed, registry.Register(Metric("new"), null));
    }

    [Fact]
    public void RegistrySupportsMultipleMetricsPerFamilyAndDeterministicOrdering()
    {
        var items = new[] { Metric("z"), Metric("a"), Metric("m", ApiEventMode.Mean) };
        var left = new ExternalSdkMetricRegistry();
        var right = new ExternalSdkMetricRegistry();
        foreach (var item in items) Assert.Equal(ApiRegistrationResult.Accepted, left.Register(item, null));
        foreach (var item in items.Reverse()) Assert.Equal(ApiRegistrationResult.Accepted, right.Register(item, null));
        Assert.Equal(left.Seal().Select(x => x.Descriptor.StableKey),
            right.Seal().Select(x => x.Descriptor.StableKey));
    }

    [Fact]
    public void RegistryValidatesOwnerVersionAndProviderIdentityBeforeActivation()
    {
        var d = Metric() with
        {
            MinimumOwnerVersion = "1.2.0", MaximumOwnerVersionExclusive = "2.0.0",
        };
        var registry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(d, null));
        var descriptor = Assert.Single(registry.Seal()).Descriptor;
        Assert.Equal(ModProviderAvailability.MissingPackage,
            ModProviderDiscovery.Resolve(descriptor, Array.Empty<ModPackageInfo>()).State);
        Assert.Equal(ModProviderAvailability.MissingVersion,
            ModProviderDiscovery.Resolve(descriptor, new[] { new ModPackageInfo("other.hospitality", null) }).State);
        Assert.Equal(ModProviderAvailability.IncompatibleVersion,
            ModProviderDiscovery.Resolve(descriptor, new[] { new ModPackageInfo("other.hospitality", "2.0.0") }).State);
        Assert.Equal(ModProviderAvailability.Available,
            ModProviderDiscovery.Resolve(descriptor, new[] { new ModPackageInfo("other.hospitality", "1.3.0") }).State);
    }

    [Fact]
    public void SnapshotRequiresReaderAndDoesNotFabricateMissingValues()
    {
        var registry = new ExternalSdkMetricRegistry();
        var d = Metric("guests") with { FamilyId = "state", Kind = ApiMetricKind.SaveSnapshot };
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, registry.Register(d, null));
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(d, () => null));
        var metric = Assert.Single(registry.Seal());
        Assert.Null(ExternalSdkMetricPolicy.Snapshot(metric));
        var valid = new RegisteredSdkMetric(metric.Api, metric.Descriptor, () => 4.25);
        Assert.Equal(4.25, ExternalSdkMetricPolicy.Snapshot(valid)!.Values[MetricEntry.Scalar].Number);
        Assert.Throws<InvalidOperationException>(() =>
            ExternalSdkMetricPolicy.Snapshot(new RegisteredSdkMetric(metric.Api, metric.Descriptor, () => double.NaN)));
    }

    [Fact]
    public void EventsDeduplicateAfterSaveLoadAndAcrossForksAndNeverPersistRawIds()
    {
        var metric = Metric();
        var registry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(metric, null));
        var d = Assert.Single(registry.Seal());
        var spec = new[] { new FamilySpec(d.Descriptor.AcquisitionId, d.Descriptor.Revision) };
        var experimental = new HashSet<string> { d.Descriptor.AcquisitionId };
        var live = LiveMetricAccumulator.StartNew(spec, 0, experimental);
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.Record(live.Family(d.Descriptor.AcquisitionId)!, metric,
            "opaque-guest-event-key", 10, 1));
        Assert.Equal(ApiRecordResult.Duplicate, ExternalSdkMetricPolicy.Record(live.Family(d.Descriptor.AcquisitionId)!, metric,
            "opaque-guest-event-key", 20, 1));
        var draft = live.PrepareSave(100, DateTime.UtcNow, "parent")!;
        Assert.DoesNotContain("opaque-guest-event-key", draft.Json);
        Assert.Equal(CheckpointParseStatus.Ok, CheckpointJson.Parse(draft.Json).Status);
        var forkA = LiveMetricAccumulator.Resume(draft.Json, spec, 100, experimental);
        var forkB = LiveMetricAccumulator.Resume(draft.Json, spec, 100, experimental);
        Assert.Equal(ApiRecordResult.Duplicate, ExternalSdkMetricPolicy.Record(forkA.Family(d.Descriptor.AcquisitionId)!, metric,
            "opaque-guest-event-key", 101, 1));
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.Record(forkA.Family(d.Descriptor.AcquisitionId)!, metric,
            "next", 102, 1));
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.Record(forkB.Family(d.Descriptor.AcquisitionId)!, metric,
            "other", 102, 1));
        Assert.Equal(2, forkA.Family(d.Descriptor.AcquisitionId)!.Peek("sdk.value.checkouts")!.N);
        Assert.Equal(2, forkB.Family(d.Descriptor.AcquisitionId)!.Peek("sdk.value.checkouts")!.N);
        Assert.Equal(1, draft.Payload!.Families[d.Descriptor.AcquisitionId].Peek("sdk.value.checkouts")!.N);
    }

    [Fact]
    public void EventMeanIsNumericallyCorrectAndMissingIntervalsStayPartial()
    {
        var d = Metric("satisfaction", ApiEventMode.Mean);
        var registry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(d, null));
        var registered = Assert.Single(registry.Seal());
        var spec = new[] { new FamilySpec(registered.Descriptor.AcquisitionId, 1) };
        var acc = LiveMetricAccumulator.StartNew(spec, 0);
        var state = acc.Family(registered.Descriptor.AcquisitionId)!;
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.Record(state, d, "first", 10, 0.5));
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.Record(state, d, "second", 11, 0.9));
        var checkpoint = acc.PrepareSave(20, DateTime.UtcNow, "id")!;
        acc.Commit(checkpoint);
        var resumed = LiveMetricAccumulator.Resume(checkpoint.Json, Array.Empty<FamilySpec>(), 20);
        var paused = resumed.PrepareSave(30, DateTime.UtcNow, "paused")!;
        resumed.Commit(paused);
        var active = LiveMetricAccumulator.Resume(paused.Json, spec, 30);
        var output = new Dictionary<string, MetricEntry>();
        var family = active.Family(registered.Descriptor.AcquisitionId)!;
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(family, 30, output), registered);
        var value = output[registered.Descriptor.StableKey].Values[MetricEntry.Scalar];
        Assert.Equal(0.7, value.RealValue!.Value, 6);
        Assert.Equal(1.4, value.Numerator!.Value, 6);
        Assert.Equal(2, value.Denominator);
        Assert.Equal(MetricCoverage.Partial, value.Coverage);
    }

    [Fact]
    public void RegistryRejectsFamilySchemaMismatchAndLateMissingEvent()
    {
        var registry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(Metric(), null));
        Assert.Equal(ApiRegistrationResult.ConflictingFamily, registry.Register(Metric("other") with { Revision = 2 }, null));
        var state = new FamilyState { Revision = 1 };
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.Record(state, Metric(), "", 1, 1));
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.Record(state, Metric(), "ok", 1, double.PositiveInfinity));
        Assert.Empty(state.Metrics);
    }

    [Fact]
    public void EventDedupCapacityFailsClosedAndMarksOutputPartial()
    {
        var api = Metric();
        var registry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(api, null));
        var registered = Assert.Single(registry.Seal());
        var state = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 5000, Continuous = true };
        for (var i = 0; i < FamilyOps.KeyCap; i++)
            Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.Record(state, api, "event-" + i, i, 1));
        Assert.Equal(ApiRecordResult.CapExceeded, ExternalSdkMetricPolicy.Record(state, api, "overflow", 5000, 1));
        Assert.Equal(ApiRecordResult.CapExceeded, ExternalSdkMetricPolicy.Record(state, api, "event-0", 5000, 1));
        Assert.Equal((long)FamilyOps.KeyCap, state.Peek("sdk.value.checkouts")!.N);
        var output = new Dictionary<string, MetricEntry>();
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(state, 5000, output), registered);
        Assert.Equal(MetricCoverage.Partial, output[registered.Descriptor.StableKey].Values[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void SnapshotCannotClaimRatioWithoutNumeratorDenominator()
    {
        var reg = new ExternalSdkMetricRegistry();
        var snapshot = Metric("state") with { FamilyId = "state", Kind = ApiMetricKind.SaveSnapshot, Overall = "Ratio" };
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, reg.Register(snapshot, () => 0.7));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, reg.Register(snapshot with { Overall = "Sum", EventMode = ApiEventMode.Mean }, () => 0.7));
    }

    [Fact]
    public void OldProfileDataSurvivesAddingSdkDescriptorAndUnknownProperties()
    {
        var profile = new Profile();
        var original = ProfileJson.Serialize(profile);
        var existing = ProfileJson.Deserialize(original);
        var registry = new ExternalSdkMetricRegistry();
        registry.Register(Metric(), null);
        ProfileMutations.RememberModMetrics(existing, registry.Seal().Select(x => x.Descriptor));
        var roundtrip = ProfileJson.Deserialize(ProfileJson.Serialize(existing));
        Assert.Single(roundtrip.ModMetricDescriptors);
        Assert.Equal("mod.other.hospitality.stays.checkouts", roundtrip.ModMetricDescriptors[0].StableKey);
    }
}
