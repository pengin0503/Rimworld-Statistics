// Deterministic synthetic cross-colony leaderboard benchmark.
// No original saves, DLC data, MOD inventories or game process are used.
using System.Diagnostics;
using System.Globalization;
using RimworldStatistics.Core;

const int colonyCount = 35;
const int itemKinds = 200;
const int iterations = 15;
var definition = MetricRegistry.Find(TradeFamily.SalesUnits)!;
var rows = new Dictionary<string, IReadOnlyList<NumericRow>>(StringComparer.Ordinal);
for (var i = 0; i < colonyCount; i++)
{
    var children = Enumerable.Range(0, itemKinds).Select(j => new NumericRow
    {
        Definition = definition, Breakdown = $"SyntheticDef{j:D4}",
        Value = new NumericValue { Number = j + 1, Coverage = MetricCoverage.Complete },
        Targets = 1, Valid = 1,
    }).ToArray();
    rows.Add($"SyntheticColony{i:D3}", new[]
    {
        new NumericRow
        {
            Definition = definition,
            Value = new NumericValue
            {
                Number = (double)itemKinds * (itemKinds + 1) / 2,
                Coverage = MetricCoverage.Complete,
            },
            Targets = 1, Valid = 1, Breakdowns = children,
        },
    });
}

NumericRow Build()
{
    var ranked = CrossColonyAggregator.Build(MetricRegistry.Catalog, rows)
        .Single(r => r.Definition.Key == CrossColonyCatalog.TopPrefix + TradeFamily.SalesUnits);
    if (ranked.Value.Number != colonyCount * itemKinds
        || ranked.Targets != colonyCount || ranked.Valid != colonyCount
        || ranked.Breakdowns.Count != itemKinds
        || ranked.Breakdowns[0].Breakdowns.Count != colonyCount)
        throw new InvalidOperationException("Synthetic leaderboard contract failed");
    return ranked;
}

Build(); // JIT and data initialization excluded from measurement.
var times = new double[iterations];
var alloc = new long[iterations];
for (var i = 0; i < iterations; i++)
{
    var before = GC.GetAllocatedBytesForCurrentThread();
    var start = Stopwatch.GetTimestamp();
    Build();
    times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    alloc[i] = GC.GetAllocatedBytesForCurrentThread() - before;
}
Array.Sort(times);
Array.Sort(alloc);
Console.WriteLine("colonies,item_kinds,runs,p50_ms,p95_ms,median_alloc_bytes");
Console.WriteLine(string.Join(",",
    colonyCount.ToString(CultureInfo.InvariantCulture),
    itemKinds.ToString(CultureInfo.InvariantCulture),
    iterations.ToString(CultureInfo.InvariantCulture),
    times[iterations / 2].ToString("F3", CultureInfo.InvariantCulture),
    times[(int)Math.Ceiling(iterations * 0.95) - 1].ToString("F3", CultureInfo.InvariantCulture),
    alloc[iterations / 2].ToString(CultureInfo.InvariantCulture)));
