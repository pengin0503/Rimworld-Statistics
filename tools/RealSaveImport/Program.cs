// Explicitly private, read-only production .NET SaveReader acceptance over real .rws files.
// Only aggregate, anonymous counts and timings reach stdout; originals never leave the runner.
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using RimworldStatistics.Core;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.Error.WriteLine("Expected one private input directory; no files were read.");
    return 2;
}

static byte[] Digest(string path)
{
    using var file = new FileStream(path, FileMode.Open, FileAccess.Read,
        FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan);
    return SHA256.HashData(file);
}

static double Percentile(IReadOnlyList<double> sorted, double fraction)
    => sorted.Count == 0 ? 0 : sorted[(int)Math.Ceiling(sorted.Count * fraction) - 1];

static void CountPositivePartial(MetricObservation? observation, string key, ref int observed, ref int partial)
{
    if (observation?.Metrics.TryGetValue(key, out var entry) == true
        && entry.Values.TryGetValue("", out var value) && value.IntegerValue is > 0)
    {
        observed++;
        if (value.Coverage == MetricCoverage.Partial)
            partial++;
    }
}

var originals = Directory.EnumerateFiles(args[0], "*", SearchOption.TopDirectoryOnly)
    .Where(f => f.EndsWith(".rws", StringComparison.OrdinalIgnoreCase)
        || f.EndsWith(".rws.old", StringComparison.OrdinalIgnoreCase))
    .Order(StringComparer.Ordinal).ToArray();
if (originals.Length == 0 || originals.Length > 1000)
{
    Console.Error.WriteLine("Private input count outside bounds.");
    return 2;
}

var successes = 0;
var unidentifiable = 0;
var errors = 0;
var checkpoints = 0;
var researchObserved = 0;
var researchPartial = 0;
var turretObserved = 0;
var turretPartial = 0;
var silverObserved = 0;
var silverPartial = 0;
var medicineObserved = 0;
var medicinePartial = 0;
var freeObserved = 0;
var freePartial = 0;
var prisonerObserved = 0;
var prisonerPartial = 0;
var slaveObserved = 0;
var slavePartial = 0;
var unchanged = 0;
var times = new List<double>(originals.Length);
var allocations = new List<long>(originals.Length);
var byVersion = new SortedDictionary<string, int>(StringComparer.Ordinal);
var reader = new SaveReader();
foreach (var file in originals)
{
    try
    {
        var info = new FileInfo(file);
        var size = info.Length;
        var date = info.LastWriteTimeUtc;
        var before = Digest(file);
        var startAllocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var result = reader.ReadDetailed(file);
        times.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        allocations.Add(GC.GetAllocatedBytesForCurrentThread() - startAllocated);
        var version = result.Observation.GameVersion ?? "unknown";
        if (!Regex.IsMatch(version, @"^\d+\.\d+\.\d+ rev\d+$", RegexOptions.CultureInvariant)
            || version.Length > 50)
            version = "unknown";
        byVersion[version] = byVersion.GetValueOrDefault(version) + 1;
        if (result.Checkpoint is not null)
            checkpoints++;
        if (result.Metrics?.Metrics.TryGetValue(ResearchStateSnapshot.Finished, out var recovered) == true
            && recovered.Values.TryGetValue("", out var value) && value.IntegerValue is >= 0)
        {
            researchObserved++;
            if (value.Coverage == MetricCoverage.Partial)
                researchPartial++;
        }
        CountPositivePartial(result.Metrics, FacilitiesSnapshot.TurretsPeak, ref turretObserved, ref turretPartial);
        CountPositivePartial(result.Metrics, StockSnapshot.SilverLatest, ref silverObserved, ref silverPartial);
        CountPositivePartial(result.Metrics, StockSnapshot.MedicinePeak, ref medicineObserved, ref medicinePartial);
        CountPositivePartial(result.Metrics, RosterSnapshot.Free, ref freeObserved, ref freePartial);
        CountPositivePartial(result.Metrics, RosterSnapshot.Prisoners, ref prisonerObserved, ref prisonerPartial);
        CountPositivePartial(result.Metrics, RosterSnapshot.Slaves, ref slaveObserved, ref slavePartial);
        switch (result.Observation.Status)
        {
            case ObservationStatus.Ok: successes++; break;
            case ObservationStatus.Unidentifiable: unidentifiable++; break;
            default: errors++; break;
        }
        info.Refresh();
        if (size != info.Length || date != info.LastWriteTimeUtc
            || !before.AsSpan().SequenceEqual(Digest(file)))
            errors++;
        else
            unchanged++;
    }
    catch (Exception)
    {
        // No exception text: filesystem errors often include private file names.
        errors++;
    }
}
times.Sort();
allocations.Sort();
Console.WriteLine("schema,originals,parsed_ok,unidentifiable,errors,unchanged,checkpoints,p50_ms,p95_ms,p50_alloc_bytes,research_observed,research_partial,turret_observed,turret_partial,silver_observed,silver_partial,medicine_observed,medicine_partial,free_observed,free_partial,prisoner_observed,prisoner_partial,slave_observed,slave_partial");
Console.WriteLine(string.Join(",", 3, originals.Length, successes, unidentifiable,
    errors, unchanged, checkpoints,
    Percentile(times, 0.5).ToString("F3", CultureInfo.InvariantCulture),
    Percentile(times, 0.95).ToString("F3", CultureInfo.InvariantCulture),
    allocations.Count > 0 ? allocations[allocations.Count / 2] : 0L,
    researchObserved, researchPartial,
    turretObserved, turretPartial, silverObserved, silverPartial,
    medicineObserved, medicinePartial,
    freeObserved, freePartial, prisonerObserved, prisonerPartial,
    slaveObserved, slavePartial));
Console.WriteLine("build,anonymous_count");
foreach (var (version, count) in byVersion)
    Console.WriteLine(version + "," + count.ToString(CultureInfo.InvariantCulture));
return errors == 0 && unchanged == originals.Length ? 0 : 1;
