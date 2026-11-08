using System.Reflection;
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

    public static bool TryAttach()
    {
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(x =>
                x.GetName().Name == "RimworldStatistics.Api"))
            return false;
        try { return ApiConsumer.Connect(); }
        catch (Exception e) when (e is TypeLoadException or FileNotFoundException or FileLoadException
            or MissingMethodException or TargetInvocationException or BadImageFormatException)
        {
            return false;
        }
    }

    /// <summary>Call from an actual successful MOD event, with a stable non-personal event id.</summary>
    public static bool OnConfirmedEvent(string uniqueId) =>
        connected && ApiConsumer.Record(uniqueId);

    /// <summary>Set a detached aggregate from a known game state; null means unavailable (not zero).</summary>
    public static void SetCurrentCount(double? value) => snapshotCount = value;

    private static class ApiConsumer
    {
#if REQUIRES_API_MINOR_1
        private const int RequiredMinor = 1;
#else
        private const int RequiredMinor = 0;
#endif
        private const string EventId = "mod.pengin0503.rws.sdksample.events.completed";

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Connect()
        {
            if (!RimworldStatistics.Api.StatisticsApi.TryGetHost(1, RequiredMinor, out var host)
                || host is null)
                return false;
            var eventDefinition = new RimworldStatistics.Api.ApiMetricDefinition
            {
                ProviderId = "sdksample.events", OwnerPackageId = "pengin0503.rws.sdksample",
                FamilyId = "events", MetricName = "completed",
                Kind = RimworldStatistics.Api.ApiMetricKind.Event,
            };
            var snapshotDefinition = new RimworldStatistics.Api.ApiMetricDefinition
            {
                ProviderId = "sdksample.state", OwnerPackageId = "pengin0503.rws.sdksample",
                FamilyId = "state", MetricName = "count",
                Kind = RimworldStatistics.Api.ApiMetricKind.SaveSnapshot,
            };
            var a = host.Register(eventDefinition);
            var b = host.Register(snapshotDefinition, () => snapshotCount);
            connected = a == RimworldStatistics.Api.ApiRegistrationResult.Accepted &&
                b == RimworldStatistics.Api.ApiRegistrationResult.Accepted;
            return connected;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool Record(string id)
        {
            return RimworldStatistics.Api.StatisticsApi.TryGetHost(1, RequiredMinor, out var host)
                && host?.RecordEvent(EventId, id) == RimworldStatistics.Api.ApiRecordResult.Recorded;
        }
    }
}
