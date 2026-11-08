using RimworldStatistics.Api;
using StatisticsSdkSample;

internal static class ProbeWithApi
{
    // This body is JIT-compiled only after the no-API case has branched away.
    internal static void Run()
    {
        if (SampleSdkClient.TryAttach())
                throw new Exception("Sample incorrectly attached with API DLL absent");
            Console.WriteLine("SDK absence: PASS");
            return;
        }
        
        if (SampleSdkClient.TryAttach())
            throw new Exception("Sample found a host before installation");
        var fake = new FakeHost();
        if (!StatisticsApi.InstallHost(fake))
            throw new Exception("Could not install fake host");
        if (!StatisticsApi.TryGetHost(1, 0, out _))
            throw new Exception("Compatible 1.0 consumer rejected");
        if (!StatisticsApi.TryGetHost(1, 1, out _))
            throw new Exception("Compatible 1.1 consumer rejected");
        if (StatisticsApi.TryGetHost(2, 0, out _) || StatisticsApi.TryGetHost(1, 2, out _))
            throw new Exception("Incompatible major/minor was accepted");
        if (!SampleSdkClient.TryAttach())
            throw new Exception("Independent sample did not register with compatible host");
        SampleSdkClient.SetCurrentCount(3);
        if (!SampleSdkClient.OnConfirmedEvent("public-test-event-1"))
            throw new Exception("Sample event was not recorded");
        if (SampleSdkClient.OnConfirmedEvent("public-test-event-1"))
            throw new Exception("Sample duplicate event was recorded");
        if (fake.Snapshot?.Invoke() != 3)
            throw new Exception("Sample snapshot was not captured");
        Console.WriteLine("SDK sample: negotiation, registration, event, dedup and snapshot PASS");
        
    }

    sealed class FakeHost : IStatisticsHost
    {
        private readonly HashSet<string> keys = new(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        public Func<double?>? Snapshot { get; private set; }
        public int ApiMajor => 1;
        public int ApiMinor => 1;
    
        public ApiRegistrationResult Register(ApiMetricDefinition definition, Func<double?>? captureSnapshot = null)
        {
            if (!keys.Add(definition.MetricId))
                return ApiRegistrationResult.DuplicateMetric;
            if (definition.Kind == ApiMetricKind.SaveSnapshot)
                Snapshot = captureSnapshot;
            return ApiRegistrationResult.Accepted;
        }
    
        public ApiRecordResult RecordEvent(string metricId, string uniqueEventId, double value = 1)
            => keys.Contains(metricId) && seen.Add(metricId + "/" + uniqueEventId)
                ? ApiRecordResult.Recorded : ApiRecordResult.Duplicate;
    }
    
}
