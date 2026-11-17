// Development-only analysis of one real-game event probe run (README.md). It reads the run's saves with the production
// SaveReader, prints each save's checkpoint link and what changed since the previous save, then opens a temporary copy of the
// run's profile with the production ProfileSession and prints the colony view of every metric that changed. The run's files
// are only read.
using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using RimworldStatistics.Core;

if (args.Length != 2 || !Directory.Exists(args[0]) || !Directory.Exists(args[1]))
{
    Console.Error.WriteLine("usage: RealGameEventAnalyzer <run userdata/Saves> <run userdata/RimworldStatistics>");
    return 2;
}

var inv = CultureInfo.InvariantCulture;

string Format(MetricValue v)
{
    var number = v.IntegerValue?.ToString(inv) ?? v.RealValue?.ToString("G6", inv)
        ?? (v.Denominator is { } d ? $"{v.Numerator?.ToString("G6", inv)}/{d.ToString("G6", inv)}" : "null");
    return $"{number}({v.Coverage})";
}

static string? RawCheckpoint(string path)
{
    using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true });
    while (reader.Read())
        if (reader.NodeType == XmlNodeType.Element && reader.Name == "numericStatistics")
            return reader.ReadElementContentAsString();
    return null;
}

var saveReader = new SaveReader();
var changedKeys = new SortedSet<string>(StringComparer.Ordinal);
Dictionary<string, string>? previous = null;
string? previousName = null;
foreach (var path in Directory.EnumerateFiles(args[0], "*.rws").Order(StringComparer.Ordinal))
{
    var name = Path.GetFileName(path);
    string hash;
    using (var stream = File.OpenRead(path))
        hash = Convert.ToHexStringLower(SHA256.HashData(stream))[..12];
    Console.WriteLine($"== {name} sha256={hash} bytes={new FileInfo(path).Length}");
    var parse = CheckpointJson.Parse(RawCheckpoint(path));
    Console.WriteLine($"   checkpoint parse={parse.Status} id={parse.Payload?.CheckpointId} parent={parse.Payload?.ParentCheckpointId ?? "none"} "
        + $"saveTick={parse.Payload?.SaveTick} families={parse.Payload?.Families.Count}");
    var result = saveReader.ReadDetailed(path);
    Console.WriteLine($"   observation status={result.Observation.Status} game={result.Observation.GameVersion} "
        + $"checkpointMetrics={result.Checkpoint?.Metrics.Count.ToString(inv) ?? "none"} meta={result.Checkpoint?.CheckpointMetadata}");
    var now = new Dictionary<string, string>(StringComparer.Ordinal);
    if (result.Checkpoint != null)
        foreach (var (key, entry) in result.Checkpoint.Metrics)
            foreach (var (part, value) in entry.Values)
                now[part.Length == 0 ? key : key + "[" + part + "]"] = Format(value);
    if (previous != null)
    {
        var changed = now.Keys.Union(previous.Keys).Order(StringComparer.Ordinal)
            .Where(k => !now.TryGetValue(k, out var a) || !previous.TryGetValue(k, out var b) || a != b).ToList();
        Console.WriteLine($"   changed since {previousName}: {changed.Count}");
        foreach (var k in changed)
        {
            Console.WriteLine($"     {k}: {previous.GetValueOrDefault(k, "absent")} -> {now.GetValueOrDefault(k, "absent")}");
            changedKeys.Add(k.Split('[')[0]);
        }
    }
    previous = now;
    previousName = name;
}

// The profile is opened from a copy: opening it can write it (a ledger change is saved).
var copy = Path.Combine(Path.GetTempPath(), "rws-event-analyzer-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(copy);
try
{
    foreach (var file in Directory.EnumerateFiles(args[1]))
        File.Copy(file, Path.Combine(copy, Path.GetFileName(file)));
    var session = new ProfileSession(new ProfileStore(copy), () => DateTime.UtcNow);
    Console.WriteLine($"== profile load={session.LoadResult.Source} readOnly={session.LoadResult.ReadOnly}");
    Console.WriteLine("   observations " + session.Read(p => string.Join(" ", p.MetricObservations.GroupBy(o => o.Source).OrderBy(g => g.Key)
        .Select(g => $"{g.Key}={g.Count()}")) + $" saveObservations={p.SaveObservations.Count}"));
    var units = MetricRegistry.ImplementedCatalog.Select(d => d.Acquisition).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
    var view = session.GetView(new StatsOptions(), units);
    foreach (var colony in view.Numeric.Colonies)
        Console.WriteLine($"   colony {colony.Key} observations={colony.Observations} lineage={colony.Lineage}");
    foreach (var row in view.Numeric.Overall.Where(r => changedKeys.Contains(r.Definition.Key)).OrderBy(r => r.Definition.Key, StringComparer.Ordinal))
    {
        var v = row.Value;
        var parts = string.Join(", ", row.Breakdowns.Select(b => b.Breakdown + "=" + (b.Value.Number?.ToString("G6", inv) ?? "null")));
        Console.WriteLine($"   {row.Definition.Key}: {v.Number?.ToString("G6", inv) ?? "null"} ({v.Coverage}) [{parts}]");
    }
    // The vanilla cumulative counters between saves with a direct checkpoint parent edge (a bounded interval, never a total).
    foreach (var i in session.ReadHistoricalCounterIntervals().OrderBy(i => i.StartTick).ThenBy(i => i.ChildSaveName, StringComparer.Ordinal))
        Console.WriteLine($"   interval {i.ParentSaveName} -> {i.ChildSaveName} ticks={i.StartTick}..{i.EndTick} "
            + $"raids+={i.RaidsIncrease} colonistDeaths+={i.ColonistDeathsIncrease} ({i.Coverage})");
    session.Close();
}
finally
{
    Directory.Delete(copy, recursive: true);
}
return 0;
