using RimworldStatistics.Api;
using StatisticsSdkSample;

internal static class ProbeWithApi
{
    /// <summary>Verify that a synthetic older minor host refuses a 1.1 consumer without loading it.</summary>
    internal static void RunOlderHost()
    {
        if (!StatisticsApi.InstallHost(new FakeHost(0)))
            throw new Exception("Old 1.0 host could not install");
        if (!StatisticsApi.TryGetHost(1, 0, out _))
            throw new Exception("1.0 host rejected a compatible consumer");
        if (StatisticsApi.TryGetHost(1, 1, out _))
            throw new Exception("1.0 host incorrectly accepted a 1.1 consumer");
        if (SampleSdkClient.TryAttach())
        {
#if REQUIRES_API_MINOR_1
            throw new Exception("1.1 sample should not register on 1.0 host");
#endif
        }
        Console.WriteLine("SDK old-host version floor: PASS");
    }

    // This body is JIT-compiled only after the no-API case has branched away.
    internal static void Run()
    {
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
        SampleSdkClient.ArmSnapshotExceptionOnce();
        var snapshotFaultObserved = false;
        try { _ = fake.Snapshot?.Invoke(); }
        catch (InvalidOperationException) { snapshotFaultObserved = true; }
        if (!snapshotFaultObserved)
            throw new Exception("Sample snapshot fault injection did not trigger");
        if (fake.Snapshot?.Invoke() != 3)
            throw new Exception("Snapshot fault injection did not reset after one call");
        // This is a real copy of the sample's save/load event-id algorithm, not just host negotiation.
        var ids = new List<string>();
        var ledger = new SampleEventLedger("test-save-guid", 0);
        if (!ledger.Emit(id => { ids.Add(id); return true; }) || ledger.Count != 1)
            throw new Exception("Sample event ledger did not record first confirmed event");
        var restored = new SampleEventLedger("test-save-guid", ledger.Count);
        if (!restored.Emit(id => { ids.Add(id); return true; }) || restored.Count != 2)
            throw new Exception("Sample event ledger lost event position on restore");
        if (ids.Count != 2 || ids[0] == ids[1])
            throw new Exception("Sample event IDs collided after reload");
        var offline = new SampleEventLedger("test-save-guid", restored.Count);
        if (offline.Emit(_ => false) || offline.Count != 3)
            throw new Exception("Offline sample events must advance their local ordinal, not claim SDK recording");

        Console.WriteLine("SDK sample: negotiation, registration, event, dedup and snapshot PASS");
        
    }

    sealed class FakeHost : IStatisticsHost
    {
        private readonly HashSet<string> keys = new(StringComparer.Ordinal);
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);
        public Func<double?>? Snapshot { get; private set; }
        public int ApiMajor => 1;
        private readonly int minor;
        public FakeHost(int minor = 1) => this.minor = minor;
        public int ApiMinor => minor;
    
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
