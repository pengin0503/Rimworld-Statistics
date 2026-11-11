using RimworldStatistics.Api;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>Game-side transport only. All data goes through the existing P1 registry and Phase C/B persistence.</summary>
internal sealed class ExternalSdkHost : IStatisticsHost
{
    public static ExternalSdkHost Instance { get; } = new();
    internal DeferredHookRegistry IntegratedHooks { get; } = new();
    private readonly ExternalSdkMetricRegistry registry = new();
    private readonly object gate = new();

    public int ApiMajor => StatisticsApi.ApiVersionMajor;
    public int ApiMinor => StatisticsApi.ApiVersionMinor;

    public ApiRegistrationResult Register(ApiMetricDefinition metric, Func<double?>? captureSnapshot = null)
    {
        try
        {
            lock (gate)
                return registry.Register(metric, captureSnapshot);
        }
        catch (Exception e)
        {
            Log.ErrorOnce("[RimworldStatistics] External SDK registration failed: " + e,
                "RWS.sdk.registration".GetHashCode());
            return ApiRegistrationResult.Failed;
        }
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
