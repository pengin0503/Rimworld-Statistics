using System.Reflection;
using RimworldStatistics.Api;
using RimworldStatistics.Core.Tests.Fixtures;
using StatisticsSdkSample;

namespace RimworldStatistics.Core.Tests;

[Collection("ExternalSdkHostCollection")]
public sealed class SdkContractAndSampleTests
{
    private sealed class BadMajorHost : IStatisticsHost
    {
        public int ApiMajor => 2;
        public int ApiMinor => 0;
        public ApiRegistrationResult Register(ApiMetricDefinition metric, Func<double?>? captureSnapshot = null) => ApiRegistrationResult.Accepted;
        public ApiRecordResult RecordEvent(string metricId, string uniqueEventId, double value = 1) => ApiRecordResult.Recorded;
        public ApiRecordResult RecordBreakdownEvent(string metricId, string uniqueEventId, string breakdownKey, double value = 1) => ApiRecordResult.Recorded;
    }

    private sealed class FutureMinorHost : IStatisticsHost
    {
        public int ApiMajor => StatisticsApi.ApiVersionMajor;
        public int ApiMinor => StatisticsApi.ApiVersionMinor + 1;
        public ApiRegistrationResult Register(ApiMetricDefinition metric, Func<double?>? captureSnapshot = null) => ApiRegistrationResult.Accepted;
        public ApiRecordResult RecordEvent(string metricId, string uniqueEventId, double value = 1) => ApiRecordResult.Recorded;
        public ApiRecordResult RecordBreakdownEvent(string metricId, string uniqueEventId, string breakdownKey, double value = 1) => ApiRecordResult.Recorded;
    }

    private sealed class ValidHost12 : IStatisticsHost
    {
        public int ApiMajor => 1;
        public int ApiMinor => 2;
        public ApiRegistrationResult Register(ApiMetricDefinition metric, Func<double?>? captureSnapshot = null) => ApiRegistrationResult.Accepted;
        public ApiRecordResult RecordEvent(string metricId, string uniqueEventId, double value = 1) => ApiRecordResult.Recorded;
        public ApiRecordResult RecordBreakdownEvent(string metricId, string uniqueEventId, string breakdownKey, double value = 1) => ApiRecordResult.Recorded;
    }

    [Fact]
    public void ApiAssembly_PublicAbiSurfaceMatchesExpectedV12Contract()
    {
        var apiAssembly = typeof(StatisticsApi).Assembly;
        var exportedTypes = apiAssembly.GetExportedTypes().Select(t => t.FullName).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal([
            "RimworldStatistics.Api.ApiEventMode",
            "RimworldStatistics.Api.ApiMetricDefinition",
            "RimworldStatistics.Api.ApiMetricKind",
            "RimworldStatistics.Api.ApiPackageRequirement",
            "RimworldStatistics.Api.ApiRecordResult",
            "RimworldStatistics.Api.ApiRegistrationResult",
            "RimworldStatistics.Api.IStatisticsHost",
            "RimworldStatistics.Api.StatisticsApi",
        ], exportedTypes);

        Assert.Equal(1, StatisticsApi.ApiVersionMajor);
        Assert.Equal(2, StatisticsApi.ApiVersionMinor);
        Assert.Equal(["Event", "SaveSnapshot"], Enum.GetNames<ApiMetricKind>());
        Assert.Equal(["Count", "Sum", "Mean"], Enum.GetNames<ApiEventMode>());
        Assert.Equal(["Accepted", "InvalidDefinition", "DuplicateMetric", "ConflictingFamily", "Closed", "Failed"],
            Enum.GetNames<ApiRegistrationResult>());
        Assert.Equal(["Recorded", "Duplicate", "Disabled", "InvalidEvent", "CapExceeded", "Failed"],
            Enum.GetNames<ApiRecordResult>());

        var hostMethods = typeof(IStatisticsHost)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(m => m.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["RecordBreakdownEvent", "RecordEvent", "Register", "get_ApiMajor", "get_ApiMinor"], hostMethods);
    }

    [Fact]
    public void StatisticsApi_InstallHost_RejectsNullWrongMajorFutureMinorAndDuplicateHost()
    {
        Assert.False(StatisticsApi.InstallHost(null!));
        Assert.False(StatisticsApi.InstallHost(new BadMajorHost()));
        Assert.False(StatisticsApi.InstallHost(new FutureMinorHost()));

        // Ensure a valid host is installed once for the process; any second install must fail.
        _ = StatisticsApi.InstallHost(new ValidHost12());
        Assert.False(StatisticsApi.InstallHost(new ValidHost12()));
        Assert.True(StatisticsApi.TryGetHost(1, 2, out var resolved));
        Assert.NotNull(resolved);
    }

    [Fact]
    public void ApiMetricDefinition_HandlesNullIdentifiersWithoutThrowing()
    {
        var def = new ApiMetricDefinition
        {
            OwnerPackageId = null!,
            FamilyId = null!,
            MetricName = null!,
        };

        Assert.Equal("mod...", def.MetricId);
        Assert.Equal("mod..", def.FamilyAcquisitionId);
    }

    [Fact]
    public void SampleSdkClient_AndEventLedger_ExecuteScalarBreakdownAndFaultInjectionPaths()
    {
        _ = StatisticsApi.InstallHost(new ValidHost12());

        Assert.True(SampleSdkClient.TryAttach());
        SampleSdkClient.SetCurrentCount(7);

        var ledger = new SampleEventLedger("session-abc", 0);
        Assert.True(ledger.Emit(SampleSdkClient.OnConfirmedEvent));
        Assert.Equal(1, ledger.Count);
        Assert.True(SampleSdkClient.OnConfirmedBreakdownEvent("session-abc:1:b", "sample_kind", 1));

        // Arming and disarming snapshot exception does not disrupt subsequent calls.
        SampleSdkClient.ArmSnapshotExceptionOnce();
        var capture = typeof(SampleSdkClient).GetMethod("CaptureSnapshot", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Throws<TargetInvocationException>(() => capture.Invoke(null, null));
        Assert.Equal(7.0, (double?)capture.Invoke(null, null));
    }

    [Fact]
    public void ExternalSdkMetricRegistry_RejectsNumericEnumStringsAndReservedBuiltInUnits()
    {
        var registry = new ExternalSdkMetricRegistry();
        registry.ReserveBuiltInUnits(["mod.orion.hospitality.visits"]);

        ApiMetricDefinition BaseDef(string family = "custom", string metric = "events") => new()
        {
            ProviderId = "test.provider",
            OwnerPackageId = "thirdparty.mod",
            FamilyId = family,
            MetricName = metric,
        };

        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            registry.Register(BaseDef() with { Category = "0" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            registry.Register(BaseDef() with { Unit = "1" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            registry.Register(BaseDef() with { Overall = "0" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            registry.Register(BaseDef() with { Selection = "0" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            registry.Register(BaseDef() with { Unit = "Ranking" }, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            registry.Register(BaseDef() with { Selection = "ObservationMaxByBreakdown" }, null));

        var reservedAttempt = BaseDef() with
        {
            OwnerPackageId = "Orion.Hospitality",
            FamilyId = "visits",
            MetricName = "external_extra",
        };
        Assert.Equal(ApiRegistrationResult.ConflictingFamily,
            registry.Register(reservedAttempt, null, out var ownerUnit));
        Assert.Equal("mod.orion.hospitality.visits", ownerUnit);
    }

    [Fact]
    public void ExternalSdkMetricPolicy_IsolatesGapsPerMetric_SupportsSignedScalarsAndSumBreakdowns()
    {
        var registry = new ExternalSdkMetricRegistry();
        var sumBreakdown = new ApiMetricDefinition
        {
            ProviderId = "vendor.economy",
            OwnerPackageId = "vendor.mod",
            OwnerDisplayName = "Vendor Mod",
            FamilyId = "trade",
            MetricName = "silver_by_good",
            EventMode = ApiEventMode.Sum,
            HasBreakdown = true,
            Unit = "Silver",
        };
        var siblingScalar = new ApiMetricDefinition
        {
            ProviderId = "vendor.economy",
            OwnerPackageId = "vendor.mod",
            OwnerDisplayName = "Vendor Mod",
            FamilyId = "trade",
            MetricName = "net_profit",
            EventMode = ApiEventMode.Sum,
            Unit = "Silver",
        };

        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(sumBreakdown, null));
        Assert.Equal(ApiRegistrationResult.Accepted, registry.Register(siblingScalar, null));
        registry.Seal();

        var regBreakdown = registry.Find(sumBreakdown.MetricId)!;
        var regSibling = registry.Find(siblingScalar.MetricId)!;

        // Colony 1: complete Sum breakdown + signed negative scalar Sum + per-metric gap only on breakdown.
        var state1 = new FamilyState { SinceTick = 0, ThroughTick = 200, Continuous = true };
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.RecordBreakdown(state1, regBreakdown, "b1", "steel", 100, 150.5));
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.RecordBreakdown(state1, regBreakdown, "b2", "gold", 110, 49.5));
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.Record(state1, regSibling, "s1", 120, -42.5));

        // Simulate InvalidEvent isolation on breakdown metric only; sibling stays Complete.
        state1.Acc(regBreakdown.GapAccumulatorKey).Overflow = true;

        var obs1 = TestProfileBuilders.Checkpoint("cp-1", null, "c1", 200);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(state1, 200, obs1.Metrics), regBreakdown);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(state1, 200, obs1.Metrics), regSibling);

        Assert.Equal(MetricCoverage.Partial, obs1.Metrics[regBreakdown.Descriptor.StableKey].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(MetricCoverage.Complete, obs1.Metrics[regSibling.Descriptor.StableKey].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(-42.5, obs1.Metrics[regSibling.Descriptor.StableKey].Values[MetricEntry.Scalar].Number);

        // Colony 2: complete Sum breakdown with only "steel".
        var state2 = new FamilyState { SinceTick = 0, ThroughTick = 200, Continuous = true };
        Assert.Equal(ApiRecordResult.Recorded,
            ExternalSdkMetricPolicy.RecordBreakdown(state2, regBreakdown, "b3", "steel", 150, 200.0));
        var obs2 = TestProfileBuilders.Checkpoint("cp-2", null, "c2", 200);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(state2, 200, obs2.Metrics), regBreakdown);
        ExternalSdkMetricPolicy.Build(new FamilyOutputContext(state2, 200, obs2.Metrics), regSibling);

        var profile = new Profile
        {
            ModMetricDescriptors = [regBreakdown.Descriptor, regSibling.Descriptor],
            SaveObservations =
            {
                TestProfileBuilders.SaveObs("c1.rws", "c1", 200),
                TestProfileBuilders.SaveObs("c2.rws", "c2", 200),
            },
            MetricObservations = { obs1, obs2 },
        };
        var view = TestProfileBuilders.BuildView(profile);
        var overallBreakdown = MetricLookup.Overall(view, regBreakdown.Descriptor.StableKey)!;
        // Colony 1 was Partial without "gold" on Colony 2: gold exists only in Partial c1, while steel has both -> sum 350.5.
        Assert.Equal(350.5, overallBreakdown.Breakdowns.Single(b => b.Breakdown == "steel").Value.Number);
    }
}
