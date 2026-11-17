using RimworldStatistics.Api;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>Game-side transport only. All data goes through the existing P1 registry and Phase C/B persistence.</summary>
internal sealed class ExternalSdkHost : IStatisticsHost
{
    public static ExternalSdkHost Instance { get; } = new();
    internal DeferredHookRegistry IntegratedHooks { get; } = new();
    // A built-in integration's acquisition units are refused to SDK providers (#314). The list is read at the first
    // registration, during MOD construction: declarations only, nothing is probed or patched.
    private readonly ExternalSdkMetricRegistry registry = new(() => ModProviderAdapters.BuiltInDescriptors);
    private readonly object gate = new();

    public int ApiMajor => StatisticsApi.ApiVersionMajor;
    public int ApiMinor => StatisticsApi.ApiVersionMinor;

    public ApiRegistrationResult Register(ApiMetricDefinition metric, Func<double?>? captureSnapshot = null)
    {
        try
        {
            ApiRegistrationResult result;
            string? builtInUnit;
            lock (gate)
                result = registry.Register(metric, captureSnapshot, out builtInUnit);
            if (builtInUnit is not null)
                Log.ErrorOnce($"[RimworldStatistics] External SDK metric '{metric.ProviderId}/{metric.MetricName}' was refused "
                    + $"({result}): its acquisition unit '{builtInUnit}' belongs to a statistic built into Rimworld Statistics. "
                    + "Register it under another familyId; the provider's other units are unaffected.",
                    ("RWS.sdk.builtin." + builtInUnit).GetHashCode());
            return result;
        }
        catch (Exception e)
        {
            Log.ErrorOnce("[RimworldStatistics] External SDK registration failed: " + e,
                "RWS.sdk.registration".GetHashCode());
            return ApiRegistrationResult.Failed;
        }
    }

    /// <summary>Current-game SDK ID-set saturation; historical profile evidence is separate.</summary>
    internal bool IsEventDedupCapacityExceeded(string metricId)
    {
        RegisteredSdkMetric? metric;
        lock (gate)
            metric = registry.Find(metricId);
        if (metric is null || metric.Api.Kind != ApiMetricKind.Event)
            return false;
        return LiveMetrics.Accumulator?.Family(metric.Descriptor.AcquisitionId)?
            .Peek(metric.SeenAccumulatorKey)?.Overflow == true;
    }

    public ApiRecordResult RecordEvent(string metricId, string uniqueEventId, double value = 1)
    {
        RegisteredSdkMetric? metric;
        lock (gate)
            metric = registry.Find(metricId);
        if (metric is null || metric.Api.Kind != ApiMetricKind.Event)
            return ApiRecordResult.Disabled;
        var unit = metric.Descriptor.AcquisitionId;
        if (!MetricGates.IsRecording(unit))
            return ApiRecordResult.Disabled;
        // LiveMetrics.Family marks a gap if a provider mistakenly invokes this from a worker thread.
        var family = LiveMetrics.Family(unit);
        if (family is null)
            return ApiRecordResult.Disabled;
        try
        {
            var result = ExternalSdkMetricPolicy.Record(family, metric, uniqueEventId, Find.TickManager.TicksGame, value);
            // An invalid confirmed event is a missed observation, not evidence of zero activity.
            // Both invalid input and dedup-cap overflow must weaken coverage, never claim Complete.
            if (result is ApiRecordResult.CapExceeded or ApiRecordResult.InvalidEvent)
                LiveMetrics.Accumulator?.MarkGap(unit, Find.TickManager.TicksGame);
            if (result == ApiRecordResult.CapExceeded)
                Log.WarningOnce("[RimworldStatistics] SDK event ID deduplication limit (" +
                    FamilyOps.KeyCap + ") reached for " + metricId +
                    "; later event attempts are not counted and this metric has partial coverage.",
                    ("RWS.sdk.cap." + metricId).GetHashCode());
            return result;
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(unit, e);
            return ApiRecordResult.Failed;
        }
    }

    /// <summary>Freeze registrations once after all MOD constructors, before the acquisition adapter scan.</summary>
    internal IReadOnlyList<ModMetricAdapterRegistration> BuildAdapters()
    {
        RegisteredSdkMetric[] registered;
        lock (gate)
            registered = registry.Seal().ToArray();
        var result = new List<ModMetricAdapterRegistration>();
        var eventFamilies = registered.Where(x => x.Api.Kind == ApiMetricKind.Event)
            .GroupBy(x => x.Descriptor.AcquisitionId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var familyBuilders = eventFamilies.ToDictionary(kv => kv.Key, kv => new LiveFamily
        {
            Name = kv.Key,
            Revision = kv.Value[0].Descriptor.Revision,
            RequiredDlc = kv.Value[0].Descriptor.RequiredDlc,
            Build = output =>
            {
                foreach (var metric in kv.Value)
                {
                    // A malformed external unit cannot block unrelated metrics or the game save.
                    try { ExternalSdkMetricPolicy.Build(output, metric); }
                    catch (Exception) { /* only that metric is omitted */ }
                }
            },
        }, StringComparer.Ordinal);

        foreach (var metric in registered)
        {
            var d = metric.Descriptor;
            if (metric.Api.Kind == ApiMetricKind.Event)
                result.Add(new ModMetricAdapterRegistration(d,
                    EventFamily: familyBuilders[d.AcquisitionId],
                    InstallEventHook: _ =>
                    {
                        if (!IntegratedHooks.Run(d.AcquisitionId))
                            throw new InvalidOperationException("Integrated event hook could not be installed for " + d.AcquisitionId);
                    }, ProviderVersion: metric.Api.ProviderVersion));
            else
                result.Add(new ModMetricAdapterRegistration(d,
                    CaptureSnapshot: () =>
                    {
                        if (!UnityData.IsInMainThread)
                            throw new InvalidOperationException("SDK snapshots must run on the game thread");
                        return ExternalSdkMetricPolicy.Snapshot(metric);
                    }, ProviderVersion: metric.Api.ProviderVersion));
        }
        return result;
    }
}
