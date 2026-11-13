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
    public void ChangingOnlyMetricCaseCannotResetSavedCountOrDedupHistory()
    {
        var original = Metric("Completed");
        var catalog = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, catalog.Register(original, null));
        var registration = Assert.Single(catalog.Seal());
        var spec = new[] { new FamilySpec(registration.Descriptor.AcquisitionId, 1) };
        var accumulator = LiveMetricAccumulator.StartNew(spec, 0);
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.Record(accumulator.Family(spec[0].Name)!, original, "guest-event-101", 25, 1));
        var saved = accumulator.PrepareSave(100, DateTime.UtcNow, "case-parent")!;
        accumulator.Commit(saved);

        var reloaded = LiveMetricAccumulator.Resume(saved.Json, spec, 100);
        var renamed = Metric("completed");
        var newRegistry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, newRegistry.Register(renamed, null));
        var changedRegistration = Assert.Single(newRegistry.Seal());
        var state = reloaded.Family(spec[0].Name)!;
        Assert.Equal(ApiRecordResult.Duplicate,
            ExternalSdkMetricPolicy.Record(state, renamed, "guest-event-101", 101, 1));
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.Record(state, renamed, "guest-event-102", 102, 1));
        var values = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(state, 110, values), changedRegistration);
        Assert.Equal(2, values[changedRegistration.Descriptor.StableKey].Values[MetricEntry.Scalar].Number);
    }

    [Fact]
    public void MeanWithoutSamplesAfterCoverageGapIsUnknownNotNotApplicable()
    {
        var definition = Metric("satisfaction", ApiEventMode.Mean);
        var registry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(definition, null));
        var registered = Assert.Single(registry.Seal());
        var state = new FamilyState { Revision = 1, SinceTick = 100, ThroughTick = 200, Continuous = false };
        var output = new Dictionary<string, MetricEntry>();
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(state, 200, output), registered);
        Assert.Equal(MetricCoverage.Unknown,
            output[registered.Descriptor.StableKey].Values[MetricEntry.Scalar].Coverage);

        state = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 200, Continuous = true };
        output.Clear();
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(state, 200, output), registered);
        Assert.Equal(MetricCoverage.NotApplicable,
            output[registered.Descriptor.StableKey].Values[MetricEntry.Scalar].Coverage);
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
    public void OverflowedLegacyDigestSetRemainsFailClosedAcrossCheckpointForks()
    {
        var definition = Metric("receipt");
        var registry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(definition, null));
        var metric = Assert.Single(registry.Seal());
        var family = new FamilySpec(metric.Descriptor.AcquisitionId, metric.Descriptor.Revision);
        var live = LiveMetricAccumulator.StartNew([family], 0);
        var state = live.Family(family.Name)!;
        for (var i = 0; i < FamilyOps.KeyCap; i++)
            Assert.Equal(ApiRecordResult.Recorded,
                ExternalSdkMetricPolicy.Record(state, metric, "receipt-" + i, i + 1, 1));
        Assert.Equal(ApiRecordResult.CapExceeded,
            ExternalSdkMetricPolicy.Record(state, metric, "receipt-overflow", 5000, 1));

        var saved = live.PrepareSave(5000, DateTime.UtcNow, "checkpoint")!;
        Assert.True(saved.IsNew);
        Assert.DoesNotContain("receipt-overflow", saved.Json);
        foreach (var fork in new[]
        {
            LiveMetricAccumulator.Resume(saved.Json, [family], 5000),
            LiveMetricAccumulator.Resume(saved.Json, [family], 5000),
        })
        {
            var restored = fork.Family(family.Name)!;
            Assert.True(restored.Peek(metric.SeenAccumulatorKey)!.Overflow);
            // No information is discarded and neither a previously seen ID
            // nor a new ID may silently inflate an exact-looking lifetime count.
            Assert.Equal(ApiRecordResult.CapExceeded,
                ExternalSdkMetricPolicy.Record(restored, metric, "receipt-0", 5001, 1));
            Assert.Equal(ApiRecordResult.CapExceeded,
                ExternalSdkMetricPolicy.Record(restored, metric, "receipt-after", 5002, 1));
            Assert.Equal(FamilyOps.KeyCap, restored.Peek(metric.ValueAccumulatorKey)!.N);
            var output = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
            ExternalSdkMetricPolicy.Build(new FamilyOutputContext(restored, 5002, output), metric);
            Assert.Equal(MetricCoverage.Partial,
                output[metric.Descriptor.StableKey].Values[MetricEntry.Scalar].Coverage);
        }
    }

    [Fact]
    public void SnapshotCannotClaimRatioWithoutNumeratorDenominator()
    {
        var reg = new ExternalSdkMetricRegistry();
        var snapshot = Metric("state") with { FamilyId = "state", Kind = ApiMetricKind.SaveSnapshot, Overall = "Ratio" };
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, reg.Register(snapshot, () => 0.7));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, reg.Register(snapshot with { Overall = "Sum", EventMode = ApiEventMode.Mean }, () => 0.7));
    }

    [Theory]
    [InlineData(ApiEventMode.Count, 1.0)]
    [InlineData(ApiEventMode.Sum, 4.5)]
    [InlineData(ApiEventMode.Mean, 4.5)]
    public void RegisteredEventFastPathKeepsCheckpointKeysDigestAndDedupSemantics(ApiEventMode mode, double amount)
    {
        var metric = Metric("ReCeIpTs", mode);
        var catalog = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, catalog.Register(metric, null));
        var registered = Assert.Single(catalog.Seal());
        Assert.Equal("sdk.seen.receipts", registered.SeenAccumulatorKey);
        Assert.Equal("sdk.value.receipts", registered.ValueAccumulatorKey);
        Assert.Same(registered.SeenAccumulatorKey, registered.SeenAccumulatorKey);

        var legacy = new FamilyState { Revision = 1 };
        var fast = new FamilyState { Revision = 1 };
        var ids = new[] { "receipt-A", "ゲスト-😀", "receipt-A", "receipt-B" };
        var tick = 2;
        foreach (var id in ids)
        {
            var expected = ExternalSdkMetricPolicy.Record(legacy, metric, id, tick, amount);
            var actual = ExternalSdkMetricPolicy.Record(fast, registered, id, tick, amount);
            Assert.Equal(expected, actual);
            tick += 10;
        }
        Assert.Equal(legacy.Peek("sdk.value.receipts")!.N, fast.Peek("sdk.value.receipts")!.N);
        Assert.Equal(legacy.Peek("sdk.value.receipts")!.Sum, fast.Peek("sdk.value.receipts")!.Sum);
        Assert.Equal(legacy.Peek("sdk.value.receipts")!.First, fast.Peek("sdk.value.receipts")!.First);
        Assert.Equal(legacy.Peek("sdk.value.receipts")!.Last, fast.Peek("sdk.value.receipts")!.Last);
        Assert.Equal(
            legacy.Peek("sdk.seen.receipts")!.By!.Keys.Order(StringComparer.Ordinal),
            fast.Peek("sdk.seen.receipts")!.By!.Keys.Order(StringComparer.Ordinal));

        Assert.Equal(ApiRecordResult.InvalidEvent,
            ExternalSdkMetricPolicy.Record(fast, registered, "bad", 30, double.NaN));
        Assert.Equal(ApiRecordResult.InvalidEvent,
            ExternalSdkMetricPolicy.Record(fast, registered, new string('x', 129), 30, amount));
        Assert.Equal(ApiRecordResult.InvalidEvent,
            ExternalSdkMetricPolicy.Record(fast, registered, "negative-tick", -1, amount));
        Assert.Equal(3L, fast.Peek("sdk.value.receipts")!.N);
    }

    [Fact]
    public void RegisteredFastPathPreservesOverflowAndSaveRestoreHistory()
    {
        var metric = Metric("Checkout");
        var registry = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(metric, null));
        var registered = Assert.Single(registry.Seal());
        var id = registered.Descriptor.AcquisitionId;
        var spec = new[] { new FamilySpec(id, 1) };
        var acc = LiveMetricAccumulator.StartNew(spec, 0);
        var family = acc.Family(id)!;
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.Record(family, registered, "event-one", 1, 1));
        var save = acc.PrepareSave(50, DateTime.UtcNow, "fast-parent")!;
        var resumed = LiveMetricAccumulator.Resume(save.Json, spec, 50);
        var state = resumed.Family(id)!;
        Assert.Equal(ApiRecordResult.Duplicate,
            ExternalSdkMetricPolicy.Record(state, registered, "event-one", 51, 1));
        for (var i = 1; i < FamilyOps.KeyCap; ++i)
            Assert.Equal(ApiRecordResult.Recorded,
                ExternalSdkMetricPolicy.Record(state, registered, "new-" + i, 52 + i, 1));
        Assert.Equal(ApiRecordResult.CapExceeded,
            ExternalSdkMetricPolicy.Record(state, registered, "over", 5000, 1));
        Assert.Equal(FamilyOps.KeyCap, state.Peek(registered.ValueAccumulatorKey)!.N);
        Assert.True(state.Peek(registered.SeenAccumulatorKey)!.Overflow);
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
