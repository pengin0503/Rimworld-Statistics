// Synthetic .rws cold/warm importer benchmark. No real saves or RimWorld runtime.
// The build creates only temporary files and prints aggregate counters.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using RimworldStatistics.Core;

var count = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 55;
var paddingBytes = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 256 * 1024;
if (count < 1 || count > 3000 || paddingBytes < 1 || paddingBytes > 4 * 1024 * 1024)
    throw new ArgumentOutOfRangeException(nameof(args), "count=1..3000 and padding=1..4 MiB");

var savesDir = Path.Combine(Path.GetTempPath(), "rws-import-synth-" + Guid.NewGuid().ToString("N"));
var profileDir = Path.Combine(Path.GetTempPath(), "rws-import-profile-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(savesDir);
Directory.CreateDirectory(profileDir);
try
{
    const string benchMods = "<modIds><li>ludeon.rimworld</li></modIds>";
    var filler = new string('x', paddingBytes);
    for (var i = 0; i < count; i++)
    {
        var xml = $"""
        <?xml version="1.0" encoding="utf-8"?>
        <savegame><meta><gameVersion>1.7.5000 rev1275</gameVersion>{benchMods}</meta>
        <game>
        <info><startingTile>100,0</startingTile></info>
        <tickManager><ticksGame>60000</ticksGame></tickManager>
        <scenario><name>Synthetic</name><playerFaction><factionDef>PlayerColony</factionDef></playerFaction></scenario>
        <world><info><seedString>synthetic-benchmark-{i}</seedString><persistentRandomValue>{i + 1}</persistentRandomValue></info>
        <factionManager><allFactions><li><def>PlayerColony</def><loadID>14</loadID></li></allFactions></factionManager>
        <worldObjects><worldObjects><li><def>Settlement</def><ID>42</ID><faction>Faction_14</faction></li></worldObjects></worldObjects>
        </world>
        <maps><li><uniqueID>0</uniqueID><mapInfo><parent>WorldObject_42</parent></mapInfo>
        <things><thing><hugeIgnoredModPayload>{filler}</hugeIgnoredModPayload></thing></things></li></maps>
        <components><li Class="RimworldStatistics.GameComponent_RimworldStatistics">
        <campaignId>synthetic-benchmark-{i}</campaignId></li></components>
        </game></savegame>
        """;
        File.WriteAllText(Path.Combine(savesDir, $"synthetic-{i:D4}.rws"), xml, new UTF8Encoding(false));
    }

    var session = new ProfileSession(new ProfileStore(profileDir), () => DateTime.UtcNow);
    var importer = new ImportEngine(new SaveReader());
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var allocStart = GC.GetTotalAllocatedBytes(precise: true);
    var timer = Stopwatch.StartNew();
    var cold = importer.Run(savesDir, session, null, CancellationToken.None);
    session.Flush();
    var coldMs = timer.Elapsed.TotalMilliseconds;
    var coldAlloc = GC.GetTotalAllocatedBytes(precise: true) - allocStart;

    var allocWarm = GC.GetTotalAllocatedBytes(precise: true);
    timer.Restart();
    var warm = importer.Run(savesDir, session, null, CancellationToken.None);
    session.Flush();
    var warmMs = timer.Elapsed.TotalMilliseconds;
    var warmAllocBytes = GC.GetTotalAllocatedBytes(precise: true) - allocWarm;

    var homes = session.Read(p => p.MetricObservations.Count(o =>
        o.Source == MetricObservationSource.SaveFile
        && o.Metrics.TryGetValue(FacilitiesSnapshot.HomeMapsPeak, out var e)
        && e.Values[MetricEntry.Scalar].Coverage == MetricCoverage.Partial));
    if (cold.Read != count || cold.Failed != 0 || warm.Read != 0
        || warm.Skipped != count || warm.Failed != 0 || homes != count)
        throw new InvalidOperationException("Synthetic import acceptance contract failed.");

    Console.WriteLine("saves,padding_bytes,cold_ms,warm_ms,cold_alloc_bytes,warm_alloc_bytes,peak_working_set_bytes,home_partial");
    Console.WriteLine(string.Join(",",
        count.ToString(CultureInfo.InvariantCulture), paddingBytes.ToString(CultureInfo.InvariantCulture),
        coldMs.ToString("F1", CultureInfo.InvariantCulture), warmMs.ToString("F1", CultureInfo.InvariantCulture),
        coldAlloc.ToString(CultureInfo.InvariantCulture), warmAllocBytes.ToString(CultureInfo.InvariantCulture),
        Process.GetCurrentProcess().PeakWorkingSet64.ToString(CultureInfo.InvariantCulture),
        homes.ToString(CultureInfo.InvariantCulture)));
}
finally
{
    Directory.Delete(savesDir, recursive: true);
    Directory.Delete(profileDir, recursive: true);
}
