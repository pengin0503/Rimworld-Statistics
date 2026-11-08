// Standalone, game-independent microbenchmark. No RimWorld installation or network is needed.
// Run from the repository root with:
// dotnet run -c Release --project tools/ProfileBenchmark/ProfileBenchmark.csproj
//
// Results are measurements of this runner, not an in-game TPS claim. No threshold is
// enforced, because machine load, filesystem and runtime variation are uncontrolled.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RimworldStatistics.Core;

var sizes = args.Length == 0
    ? new[] { 100, 500, 1500, 3000 }
    : args.Select(a => int.Parse(a, CultureInfo.InvariantCulture)).ToArray();
if (sizes.Any(n => n < 1 || n > 20000))
    throw new ArgumentOutOfRangeException(nameof(args), "Save counts must be in the range 1..20000.");

const int campaigns = 12;
var epoch = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
var defs = SnapshotCatalog.Definitions.ToArray();
var ids = Enumerable.Range(0, campaigns).Select(i => new ColonyIdentity
{
    SeedString = "benchmark-seed-" + i,
    PersistentRandomValue = i + 1,
    StartingTile = (100 + i) + ",0",
    GameStartAbsTick = 0,
    FirstWealthBits = (uint)(170 + i),
    ModCampaignId = "perf-campaign-" + i,
    StartingPawnIds = new List<string> { "Thing_Human" + (1000 + i) },
}).ToArray();

static MetricObservation Snapshot(ColonyIdentity id, int save, DateTime now,
    IReadOnlyList<MetricDefinition> definitions)
{
    var observation = new MetricObservation
    {
        Source = MetricObservationSource.SaveSnapshot,
        Key = "snapshot-" + save,
        Identity = id.Clone(),
        FormatVersion = 1,
        SaveTick = 60000 + save * 10,
        ObservedUtc = now,
        ActiveDlcIds = new List<string> { AcquisitionUnits.Biotech, AcquisitionUnits.Royalty },
    };
    foreach (var d in definitions)
    {
        // Keep real coverage and revision shapes. Vary maxima, minima and latest
        // independently so compaction has to preserve different witnesses.
        var n = d.Selection == ColonySelection.ObservationMin
            ? (save % 47 - 30)
            : d.Selection == ColonySelection.ObservationMax ? save % 43 : save % 17;
        observation.Metrics[d.StoredKey] = MetricEntry.Of(d.Revision, new MetricValue
        {
            Coverage = MetricCoverage.SnapshotOnly,
            IntegerValue = n,
        });
    }
    return observation;
}

static (double Milliseconds, long AllocBytes) Measure(Action action, int repetitions = 7)
{
    action();
    action();
    var elapsed = new List<double>(repetitions);
    var allocs = new List<long>(repetitions);
    for (var i = 0; i < repetitions; i++)
    {
        var beforeAlloc = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        action();
        elapsed.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        allocs.Add(GC.GetAllocatedBytesForCurrentThread() - beforeAlloc);
    }
    elapsed.Sort();
    allocs.Sort();
    return (elapsed[elapsed.Count / 2], allocs[allocs.Count / 2]);
}

static string Num(double n) => n.ToString("F4", CultureInfo.InvariantCulture);

Console.WriteLine("saves,observations_stored,profile_bytes,append_tail_ms,serialize_ms,build_view_ms,build_view_alloc_bytes,disk_commit_ms");
foreach (var size in sizes)
{
    var profile = new Profile();
    foreach (var id in ids)
        ProfileMutations.ApplyLiveObservation(profile, id.ModCampaignId!, id,
            new ObservedValues { TicksGame = 60000 }, new DisplayInfo(), epoch);
    var appendTail = new List<double>();
    for (var save = 0; save < size; save++)
    {
        var observation = Snapshot(ids[save % campaigns], save, epoch.AddMinutes(save), defs);
        var start = Stopwatch.GetTimestamp();
        ProfileMutations.UpsertMetricObservation(profile, observation);
        if (save >= size - Math.Min(100, size))
            appendTail.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    appendTail.Sort();
    var tailMs = appendTail[appendTail.Count / 2];

    var serialized = ProfileJson.Serialize(profile);
    var bytes = Encoding.UTF8.GetByteCount(serialized);
    var ser = Measure(() => _ = ProfileJson.Serialize(profile));

    var view = Measure(() =>
    {
        var colonies = CampaignMatcher.BuildColonies(profile);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        _ = MetricCalculator.BuildView(profile, colonies, stats, defs);
    });

    var path = Path.Combine(Path.GetTempPath(), "rws-profile-benchmark-" + Guid.NewGuid().ToString("N"));
    var commits = new List<double>();
    try
    {
        var store = new ProfileStore(path);
        _ = store.Load(epoch);
        for (var i = 0; i < 5; i++)
        {
            // Only importState changes: this forces a real atomic file replacement
            // without rotating a full backup, which would distort size scaling.
            profile.ImportState.LastScanUtc = epoch.AddSeconds(i + 1);
            var json = ProfileJson.Serialize(profile);
            var start = Stopwatch.GetTimestamp();
            store.SaveSerialized(json);
            commits.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }
    finally
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    commits.Sort();

    Console.WriteLine(string.Join(",",
        size.ToString(CultureInfo.InvariantCulture),
        profile.MetricObservations.Count.ToString(CultureInfo.InvariantCulture),
        bytes.ToString(CultureInfo.InvariantCulture),
        Num(tailMs),
        Num(ser.Milliseconds),
        Num(view.Milliseconds),
        view.AllocBytes.ToString(CultureInfo.InvariantCulture),
        Num(commits[commits.Count / 2])));
}
