// Same-process A/B microbenchmark of the previous linear per-key scan versus
// the indexed lookup. The full MetricCalculator behavior is covered by tests.
// Synthetic data only; this is not game TPS or a real-save import benchmark.
using System.Diagnostics;
using System.Globalization;
using RimworldStatistics.Core;

const int colonyCount = 35;
const int itemKinds = 200;
const int runs = 21;
var definition = MetricRegistry.Find(TradeFamily.SalesUnits)!;
var colonies = Enumerable.Range(0, colonyCount).Select(colony =>
{
    var children = Enumerable.Range(0, itemKinds).Select(j => new NumericRow
    {
        Definition = definition, Breakdown = "SyntheticDef" + j.ToString("D4", CultureInfo.InvariantCulture),
        Value = new NumericValue { Number = j + 1, Coverage = MetricCoverage.Complete },
    }).ToArray();
    return new NumericRow
    {
        Definition = definition,
        Value = new NumericValue { Number = (double)itemKinds * (itemKinds + 1) / 2,
            Coverage = MetricCoverage.Complete },
        Breakdowns = children,
    };
}).ToArray();
var keys = colonies.SelectMany(c => c.Breakdowns.Select(b => b.Breakdown))
    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

double Linear()
{
    double checksum = 0;
    foreach (var key in keys)
        foreach (var row in colonies)
            checksum += row.Breakdowns.FirstOrDefault(r => r.Breakdown == key)?.Value.Number ?? 0;
    return checksum;
}
double Indexed()
{
    var maps = colonies.Select(row => row.Breakdowns.ToDictionary(x => x.Breakdown, x => x.Value.Number!.Value,
        StringComparer.Ordinal)).ToArray();
    double checksum = 0;
    foreach (var key in keys)
        foreach (var map in maps)
            checksum += map.GetValueOrDefault(key);
    return checksum;
}

var expected = Linear();
if (expected != Indexed())
    throw new InvalidOperationException("Indexed and linear lookup values differ");
var linearMs = new double[runs];
var indexMs = new double[runs];
var linearAlloc = new long[runs];
var indexAlloc = new long[runs];
void Measure(Func<double> operation, double[] times, long[] allocations, int i)
{
    var alloc = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    if (operation() != expected)
        throw new InvalidOperationException("Inconsistent benchmark checksum");
    times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    allocations[i] = GC.GetAllocatedBytesForCurrentThread() - alloc;
}
for (var i = 0; i < runs; i++)
{
    if (i % 2 == 0)
    {
        Measure(Linear, linearMs, linearAlloc, i);
        Measure(Indexed, indexMs, indexAlloc, i);
    }
    else
    {
        Measure(Indexed, indexMs, indexAlloc, i);
        Measure(Linear, linearMs, linearAlloc, i);
    }
}
Array.Sort(linearMs);
Array.Sort(indexMs);
Array.Sort(linearAlloc);
Array.Sort(indexAlloc);
Console.WriteLine("colonies,item_kinds,runs,linear_p50_ms,index_p50_ms,linear_alloc_p50_bytes,index_alloc_p50_bytes");
Console.WriteLine(string.Join(",",
    colonyCount, itemKinds, runs,
    linearMs[runs / 2].ToString("F3", CultureInfo.InvariantCulture),
    indexMs[runs / 2].ToString("F3", CultureInfo.InvariantCulture),
    linearAlloc[runs / 2], indexAlloc[runs / 2]));
