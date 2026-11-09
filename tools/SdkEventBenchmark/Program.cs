// No-game microbenchmark. It compares the historical public-core helper against the
// registered fast path using identical UTF-8 event IDs and bounded checkpoint state.
// Results are measurements of CI's .NET runtime, not RimWorld TPS or save latency.
using System.Diagnostics;
using System.Globalization;
using RimworldStatistics.Api;
using RimworldStatistics.Core;

var api = new ApiMetricDefinition
{
    ProviderId = "bench.events", OwnerPackageId = "bench.consumer",
    FamilyId = "events", MetricName = "CoMpLeTeD", EventMode = ApiEventMode.Count,
};
var registry = new ExternalSdkMetricRegistry();
if (registry.Register(api, null) != ApiRegistrationResult.Accepted)
    throw new InvalidOperationException("SDK benchmark could not register event metric");
var registered = registry.Seal().Single();

static (double Nanoseconds, double AllocatedBytes) Measure(
    int iterations, bool unique, Func<FamilyState, string, int, ApiRecordResult> record, int repetitions)
{
    var timings = new double[repetitions];
    var allocations = new double[repetitions];
    for (var j = 0; j < repetitions; j++)
    {
        var state = new FamilyState { Revision = 1 };
        if (!unique && record(state, "repeat-key", 0) != ApiRecordResult.Recorded)
            throw new InvalidOperationException("Benchmark prefill was not recorded");
        // Warm the JIT and the SHA implementation before collecting samples.
        if (j == 0)
        {
            var warm = new FamilyState { Revision = 1 };
            for (var i = 0; i < 128; i++)
                _ = record(warm, "warm-event", i);
        }
        var allocatedAtStart = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            var id = unique ? "event-" + i.ToString(CultureInfo.InvariantCulture) : "repeat-key";
            var result = record(state, id, i);
            var expected = unique ? ApiRecordResult.Recorded : ApiRecordResult.Duplicate;
            if (result != expected)
                throw new InvalidOperationException($"Benchmark event mode mismatch: {result}, expected {expected}");
        }
        timings[j] = Stopwatch.GetElapsedTime(started).TotalNanoseconds / iterations;
        allocations[j] = (GC.GetAllocatedBytesForCurrentThread() - allocatedAtStart) / (double)iterations;
    }
    Array.Sort(timings);
    Array.Sort(allocations);
    return (timings[repetitions / 2], allocations[repetitions / 2]);
}

var legacy = new Func<FamilyState, string, int, ApiRecordResult>((state, id, tick) =>
    ExternalSdkMetricPolicy.Record(state, api, id, tick, 1));
var fast = new Func<FamilyState, string, int, ApiRecordResult>((state, id, tick) =>
    ExternalSdkMetricPolicy.Record(state, registered, id, tick, 1));

Console.WriteLine("scenario,method,median_ns_per_event,median_alloc_bytes_per_event");
foreach (var (name, iterations, unique) in new[]
{
    ("duplicate", 20000, false),
    ("unique", 2000, true),
})
{
    var original = Measure(iterations, unique, legacy, repetitions: 5);
    var optimized = Measure(iterations, unique, fast, repetitions: 5);
    Console.WriteLine(string.Join(",", name, "legacy",
        original.Nanoseconds.ToString("F1", CultureInfo.InvariantCulture),
        original.AllocatedBytes.ToString("F1", CultureInfo.InvariantCulture)));
    Console.WriteLine(string.Join(",", name, "registered",
        optimized.Nanoseconds.ToString("F1", CultureInfo.InvariantCulture),
        optimized.AllocatedBytes.ToString("F1", CultureInfo.InvariantCulture)));
    // Allocation counts on the same thread and runtime are deterministic enough for
    // a deliberately large margin, unlike elapsed CPU time on shared CI runners.
    if (original.AllocatedBytes - optimized.AllocatedBytes < 64)
        throw new Exception($"SDK fast path failed to reduce {name} per-event allocation by 64 bytes");
}
