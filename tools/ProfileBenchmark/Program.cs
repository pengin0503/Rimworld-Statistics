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

// Exercise both a scan-timestamp update and a real statistics update against
// the same profile, with enough ordinary observations to hit each size.
if (args.Length > 0 && args[0] == "--large")
{
    var sizesMiB = args.Length == 1 ? new[] { 5, 10 }
        : args.Skip(1).Select(a => int.Parse(a, CultureInfo.InvariantCulture)).ToArray();
    if (sizesMiB.Any(n => n < 1 || n > 32))
        throw new ArgumentOutOfRangeException(nameof(args), "Large-profile sizes must be 1..32 MiB.");
    Console.WriteLine("target_mib,profile_bytes,saves,serialize_p95_ms,import_only_commit_p95_ms,statistics_commit_p95_ms,import_only_alloc_median_bytes,statistics_alloc_median_bytes,logical_primary_write_bytes");
    foreach (var mib in sizesMiB)
        MeasureLargeProfile(mib);
    return;
}

// Issue #195: profiles shaped like imported real ones (per colony: 8 save files with every SaveFile metric,
// a checkpoint chain whose tip holds every live family metric with breakdowns, compressed ancestors).
if (args.Length > 0 && args[0] == "--representative")
{
    var targets = args.Length == 1 ? new[] { 0.2, 5d, 10d }
        : args.Skip(1).Select(a => double.Parse(a, CultureInfo.InvariantCulture)).ToArray();
    if (targets.Any(n => n is < 0.05 or > 32))
        throw new ArgumentOutOfRangeException(nameof(args), "Representative sizes must be 0.05..32 MiB.");
    Console.WriteLine("target_mib,profile_bytes,colonies,locked_serialize_median_ms,locked_serialize_p95_ms,serialize_alloc_median_bytes,"
        + "statistics_commit_median_ms,statistics_commit_p95_ms,import_only_commit_median_ms,commit_read_bytes,commit_write_bytes,"
        + "blocking_wait_p95_ms,blocking_wait_max_ms,nonblocking_wait_p95_ms,nonblocking_wait_max_ms");
    foreach (var mib in targets)
        RepresentativeBenchmark.Run(mib);
    return;
}

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

static void MeasureLargeProfile(int mib)
{
    var timestamp = new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc);
    var profile = new Profile();
    var targetBytes = mib * 1024 * 1024;
    var snapshotDefinitions = SnapshotCatalog.Definitions.ToArray();
    string serialized;
    do
    {
        for (var i = 0; i < 100; i++)
        {
            var save = profile.SaveObservations.Count;
            var colony = save / 12;
            var campaign = "benchmark-campaign-" + colony;
            var identity = new ColonyIdentity
            {
                ModCampaignId = campaign,
                SeedString = "benchmark-seed-" + colony,
                StartingTile = (100 + colony) + ",0",
                PersistentRandomValue = colony + 1,
                FirstWealthBits = (uint)(170 + colony),
                StartingPawnIds = { "Thing_Human" + (1000 + colony) },
            };
            var observed = new ObservedValues
            {
                TicksGame = 60000 * (save % 12 + 1),
                NumRaidsEnemy = save % 12,
                WealthLast = 5000 + save * 25,
                WealthMax = 5000 + save * 25,
            };
            profile.SaveObservations.Add(new SaveObservation
            {
                FileName = $"benchmark-colony-{colony:D4}-save-{save % 12:D2}.rws",
                FileSize = 20 * 1024 * 1024 + save,
                LastWriteUtc = timestamp.AddMinutes(save),
                GameVersion = "1.7.5005 rev1082",
                ReaderVersion = SaveReader.Version,
                Status = ObservationStatus.Ok,
                Identity = identity,
                Values = observed,
            });
            if (save % 12 != 11)
                continue;
            ProfileMutations.ApplyLiveObservation(profile, campaign, identity, observed, new DisplayInfo(), timestamp);
            var snapshot = Snapshot(identity, save, timestamp, snapshotDefinitions);
            snapshot.SaveTick = observed.TicksGame;
            profile.MetricObservations.Add(snapshot);
        }
        serialized = ProfileJson.Serialize(profile);
    }
    while (Encoding.UTF8.GetByteCount(serialized) < targetBytes);

    var directory = Directory.CreateTempSubdirectory("rws-profile-large-");
    try
    {
        var store = new ProfileStore(directory.FullName);
        _ = store.Load(timestamp);
        store.SaveSerialized(serialized);
        var profileBytes = new FileInfo(store.ProfilePath).Length;
        var session = new ProfileSession(new ProfileStore(directory.FullName), () => timestamp);
        var serialization = new List<double>();
        var scans = new List<double>();
        var statistics = new List<double>();
        var scanAllocations = new List<long>();
        var statisticsAllocations = new List<long>();
        long logicalBytes = 0;
        for (var i = 0; i <= 10; i++)
        {
            // Match WriteLatest's serialization under the session lock while
            // measuring the store separately on exactly the same JSON shape.
            session.Mutate(p => p.ImportState.LastScanUtc = timestamp.AddSeconds(i + 1), SaveMode.None);
            var start = Stopwatch.GetTimestamp();
            var scan = session.Read(ProfileJson.SerializeToUtf8Bytes);
            var serializeMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            MeasureCommit(scan, scans, scanAllocations, i > 0);

            if (i == 0 && File.Exists(store.ProfilePath + ".bak1"))
                throw new InvalidOperationException("Scan-only update rotated a backup.");
            session.Mutate(p => p.SaveObservations[^1].Values.NumRaidsEnemy++, SaveMode.None);
            MeasureCommit(session.Read(ProfileJson.SerializeToUtf8Bytes), statistics, statisticsAllocations, i > 0);
            if (i == 0 && !File.Exists(store.ProfilePath + ".bak1"))
                throw new InvalidOperationException("Statistics update did not rotate a backup.");
            if (i > 0)
                serialization.Add(serializeMs);
        }
        Console.WriteLine(FormattableString.Invariant(
            $"{mib},{profileBytes},{profile.SaveObservations.Count},{Num(Percentile95(serialization))},{Num(Percentile95(scans))},{Num(Percentile95(statistics))},{scanAllocations.Order().ElementAt(5)},{statisticsAllocations.Order().ElementAt(5)},{logicalBytes}"));

        void MeasureCommit(byte[] json, List<double> times, List<long> allocations, bool record)
        {
            // The session's own path: UTF-8 bytes from the serializer straight to the store.
            var start = Stopwatch.GetTimestamp();
            var allocated = GC.GetAllocatedBytesForCurrentThread();
            store.SaveSerialized(json);
            var used = GC.GetAllocatedBytesForCurrentThread() - allocated;
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (!record)
                return;
            times.Add(elapsed);
            allocations.Add(used);
            logicalBytes += json.Length;
        }
    }
    finally
    {
        directory.Delete(recursive: true);
    }
}

static double Percentile95(List<double> samples)
{
    samples.Sort();
    return samples[(int)Math.Ceiling(samples.Count * 0.95) - 1];
}

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

    var bytes = ProfileJson.SerializeToUtf8Bytes(profile).Length;
    var ser = Measure(() => _ = ProfileJson.SerializeToUtf8Bytes(profile));

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
            var json = ProfileJson.SerializeToUtf8Bytes(profile);
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
