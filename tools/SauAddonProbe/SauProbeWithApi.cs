using RimworldStatistics.Api;
using RimworldStatistics.SmartApparelAddon;

internal static class SauProbeWithApi
{
    public static void Run()
    {
        if (SauHostClient.TryRegister())
            throw new Exception("SAU addon registered before a host was installed");

        var host = new FakeHost();
        if (!StatisticsApi.InstallHost(host))
            throw new Exception("Failed to install synthetic host");
        if (!SauHostClient.TryRegister())
            throw new Exception("SAU addon registration unexpectedly failed");

        var metric = host.Definition ?? throw new Exception("No addon metric definition was registered");
        if (metric.MetricId != "mod.pengin0503.smartapparelupgrade.orders.issued"
            || metric.FamilyAcquisitionId != "mod.pengin0503.smartapparelupgrade.orders"
            || metric.Kind != ApiMetricKind.Event || metric.EventMode != ApiEventMode.Count
            || metric.Category != "Work" || metric.Overall != "Sum"
            || metric.LabelKey != "RWS_SAU_OrdersIssued"
            || metric.OwnerPackageId != "pengin0503.smartapparelupgrade")
            throw new Exception("Addon registered the wrong metric or event semantics");

        var first = SauOrderEventId.Create("Human_10", "Apparel_20", 12000)!;
        var second = SauOrderEventId.Create("Human_10", "Apparel_21", 12000)!;
        SauHostClient.OnIssuedOrder(first);
        SauHostClient.OnIssuedOrder(first);
        SauHostClient.OnIssuedOrder(second);
        if (host.AcceptedCount != 2 || host.DuplicateCount != 1)
            throw new Exception("Host deduplication/recording does not match issued-order contract");
        Console.WriteLine("SAU addon optional-host negotiation, declaration and deduplication: PASS");
    }

    private sealed class FakeHost : IStatisticsHost
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        public ApiMetricDefinition? Definition { get; private set; }
        public int AcceptedCount { get; private set; }
        public int DuplicateCount { get; private set; }
        public int ApiMajor => 1;
        public int ApiMinor => 1;

        public ApiRegistrationResult Register(ApiMetricDefinition metric, Func<double?>? captureSnapshot = null)
        {
            if (captureSnapshot is not null || Definition is not null)
                return ApiRegistrationResult.InvalidDefinition;
            Definition = metric;
            return ApiRegistrationResult.Accepted;
        }

        public ApiRecordResult RecordEvent(string metricId, string uniqueEventId, double value = 1)
        {
            if (metricId != Definition?.MetricId || value != 1)
                return ApiRecordResult.InvalidEvent;
            if (!seen.Add(uniqueEventId))
            {
                DuplicateCount++;
                return ApiRecordResult.Duplicate;
            }
            AcceptedCount++;
            return ApiRecordResult.Recorded;
        }
    }
}
