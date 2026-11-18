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

    // The built-in VFE Security integration (VfeSecurityStatistics): one unit, one metric.
    private static ModMetricDescriptor BuiltInTrapRearm() => new()
    {
        ProviderId = "rws.traprearm", OwnerPackageId = "VanillaExpanded.VFESecurity", FamilyId = "traprearm",
        MetricName = "completed", Revision = 1, Category = MetricCategory.Defense, Unit = MetricUnit.Count,
        Source = MetricObservationSource.Checkpoint, Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        RequiredPackages = [new ModPackageRequirement("VanillaExpanded.VFESecurity")],
    };

    private static ApiMetricDefinition SecurityMetric(string family, string name) => Metric(name) with
    {
        ProviderId = "third.party.security", OwnerPackageId = "VanillaExpanded.VFESecurity", FamilyId = family,
    };

    [Fact]
    public void BuiltInUnitIsRefusedToSdkWhileTheBuiltInAndOtherSdkUnitsKeepRecording()
    {
        var builtIn = BuiltInTrapRearm();
        var reads = 0;
        var registry = new ExternalSdkMetricRegistry(() => { reads++; return [builtIn]; });

        // An SDK metric in the built-in unit is refused, whatever provider ID it claims: the built-in family reads only its own.
        Assert.Equal(ApiRegistrationResult.ConflictingFamily,
            registry.Register(SecurityMetric("traprearm", "extra"), null, out var unit));
        Assert.Equal(builtIn.AcquisitionId, unit);
        Assert.Equal(ApiRegistrationResult.ConflictingFamily,
            registry.Register(SecurityMetric("traprearm", "extra") with { ProviderId = builtIn.ProviderId }, null, out _));
        Assert.Equal(ApiRegistrationResult.ConflictingFamily, registry.Register(
            SecurityMetric("traprearm", "stock") with { Kind = ApiMetricKind.SaveSnapshot }, () => 1, out _));
        Assert.Equal(ApiRegistrationResult.DuplicateMetric,
            registry.Register(SecurityMetric("TrapRearm", "Completed"), null, out unit));
        Assert.Equal(builtIn.AcquisitionId, unit);
        // Another unit of the same MOD and an unrelated provider are not affected.
        Assert.Equal(ApiRegistrationResult.Accepted,
            registry.Register(SecurityMetric("traprearm_sdk", "extra"), null, out unit));
        Assert.Null(unit);
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(Metric(), null, out unit));
        Assert.Null(unit);
        Assert.Equal(1, reads);
        var accepted = registry.Seal();
        Assert.Equal(["mod.other.hospitality.stays.checkouts", "mod.vanillaexpanded.vfesecurity.traprearm_sdk.extra"],
            accepted.Select(x => x.Descriptor.StableKey));
        Assert.Null(registry.Find(builtIn.AcquisitionId + ".extra"));

        // Every event the SDK is told was Recorded is read back from the checkpoint, next to the built-in count.
        var extra = accepted[1];
        var spec = new[]
        {
            new FamilySpec(builtIn.AcquisitionId, builtIn.Revision),
            new FamilySpec(extra.Descriptor.AcquisitionId, extra.Descriptor.Revision),
        };
        var experimental = spec.Select(x => x.Name).ToHashSet();
        var live = LiveMetricAccumulator.StartNew(spec, 0, experimental);
        Assert.True(OptionalTransitionRecording.RecordCount(live.Family(builtIn.AcquisitionId)!, "completed"));
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.Record(live.Family(extra.Descriptor.AcquisitionId)!, extra, "trap-1", 10, 1));
        var draft = live.PrepareSave(100, DateTime.UtcNow, "parent")!;
        var resumed = LiveMetricAccumulator.Resume(draft.Json, spec, 100, experimental);
        var output = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(resumed.Family(builtIn.AcquisitionId)!, 100, output),
            "completed", builtIn.StableKey);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(resumed.Family(extra.Descriptor.AcquisitionId)!, 100, output), extra);
        Assert.Equal(1, output[builtIn.StableKey].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1, output[extra.Descriptor.StableKey].Values[MetricEntry.Scalar].IntegerValue);
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

    [Fact]
    public void ApiVersionNegotiation_SupportsMinorZeroOneTwoAndRejectsFutureMinorOrWrongMajor()
    {
        Assert.Equal(1, StatisticsApi.ApiVersionMajor);
        Assert.Equal(2, StatisticsApi.ApiVersionMinor);
        Assert.Equal(new Version(1, 0, 0, 0), typeof(StatisticsApi).Assembly.GetName().Version);

        _ = StatisticsApi.InstallHost(new StubHost12());

        Assert.True(StatisticsApi.TryGetHost(1, 0, out var h0) && h0 is not null);
        Assert.True(StatisticsApi.TryGetHost(1, 1, out var h1) && h1 is not null);
        Assert.True(StatisticsApi.TryGetHost(1, 2, out var h2) && h2 is not null);
        Assert.Equal(2, h2!.ApiMinor);

        Assert.False(StatisticsApi.TryGetHost(1, 3, out var future));
        Assert.Null(future);
        Assert.False(StatisticsApi.TryGetHost(1, -1, out _));
        Assert.False(StatisticsApi.TryGetHost(2, 0, out _));
        Assert.False(StatisticsApi.TryGetHost(0, 0, out _));
    }

    [Fact]
    public void PersistedOwnerDisplayName_SurvivesModRemovalBackfillsLegacyDescriptorsAndNeverAllowsRawPackageId()
    {
        var reg = new ExternalSdkMetricRegistry();
        // Raw packageId, untrimmed text, control chars, and >128 chars are rejected.
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            reg.Register(Metric("bad1") with { OwnerDisplayName = "other.hospitality" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            reg.Register(Metric("bad2") with { OwnerDisplayName = "OTHER.HOSPITALITY" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            reg.Register(Metric("bad3") with { OwnerDisplayName = " Padded Title " }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            reg.Register(Metric("bad4") with { OwnerDisplayName = "Bad\nTitle" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            reg.Register(Metric("bad5") with { OwnerDisplayName = new string('A', 129) }, null));

        // 1) Legacy v1.1 descriptor without OwnerDisplayName keeps generic "External mod" fallback after MOD removal.
        var legacyReg = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, legacyReg.Register(Metric(), null));
        var legacyDesc = Assert.Single(legacyReg.Seal()).Descriptor;
        Assert.Null(legacyDesc.OwnerDisplayName);

        var profile = new Profile();
        ProfileMutations.RememberModMetrics(profile, [legacyDesc]);
        var legacyDef = profile.ModMetricDescriptors[0].ToDefinition();
        Assert.Null(MetricAcquisitionLabels.SafeModDisplayName(legacyDef.OwnerPackageId, _ => null, legacyDef.OwnerDisplayName));

        // 2) Same-provider, same-revision descriptor with OwnerDisplayName backfills existing profile entry on save.
        var v12Reg = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted,
            v12Reg.Register(Metric() with { OwnerDisplayName = "Cozy Hospitality Addon" }, null));
        var namedDesc = Assert.Single(v12Reg.Seal()).Descriptor;
        ProfileMutations.RememberModMetrics(profile, [namedDesc]);

        var roundtrip = ProfileJson.Deserialize(ProfileJson.Serialize(profile));
        var persistedDesc = Assert.Single(roundtrip.ModMetricDescriptors);
        Assert.Equal("Cozy Hospitality Addon", persistedDesc.OwnerDisplayName);

        // 3) After the external MOD is uninstalled (activeModNameLookup returns null), UI helpers still resolve the persisted title!
        var defAfterRemoval = persistedDesc.ToDefinition();
        Assert.Equal("Cozy Hospitality Addon",
            MetricAcquisitionLabels.SafeModDisplayName(defAfterRemoval.OwnerPackageId, _ => null, defAfterRemoval.OwnerDisplayName));
        Assert.Equal("Cozy Hospitality Addon",
            MetricAcquisitionLabels.UnitLabel(defAfterRemoval.Acquisition, defAfterRemoval.OwnerPackageId, _ => null, k => k, defAfterRemoval.OwnerDisplayName));
        Assert.Equal("Cozy Hospitality Addon",
            MetricAcquisitionLabels.ResolveGroupLabel(defAfterRemoval, _ => null, k => k, c => c.ToString(), c => "External: " + c));

        var groups = MetricAcquisitionLabels.BuildGroups(
            [
                new MetricStatus(
                    defAfterRemoval,
                    MetricAvailability.AllMeasuredColonies,
                    false,
                    MetricVerification.Experimental,
                    UnitRecordingState.TargetModMissing,
                    1,
                    1,
                    Array.Empty<string>()),
            ],
            _ => null,
            k => k,
            c => c.ToString(),
            c => "External: " + c,
            StringComparer.Ordinal);
        var group = Assert.Single(groups);
        Assert.Equal("Cozy Hospitality Addon", group.Label);
        Assert.Equal("Cozy Hospitality Addon", group.OwnerDisplayName);

        // 4) Revision bump without OwnerDisplayName preserves the previously remembered display name.
        ProfileMutations.RememberModMetrics(roundtrip, [legacyDesc with { Revision = 2 }]);
        Assert.Equal(2, roundtrip.ModMetricDescriptors[0].Revision);
        Assert.Equal("Cozy Hospitality Addon", roundtrip.ModMetricDescriptors[0].OwnerDisplayName);
    }

    [Fact]
    public void BreakdownEventRecording_SupportsCountAndSumDeduplicatesForksAndRejectsScalarMismatch()
    {
        var reg = new ExternalSdkMetricRegistry();
        // Breakdown is rejected on SaveSnapshot and Mean.
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            reg.Register(Metric("snap") with { FamilyId = "state", Kind = ApiMetricKind.SaveSnapshot, HasBreakdown = true }, () => 1));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            reg.Register(Metric("avg", ApiEventMode.Mean) with { HasBreakdown = true }, null));

        var countApi = Metric("by_room") with { HasBreakdown = true, OwnerDisplayName = "Hotel Expanded" };
        var sumApi = Metric("revenue_by_room", ApiEventMode.Sum) with { HasBreakdown = true, OwnerDisplayName = "Hotel Expanded", Unit = "Silver" };
        var plainApi = Metric("plain_checkouts");
        Assert.Equal(ApiRegistrationResult.Accepted, reg.Register(countApi, null));
        Assert.Equal(ApiRegistrationResult.Accepted, reg.Register(sumApi, null));
        Assert.Equal(ApiRegistrationResult.Accepted, reg.Register(plainApi, null));

        var sealedMetrics = reg.Seal().ToDictionary(m => m.Descriptor.MetricName, StringComparer.Ordinal);
        var countReg = sealedMetrics["by_room"];
        var sumReg = sealedMetrics["revenue_by_room"];
        var plainReg = sealedMetrics["plain_checkouts"];

        var spec = new[] { new FamilySpec(countReg.Descriptor.AcquisitionId, 1) };
        var live = LiveMetricAccumulator.StartNew(spec, 0);
        var state = live.Family(spec[0].Name)!;

        // Scalar Record on HasBreakdown=true and RecordBreakdown on HasBreakdown=false must both fail closed with InvalidEvent.
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.Record(state, countReg, "ev-scalar", 10, 1));
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.RecordBreakdown(state, plainReg, "ev-b", "suite", 10, 1));

        // Malformed breakdown keys (empty, whitespace-padded, control chars, >96 chars) and negative/non-unit values are rejected.
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.RecordBreakdown(state, countReg, "ev-bad1", "", 10, 1));
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.RecordBreakdown(state, countReg, "ev-bad2", " suite ", 10, 1));
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.RecordBreakdown(state, countReg, "ev-bad3", "suite\t1", 10, 1));
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.RecordBreakdown(state, countReg, "ev-bad4", new string('k', 97), 10, 1));
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.RecordBreakdown(state, countReg, "ev-bad5", "suite", 10, 2));
        Assert.Equal(ApiRecordResult.InvalidEvent, ExternalSdkMetricPolicy.RecordBreakdown(state, sumReg, "ev-bad6", "suite", 10, -5));

        // Valid breakdown events record and deduplicate across checkpoint save/load forks without persisting raw event IDs.
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.RecordBreakdown(state, countReg, "secret-evt-1", "suite_luxury", 100, 1));
        Assert.Equal(ApiRecordResult.Duplicate, ExternalSdkMetricPolicy.RecordBreakdown(state, countReg, "secret-evt-1", "suite_luxury", 105, 1));
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.RecordBreakdown(state, countReg, "secret-evt-2", "dormitory", 110, 1));
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.RecordBreakdown(state, sumReg, "rev-1", "suite_luxury", 100, 120.5));
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.RecordBreakdown(state, sumReg, "rev-2", "suite_luxury", 120, 79.5));

        var draft = live.PrepareSave(200, DateTime.UtcNow, "cp-root")!;
        Assert.DoesNotContain("secret-evt-1", draft.Json);

        var fork = LiveMetricAccumulator.Resume(draft.Json, spec, 200);
        var forkState = fork.Family(spec[0].Name)!;
        Assert.Equal(ApiRecordResult.Duplicate, ExternalSdkMetricPolicy.RecordBreakdown(forkState, countReg, "secret-evt-1", "suite_luxury", 210, 1));
        Assert.Equal(ApiRecordResult.Recorded, ExternalSdkMetricPolicy.RecordBreakdown(forkState, countReg, "secret-evt-3", "suite_luxury", 220, 1));

        var forkDraft = fork.PrepareSave(220, DateTime.UtcNow, "cp-fork")!;
        var savedForkState = forkDraft.Payload!.Families[spec[0].Name];
        var output = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        var ctx = new FamilyOutputContext(savedForkState, 220, output);
        ExternalSdkMetricPolicy.Build(ctx, countReg);
        ExternalSdkMetricPolicy.Build(ctx, sumReg);

        var countEntry = output[countReg.Descriptor.StableKey];
        Assert.Equal(3L, countEntry.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Complete, countEntry.Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(2L, countEntry.Values["suite_luxury"].IntegerValue);
        Assert.Equal(1L, countEntry.Values["dormitory"].IntegerValue);

        var sumEntry = output[sumReg.Descriptor.StableKey];
        Assert.Equal(200.0, sumEntry.Values[MetricEntry.Scalar].RealValue!.Value, 6);
        Assert.Equal(200.0, sumEntry.Values["suite_luxury"].RealValue!.Value, 6);
    }

    [Fact]
    public void BreakdownBucketCap_FailsClosedAt64BucketsMarksPartialAndNeverFabricatesZeroForAbsentBuckets()
    {
        var api = Metric("by_item") with { HasBreakdown = true };
        var reg = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, reg.Register(api, null));
        var registered = Assert.Single(reg.Seal());
        var def = registered.Descriptor.ToDefinition();

        var stateCol1 = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 60_000, Continuous = true };
        for (var i = 0; i < ExternalSdkMetricPolicy.BreakdownBucketCap; i++)
        {
            Assert.Equal(ApiRecordResult.Recorded,
                ExternalSdkMetricPolicy.RecordBreakdown(stateCol1, registered, $"evt-{i}", $"bucket-{i:D2}", i + 1, 1));
        }

        // Duplicate eventId at full bucket capacity still reports Duplicate; existing bucket still accepts new events!
        Assert.Equal(ApiRecordResult.Duplicate,
            ExternalSdkMetricPolicy.RecordBreakdown(stateCol1, registered, "evt-0", "bucket-00", 100, 1));
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.RecordBreakdown(stateCol1, registered, "evt-extra-existing", "bucket-00", 101, 1));

        var beforeOverflow = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(stateCol1, 60_000, beforeOverflow), registered);
        Assert.Equal(MetricCoverage.Complete, beforeOverflow[def.StoredKey].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(65L, beforeOverflow[def.StoredKey].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, beforeOverflow[def.StoredKey].Values["bucket-00"].IntegerValue);

        // 65th distinct bucket triggers CapExceeded, marks Overflow=true, and weakens scalar + all buckets to Partial!
        Assert.Equal(ApiRecordResult.CapExceeded,
            ExternalSdkMetricPolicy.RecordBreakdown(stateCol1, registered, "evt-overflow", "bucket-64", 102, 1));
        Assert.Equal(ApiRecordResult.CapExceeded,
            ExternalSdkMetricPolicy.RecordBreakdown(stateCol1, registered, "evt-after-overflow", "bucket-00", 103, 1));

        var col1Metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(stateCol1, 60_000, col1Metrics), registered);
        Assert.Equal(65L, col1Metrics[def.StoredKey].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, col1Metrics[def.StoredKey].Values[MetricEntry.Scalar].Coverage);
        Assert.True(col1Metrics[def.StoredKey].Values[MetricEntry.Scalar].Weakened);
        Assert.Equal(MetricCoverage.Partial, col1Metrics[def.StoredKey].Values["bucket-00"].Coverage);

        // Cross-colony check: Colony 2 has "bucket-99" (Complete, 4 events). Because Colony 1 overflowed (Partial),
        // Colony 1's missing "bucket-99" must NOT be treated as a known 0 — overall "bucket-99" must be Partial (4), not Complete!
        var stateCol2 = new FamilyState { Revision = 1, SinceTick = 0, ThroughTick = 60_000, Continuous = true };
        for (var i = 0; i < 4; i++)
            Assert.Equal(ApiRecordResult.Recorded,
                ExternalSdkMetricPolicy.RecordBreakdown(stateCol2, registered, $"c2-{i}", "bucket-99", i + 1, 1));
        var col2Metrics = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(stateCol2, 60_000, col2Metrics), registered);

        var profile = new Profile
        {
            ModMetricDescriptors = [registered.Descriptor],
            SaveObservations =
            [
                new SaveObservation
                {
                    FileName = "c1.rws", Status = ObservationStatus.Ok,
                    Identity = new ColonyIdentity { ModCampaignId = "c1" },
                    Values = new ObservedValues { TicksGame = 60_000 },
                },
                new SaveObservation
                {
                    FileName = "c2.rws", Status = ObservationStatus.Ok,
                    Identity = new ColonyIdentity { ModCampaignId = "c2" },
                    Values = new ObservedValues { TicksGame = 60_000 },
                },
            ],
            MetricObservations =
            [
                new MetricObservation
                {
                    Source = MetricObservationSource.Checkpoint, Key = "cp-c1", PayloadHash = "h1", SaveTick = 60_000,
                    ContinuousFamilies = [registered.Descriptor.AcquisitionId],
                    Identity = new ColonyIdentity { ModCampaignId = "c1" },
                    Metrics = col1Metrics,
                },
                new MetricObservation
                {
                    Source = MetricObservationSource.Checkpoint, Key = "cp-c2", PayloadHash = "h2", SaveTick = 60_000,
                    ContinuousFamilies = [registered.Descriptor.AcquisitionId],
                    Identity = new ColonyIdentity { ModCampaignId = "c2" },
                    Metrics = col2Metrics,
                },
            ],
        };

        var colonies = CampaignMatcher.BuildColonies(profile);
        var numeric = MetricCalculator.BuildView(
            profile,
            colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()),
            MetricRegistry.Compose(
                profile.ModMetricDescriptors,
                new HashSet<string>(StringComparer.Ordinal) { registered.Descriptor.AcquisitionId }));
        var overallRow = numeric.Overall.Single(r => r.Definition.Key == def.Key);
        var bucket99 = overallRow.Breakdowns.Single(b => b.Breakdown == "bucket-99");
        Assert.Equal(4.0, bucket99.Value.Number);
        Assert.Equal(MetricCoverage.Partial, bucket99.Value.Coverage);
        Assert.Equal(1, bucket99.Valid); // Only Colony 2 is counted as measured; Colony 1 was Unknown, NOT fabricated 0!
    }

    [Fact]
    public void BreakdownFastPath_HasBoundedPerEventAllocation()
    {
        var api = Metric("fast_breakdown") with { HasBreakdown = true };
        var reg = new ExternalSdkMetricRegistry();
        Assert.Equal(ApiRegistrationResult.Accepted, reg.Register(api, null));
        var registered = Assert.Single(reg.Seal());

        string[] buckets = ["alpha", "beta", "gamma", "delta"];
        string[] eventIds = Enumerable.Range(0, 256).Select(i => $"steady-event-{i:D4}").ToArray();

        // Warm the JIT and SHA-256 implementation outside the timed/allocation window.
        var warmState = new FamilyState { Revision = 1 };
        for (var i = 0; i < 128; i++)
            _ = ExternalSdkMetricPolicy.RecordBreakdown(warmState, registered, eventIds[i & 31], buckets[i & 3], i + 1, 1);

        // 1) Duplicate fast-path check: zero dictionary growth, allocates only the 64-char hex digest (~152 bytes/call).
        var dupOk = true;
        var dupBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 128; i++)
        {
            if (ExternalSdkMetricPolicy.RecordBreakdown(warmState, registered, eventIds[0], buckets[0], 500 + i, 1) != ApiRecordResult.Duplicate)
                dupOk = false;
        }
        var dupPerCall = (GC.GetAllocatedBytesForCurrentThread() - dupBefore) / 128.0;
        Assert.True(dupOk);
        Assert.True(dupPerCall <= 256.0, $"Expected duplicate fast-path <= 256 B/call, got {dupPerCall:F1} B/call.");

        // 2) Unique fast-path check across 200 events (amortizing Dictionary capacity growth): <= 384 B/event.
        var state = new FamilyState { Revision = 1 };
        for (var i = 0; i < 16; i++)
            Assert.Equal(ApiRecordResult.Recorded,
                ExternalSdkMetricPolicy.RecordBreakdown(state, registered, eventIds[i], buckets[i & 3], i + 1, 1));

        var uniqOk = true;
        var uniqBefore = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 16; i < 216; i++)
        {
            if (ExternalSdkMetricPolicy.RecordBreakdown(state, registered, eventIds[i], buckets[i & 3], i + 1, 1) != ApiRecordResult.Recorded)
                uniqOk = false;
        }
        var uniqPerCall = (GC.GetAllocatedBytesForCurrentThread() - uniqBefore) / 200.0;
        Assert.True(uniqOk);
        Assert.True(uniqPerCall <= 384.0, $"Expected unique fast-path <= 384 B/event, got {uniqPerCall:F1} B/event.");
    }

    private sealed class StubHost12 : IStatisticsHost
    {
        public int ApiMajor => StatisticsApi.ApiVersionMajor;
        public int ApiMinor => StatisticsApi.ApiVersionMinor;
        public ApiRegistrationResult Register(ApiMetricDefinition metric, Func<double?>? captureSnapshot = null) => ApiRegistrationResult.Accepted;
        public ApiRecordResult RecordEvent(string metricId, string uniqueEventId, double value = 1) => ApiRecordResult.Recorded;
        public ApiRecordResult RecordBreakdownEvent(string metricId, string uniqueEventId, string breakdownKey, double value = 1) => ApiRecordResult.Recorded;
    }
}
