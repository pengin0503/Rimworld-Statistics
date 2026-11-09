using System.Diagnostics;
using System.Text;
using LudeonTK;
using RimWorld;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// The harness's debug actions (dev mode → Debug actions → "RWS Verify"). Results go to the game log and to
/// <see cref="VerifyLogFile.Path"/>; long reports (status, dumps, diffs) are written in full only to the file.
/// </summary>
public static class VerifyDebugActions
{
    private const string Category = "RWS Verify";

    private static SortedDictionary<string, FlatValue>? mark;
    private static Dictionary<string, int>? markHits;
    private static int markTick;

    private static void Report(string text, string summary)
    {
        VerifyLogFile.Append(text);
        Log.Message(summary + " (full report: " + VerifyLogFile.Path + ")");
    }

    // AllowedGameStates is an enumeration of mutually-exclusive contexts, not a [Flags] mask.
    // Combining Entry | Playing hides the action in the in-game menu.
    [DebugAction(Category, "Status", allowedGameStates = AllowedGameStates.Entry)]
    public static void EntryStatus() => Status();

    [DebugAction(Category, "Status", allowedGameStates = AllowedGameStates.Playing)]
    public static void Status()
    {
        var text = VerifyProbe.Status();
        Report(text, text.Split('\n')[0]);
    }

    /// <summary>
    /// Read-only Issue #28 equivalence and timing probe for the optimized indoor-room count.
    /// The exhaustive cell walk matches the original save-time reader; the region walk
    /// matches the current reader. Require identical Room objects, not only equal counts.
    /// This is an explicit debug action, never an extra scan on the save path.
    /// </summary>
    [DebugAction(Category, "Benchmark indoor rooms", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void BenchmarkIndoorRooms()
    {
        var report = new StringBuilder();
        var maps = Find.Maps.Where(m => m.IsPlayerHome).ToList();
        var mismatch = 0;
        var errors = 0;
        report.AppendLine($"{CaseCheck.LogPrefix} rooms-benchmark game={VerifyProbe.GameVersion} homeMaps={maps.Count}");
        foreach (var map in maps)
        {
            try
            {
                var regionRooms = new HashSet<Room>();
                var watch = Stopwatch.StartNew();
                foreach (var region in map.regionGrid.AllRegions)
                {
                    var room = region.Room;
                    if (room != null && !room.PsychologicallyOutdoors)
                        regionRooms.Add(room);
                }
                watch.Stop();
                var regionMs = watch.Elapsed.TotalMilliseconds;

                var cellRooms = new HashSet<Room>();
                watch.Restart();
                foreach (var cell in CellRect.WholeMap(map).Cells)
                {
                    var room = RegionAndRoomQuery.RoomAt(cell, map);
                    if (room != null && !room.PsychologicallyOutdoors)
                        cellRooms.Add(room);
                }
                watch.Stop();
                var scanMs = watch.Elapsed.TotalMilliseconds;
                var exact = regionRooms.SetEquals(cellRooms);
                if (!exact)
                    mismatch++;
                report.AppendLine($"  map={map.uniqueID} cells={map.cellIndices.NumGridCells} regions={regionRooms.Count} scan={cellRooms.Count} " +
                    $"regionMs={regionMs:F4} scanMs={scanMs:F4} result={(exact ? "MATCH" : "MISMATCH")}");
            }
            catch (Exception e)
            {
                errors++;
                report.AppendLine($"  map={map.uniqueID} result=ERROR error={e.GetType().Name}: {e.Message}");
            }
        }
        var final = $"{CaseCheck.LogPrefix} rooms-benchmark summary maps={maps.Count} mismatches={mismatch} errors={errors} " +
            $"result={(maps.Count == 0 ? "SKIP" : mismatch + errors == 0 ? "PASS" : "FAIL")}";
        report.AppendLine(final);
        Report(report.ToString().TrimEnd(), final);
    }

    /// <summary>
    /// Non-mutating contract check for the optimized Phase B/D2 pollution reader.
    /// Measures the vanilla aggregate against the old exhaustive IsPolluted scan.
    /// No production startup cost and no map-wide callbacks in normal gameplay.
    /// </summary>
    [DebugAction(Category, "Benchmark pollution totals", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void BenchmarkPollutionTotals()
    {
        if (!ModsConfig.BiotechActive)
        {
            Report($"{CaseCheck.LogPrefix} pollution-benchmark result=SKIP reason=Biotech-disabled",
                $"{CaseCheck.LogPrefix} pollution-benchmark result=SKIP (Biotech disabled)");
            return;
        }
        var report = new StringBuilder();
        var maps = Find.Maps.Where(m => m.IsPlayerHome).ToList();
        var mismatch = 0;
        var errors = 0;
        report.AppendLine($"{CaseCheck.LogPrefix} pollution-benchmark game={VerifyProbe.GameVersion} homeMaps={maps.Count}");
        foreach (var map in maps)
        {
            try
            {
                var watch = Stopwatch.StartNew();
                var direct = map.pollutionGrid.TotalPollution;
                watch.Stop();
                var directMs = watch.Elapsed.TotalMilliseconds;
                watch.Restart();
                var full = 0;
                foreach (var cell in CellRect.WholeMap(map).Cells)
                    if (map.pollutionGrid.IsPolluted(cell))
                        full++;
                watch.Stop();
                var fullMs = watch.Elapsed.TotalMilliseconds;
                if (direct != full)
                    mismatch++;
                report.AppendLine($"  map={map.uniqueID} cells={map.cellIndices.NumGridCells} direct={direct} scan={full} " +
                    $"directMs={directMs:F4} scanMs={fullMs:F4} result={(direct == full ? "MATCH" : "MISMATCH")}");
            }
            catch (Exception e)
            {
                errors++;
                report.AppendLine($"  map={map.uniqueID} result=ERROR error={e.GetType().Name}: {e.Message}");
            }
        }
        var final = $"{CaseCheck.LogPrefix} pollution-benchmark summary maps={maps.Count} mismatches={mismatch} errors={errors} " +
            $"result={(maps.Count == 0 ? "SKIP" : mismatch + errors == 0 ? "PASS" : "FAIL")}";
        report.AppendLine(final);
        Report(report.ToString().TrimEnd(), final);
    }

    [DebugAction(Category, "Checkpoint lineage", allowedGameStates = AllowedGameStates.Playing)]
    public static void Lineage()
    {
        var text = VerifyProbe.Lineage();
        var lines = text.Split('\n');
        Report(text, string.Join("\n", lines.Where(l => !l.StartsWith("    ", StringComparison.Ordinal))));
    }

    [DebugAction(Category, "Dump values", allowedGameStates = AllowedGameStates.Playing)]
    public static void Dump()
    {
        var values = VerifyProbe.Values(out var why);
        var sb = new StringBuilder($"{CaseCheck.LogPrefix} dump tick={Find.TickManager.TicksGame} values={values.Count}{(why is null ? "" : " (" + why + ")")}\n");
        foreach (var (key, v) in values)
            sb.AppendLine($"  {VerifyValues.UnitOf(key) ?? "-"} {key} = {v.Display}");
        Report(sb.ToString().TrimEnd(), $"{CaseCheck.LogPrefix} dump: {values.Count} values");
    }

    [DebugAction(Category, "Mark (remember values now)", allowedGameStates = AllowedGameStates.Playing)]
    public static void Mark()
    {
        mark = VerifyProbe.Values(out var why);
        markHits = new Dictionary<string, int>(LiveMetrics.HitCounts(), StringComparer.Ordinal);
        markTick = Find.TickManager.TicksGame;
        var line = $"{CaseCheck.LogPrefix} mark tick={markTick} values={mark.Count}{(why is null ? "" : " (" + why + ")")}";
        VerifyLogFile.Append(line);
        Log.Message(line);
    }

    [DebugAction(Category, "Diff since mark", allowedGameStates = AllowedGameStates.Playing)]
    public static void Diff()
    {
        if (mark is null || markHits is null)
        {
            Log.Warning($"{CaseCheck.LogPrefix} No mark yet: use \"Mark (remember values now)\" first.");
            return;
        }
        var now = VerifyProbe.Values(out _);
        var changes = VerifyValues.Diff(mark, now);
        var sb = new StringBuilder($"{CaseCheck.LogPrefix} diff ticks={markTick}->{Find.TickManager.TicksGame} changes={changes.Count}\n");
        foreach (var ch in changes)
        {
            var delta = ch.Delta is { } d ? $" ({(d >= 0 ? "+" : "")}{d:0.######})" : "";
            sb.AppendLine($"  {ch.Unit ?? "-"} {ch.Key}: {ch.Before?.Display ?? "(none)"} -> {ch.After?.Display ?? "(none)"}{delta}");
        }
        foreach (var (unit, n) in LiveMetrics.HitCounts().OrderBy(kv => kv.Key, StringComparer.Ordinal))
            if (n != markHits.GetValueOrDefault(unit))
                sb.AppendLine($"  hits {unit}: +{n - markHits.GetValueOrDefault(unit)}");
        var text = sb.ToString().TrimEnd();
        VerifyLogFile.Append(text);
        Log.Message(text);
    }

    [DebugAction(Category, "Run all automated cases", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static void RunAll()
    {
        var results = VerifyGameCases.All.Select(VerifyGameCases.Run).ToList();
        foreach (var r in results)
            Write(r);
        var line = $"{CaseCheck.LogPrefix} summary pass={results.Count(r => r.Outcome == CaseOutcome.Pass)} fail={results.Count(r => r.Outcome == CaseOutcome.Fail)} "
            + $"skip={results.Count(r => r.Outcome == CaseOutcome.Skip)} error={results.Count(r => r.Outcome == CaseOutcome.Error)}";
        VerifyLogFile.Append(line);
        Log.Message(line);
    }

    [DebugAction(Category, "Run one case", allowedGameStates = AllowedGameStates.PlayingOnMap)]
    public static List<DebugActionNode> RunOne()
        => VerifyGameCases.All.Select(c => new DebugActionNode(c.Id + " — " + c.Description, DebugActionType.Action, () => Write(VerifyGameCases.Run(c)))).ToList();

    private static void Write(CaseResult r)
    {
        var line = CaseCheck.Line(r, VerifyProbe.GameVersion);
        VerifyLogFile.Append(line);
        if (r.Outcome is CaseOutcome.Fail or CaseOutcome.Error)
            Log.Error(line);
        else
            Log.Message(line);
    }
}
