using System.Runtime.CompilerServices;

namespace StatisticsSdkSample;

/// <summary>
/// The public methods have no API types in signatures or declaring types. They can be loaded
/// when the optional RimworldStatistics.Api assembly is entirely absent.
/// </summary>
public static class SampleSdkClient
{
    private static bool connected;
    private static double? snapshotCount;
    private static bool throwOnNextSnapshot;

    public static bool TryAttach()
    {
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(x =>
                x.GetName().Name == "RimworldStatistics.Api"))
            return false;
        try { return ApiConsumer.Connect(); }
        catch (Exception)
        {
            // Optional host API cannot become a startup failure for this consumer.
            return false;
        }
    }

    /// <summary>Call from an actual successful MOD event, with a stable non-personal event id.</summary>
    public static bool OnConfirmedEvent(string uniqueId)
    {
        if (!connected)
            return false;
        try { return ApiConsumer.Record(uniqueId); }
        catch (Exception) { return false; }
    }

    /// <summary>Call from a confirmed MOD event attributed to a bounded category/Def bucket key (SDK 1.2+).</summary>
    public static bool OnConfirmedBreakdownEvent(string uniqueId, string breakdownKey, double value = 1)
    {
        if (!connected)
            return false;
        try { return ApiConsumer.RecordBreakdown(uniqueId, breakdownKey, value); }
        catch (Exception) { return false; }
    }

    /// <summary>Set a detached aggregate from a known game state; null means unavailable (not zero).</summary>
    public static void SetCurrentCount(double? value) => snapshotCount = value;

    /// <summary>Development-only fault injection for the next save-time snapshot callback.</summary>
    public static void ArmSnapshotExceptionOnce() => throwOnNextSnapshot = true;

    private static double? CaptureSnapshot()
    {
        if (throwOnNextSnapshot)
        {
            throwOnNextSnapshot = false;
            throw new InvalidOperationException("Intentional SDK sample snapshot failure (verification only)");
        }
        return snapshotCount;
    }

    private static class ApiConsumer
    {
#if REQUIRES_API_MINOR_2
        private const int RequiredMinor = 2;
#elif REQUIRES_API_MINOR_1
        private const int RequiredMinor = 1;
#else
        private const int RequiredMinor = 0;
#endif
        private const string OwnerName = "Statistics SDK Sample (Development)";
        private const string EventId = "mod.pengin0503.rws.sdksample.events.completed";
        private const string BreakdownEventId = "mod.pengin0503.rws.sdksample.events.by_kind";
        private static bool breakdownSupported;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Connect()
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, RequiredMinor, out var host)
                || host is null)
                return false;
            var eventDefinition = new RimworldStatistics.Api.ApiMetricDefinition
            {
                ProviderId = "sdksample.events", OwnerPackageId = "pengin0503.rws.sdksample",
                OwnerDisplayName = OwnerName,
                FamilyId = "events", MetricName = "completed",
                Kind = RimworldStatistics.Api.ApiMetricKind.Event,
            };
            var snapshotDefinition = new RimworldStatistics.Api.ApiMetricDefinition
            {
                ProviderId = "sdksample.state", OwnerPackageId = "pengin0503.rws.sdksample",
                OwnerDisplayName = OwnerName,
                FamilyId = "state", MetricName = "count",
                Kind = RimworldStatistics.Api.ApiMetricKind.SaveSnapshot,
            };
            var a = host.Register(eventDefinition);
            var b = host.Register(snapshotDefinition, CaptureSnapshot);
            var c = true;
            if (host.ApiMinor >= 2)
            {
                var breakdownDefinition = new RimworldStatistics.Api.ApiMetricDefinition
                {
                    ProviderId = "sdksample.events", OwnerPackageId = "pengin0503.rws.sdksample",
                    OwnerDisplayName = OwnerName,
                    FamilyId = "events", MetricName = "by_kind",
                    Kind = RimworldStatistics.Api.ApiMetricKind.Event,
                    HasBreakdown = true,
                };
                c = host.Register(breakdownDefinition) == RimworldStatistics.Api.ApiRegistrationResult.Accepted;
                breakdownSupported = c;
            }
            connected = a == RimworldStatistics.Api.ApiRegistrationResult.Accepted &&
                b == RimworldStatistics.Api.ApiRegistrationResult.Accepted && c;
            return connected;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Record(string id)
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, RequiredMinor, out var host) || host is null)
                return false;
            var ok = host.RecordEvent(EventId, id) == RimworldStatistics.Api.ApiRecordResult.Recorded;
            if (ok && breakdownSupported)
                host.RecordBreakdownEvent(BreakdownEventId, id, "sample_action");
            return ok;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool RecordBreakdown(string id, string breakdownKey, double value)
        {
            return breakdownSupported
                && RimworldStatistics.Api.StatisticsApi.TryGetHost(1, 2, out var host)
                && host?.RecordBreakdownEvent(BreakdownEventId, id, breakdownKey, value)
                    == RimworldStatistics.Api.ApiRecordResult.Recorded;
        }
    }
}
