using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RimWorld;
using Verse;

namespace RimworldStatistics.RealGameEventProbe;

/// <summary>
/// Development-only probe for the real-game acceptance run of #304 (README.md next to this project). Early in a new game it
/// saves once. A little later, all in one tick, it performs representative events through the game's own API, each once and
/// then the same notification again, plus a raid that fails; then it saves again (so the two saves are a parent and child
/// checkpoint with a tick span between them), saves several more times for timing, runs the statistics MOD's own import of
/// the saves folder, and logs one line per step. The game's own errors from the repeated Kill end the validation host's
/// tick loop after that tick, which is why everything after the first save happens in it. The probe does the same game work
/// whether or not the statistics MOD is loaded, so the host's baseline run (without the MOD) and target run (with it) differ
/// only by the MOD. It never references the MOD at compile time.
/// </summary>
public sealed class ProbeComponent : GameComponent
{
    private const int FirstSaveTick = 240;
    private const int EventTick = 300;
    private const string Tag = "[rws-event-probe] ";
    private const int TimingSaves = 15;
    private bool started;
    private int phase;

    public ProbeComponent(Game game)
    {
    }

    public override void StartedNewGame() => started = true;

    public override void GameComponentTick()
    {
        var tick = Find.TickManager.TicksGame;
        if (!started || phase == 0 && tick < FirstSaveTick || phase == 1 && tick < EventTick || phase == 2)
            return;
        phase++;
        try
        {
            if (phase == 1)
            {
                Line("start", $"tick={tick} statisticsLoaded={StatisticsLoaded} colonists={Find.Maps[0].mapPawns.FreeColonistsSpawnedCount}");
                Step("save0", () => $"ms={TimedSave("rws-probe-0"):F1}");
            }
            else
            {
                RunEvents();
                RunSaves();
            }
        }
        catch (Exception e)
        {
            Line("fatal", Flat(e.ToString()));
        }
    }

    private static string Flat(string s) => s.Replace("\r", " ").Replace("\n", " | ");

    private static void Line(string step, string detail) => Log.Message(Tag + step + " " + detail);

    private static void Step(string step, Func<string> action)
    {
        try
        {
            Line(step, action());
        }
        catch (Exception e)
        {
            Line(step, "exception " + Flat(e.GetType().Name + ": " + e.Message));
        }
    }

    private static double TimedSave(string name)
    {
        var watch = Stopwatch.StartNew();
        GameDataSaveLoader.SaveGame(name);
        return watch.Elapsed.TotalMilliseconds;
    }

    private static bool StatisticsLoaded => GenTypes.GetTypeInAnyAssembly("RimworldStatistics.Metrics.LiveMetrics") != null;

    private static void RunEvents()
    {
        var map = Find.Maps[0];
        var player = Faction.OfPlayer;
        Line("events.start", $"tick={Find.TickManager.TicksGame}");

        // Research: one project without prerequisites, then the same completion again.
        var project = DefDatabase<ResearchProjectDef>.AllDefsListForReading
            .Where(p => !p.IsFinished && p.prerequisites.NullOrEmpty() && p.hiddenPrerequisites.NullOrEmpty() && p.baseCost > 0f
                && p.TechprintCount == 0 && p.RequiredAnalyzedThingCount == 0)
            .OrderBy(p => p.defName, StringComparer.Ordinal).First();
        foreach (var step in new[] { "research.finish", "research.finish.again" })
            Step(step, () =>
            {
                Find.ResearchManager.FinishProject(project, doCompletionDialog: false, researcher: null, doCompletionLetter: false);
                return $"def={project.defName} finished={project.IsFinished}";
            });

        var enemy = Find.FactionManager.AllFactionsListForReading
            .Where(f => !f.IsPlayer && f.def.humanlikeFaction && !f.Hidden && !f.defeated && !f.deactivated && f.HostileTo(player)
                && f.def.pawnGroupMakers != null && f.def.pawnGroupMakers.Any(g => g.kindDef == PawnGroupKindDefOf.Combat))
            .OrderBy(f => f.loadID).First();
        var shooter = map.mapPawns.FreeColonistsSpawned.OrderBy(p => p.thingIDNumber).First();

        // Death: a hostile pawn shot by a colonist with a rifle, then the same Kill on the dead pawn (the game logs errors
        // for that second call itself, in both runs).
        Pawn? victim = null;
        var shot = new DamageInfo(DamageDefOf.Bullet, 200f, 0f, -1f, shooter, null, ThingDef.Named("Gun_BoltActionRifle"));
        Step("kill", () =>
        {
            var kind = enemy.def.pawnGroupMakers.Where(g => g.kindDef == PawnGroupKindDefOf.Combat).SelectMany(g => g.options)
                .Select(o => o.kind).Where(k => k.RaceProps.Humanlike).OrderBy(k => k.defName, StringComparer.Ordinal).First();
            victim = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kind, enemy, forceGenerateNewPawn: true));
            GenSpawn.Spawn(victim, CellFinder.RandomClosewalkCellNear(shooter.Position, map, 6), map);
            victim.Kill(shot);
            return $"faction={enemy.def.defName} kind={kind.defName} dead={victim.Dead}";
        });
        Step("kill.again", () =>
        {
            victim!.Kill(shot);
            return $"dead={victim.Dead}";
        });

        // Raid: one that arrives, then one whose strategy has no arrival mode, so TryGenerateRaidInfo and TryExecute fail.
        bool Raid() => IncidentDefOf.RaidEnemy.Worker.TryExecute(new IncidentParms
        {
            target = map, faction = enemy, points = 300f, forced = true,
            raidStrategy = RaidStrategyDefOf.ImmediateAttack, raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn,
        });
        Step("raid", () => $"result={Raid()} faction={enemy.def.defName}");
        Step("raid.fail", () =>
        {
            var parms = new IncidentParms
            {
                target = map, faction = enemy, points = 300f, forced = true,
                raidStrategy = DefDatabase<RaidStrategyDef>.GetNamed("RwsProbe_NoArrival"),
            };
            return $"result={IncidentDefOf.RaidEnemy.Worker.TryExecute(parms)}";
        });

        // Acquisition off and on again (#304 item 4): the raid unit's box on the statistics MOD's settings page is cleared, a
        // raid arrives, the box is ticked again, and another raid arrives. Only the second of these raids is counted.
        Step("raid.unitOff", () => SetRaidUnit(false));
        Step("raid.whileOff", () => $"result={Raid()}");
        Step("raid.unitOn", () => SetRaidUnit(true));
        Step("raid.afterOn", () => $"result={Raid()}");

        // Membership: a pawn without a faction joins, then the same faction change again (the game warns about that one).
        Pawn? joiner = null;
        Step("join", () =>
        {
            joiner = PawnGenerator.GeneratePawn(new PawnGenerationRequest(PawnKindDefOf.Villager, null, forceGenerateNewPawn: true));
            GenSpawn.Spawn(joiner, CellFinder.RandomClosewalkCellNear(shooter.Position, map, 6), map);
            joiner.SetFaction(player);
            return $"colonist={joiner.IsFreeNonSlaveColonist}";
        });
        Step("join.again", () =>
        {
            joiner!.SetFaction(player);
            return $"colonist={joiner.IsFreeNonSlaveColonist}";
        });

        // The probe's own MOD incident needs colonists (#352): once with colonists on the map, so the worker runs, and once
        // with none spawned, where IncidentWorker.TryExecute returns true without running the worker.
        var incident = DefDatabase<IncidentDef>.GetNamed("RwsProbe_ColonistsRequired");
        Step("modincident.present", () =>
        {
            var before = IncidentWorker_Probe.Executed;
            var ok = incident.Worker.TryExecute(new IncidentParms { target = map, forced = true });
            return $"result={ok} workerRan={IncidentWorker_Probe.Executed - before}";
        });
        Step("modincident.absent", () =>
        {
            var away = map.mapPawns.FreeColonistsSpawned.Select(p => (Pawn: p, Cell: p.Position)).ToList();
            foreach (var (pawn, _) in away)
                pawn.DeSpawn();
            var before = IncidentWorker_Probe.Executed;
            try
            {
                var during = map.mapPawns.FreeColonistsSpawnedCount;
                var ok = incident.Worker.TryExecute(new IncidentParms { target = map, forced = true });
                return $"result={ok} workerRan={IncidentWorker_Probe.Executed - before} colonistsDuring={during}";
            }
            finally
            {
                foreach (var (pawn, cell) in away)
                    GenSpawn.Spawn(pawn, cell, map);
            }
        });
    }

    /// <summary>The save after the events, the timing saves (no event between them), and the MOD's import.</summary>
    private static void RunSaves()
    {
        Step("save1", () => $"ms={TimedSave("rws-probe-1"):F1}");
        var times = new List<string>();
        for (var i = 1; i <= TimingSaves; i++)
            times.Add(TimedSave("rws-probe-t" + i).ToString("F1"));
        Line("save.timing", "ms=" + string.Join(",", times));

        if (StatisticsLoaded)
            Step("import", RunImport);
        Line("end", $"tick={Find.TickManager.TicksGame}");
    }

    /// <summary>
    /// What the statistics MOD's settings page does when the raid unit's box is cleared (while every unit is selected) or
    /// ticked again: it changes the selection and applies it at once on the game thread. Without the MOD it does nothing, so
    /// both runs do the same game work.
    /// </summary>
    private static string SetRaidUnit(bool on)
    {
        const string unit = "raid";
        var mod = GenTypes.GetTypeInAnyAssembly("RimworldStatistics.RimworldStatisticsMod");
        if (mod == null)
            return "skipped statisticsLoaded=False";
        var settings = mod.GetProperty("Settings", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var ids = settings.GetType().GetField("experimentalUnitIds")!;
        var selected = (List<string>)ids.GetValue(settings)!;
        if (on)
        {
            if (!selected.Contains(unit))
                selected.Add(unit);
        }
        else
        {
            // The page's list: the built-in units and the optional MOD units, without derived ones.
            static IEnumerable<object> Units(string type, string property) => (IEnumerable<object>)GenTypes.GetTypeInAnyAssembly(type)!
                .GetProperty(property, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            static string Read(object o, string name) => o.GetType().GetProperty(name)!.GetValue(o)!.ToString()!;
            ids.SetValue(settings, Units("RimworldStatistics.Core.AcquisitionUnits", "All")
                .Concat(Units("RimworldStatistics.Metrics.ModProviderAdapters", "KnownUnits"))
                .Where(u => Read(u, "Kind") != "Derived" && Read(u, "Id") != unit)
                .Select(u => Read(u, "Id")).Distinct(StringComparer.Ordinal).ToList());
        }
        mod.GetMethod("ApplyLiveSelectionIfChanged", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
        var recording = GenTypes.GetTypeInAnyAssembly("RimworldStatistics.Metrics.LiveMetrics")!.GetMethod("IsRecordingNow")!
            .Invoke(null, new object[] { unit });
        return $"selected={((List<string>)ids.GetValue(settings)!).Count} recording={recording}";
    }

    /// <summary>
    /// The statistics MOD's own scan of the saves folder (it runs on a pool thread; the game thread waits here), then a flush
    /// of its profile. The saves are hashed before and after.
    /// </summary>
    private static string RunImport()
    {
        var runner = GenTypes.GetTypeInAnyAssembly("RimworldStatistics.ImportRunner")!;
        var service = GenTypes.GetTypeInAnyAssembly("RimworldStatistics.ProfileService")!;
        var running = runner.GetProperty("Running")!;
        var before = SaveHashes();
        var watch = Stopwatch.StartNew();
        runner.GetMethod("StartIfIdle")!.Invoke(null, null);
        while ((bool)running.GetValue(null)! && watch.Elapsed < TimeSpan.FromSeconds(120))
            Thread.Sleep(20);
        var scanMs = watch.Elapsed.TotalMilliseconds;
        var state = runner.GetProperty("State")!.GetValue(null);
        var session = service.GetProperty("Session")!.GetValue(null)!;
        session.GetType().GetMethod("Flush")!.Invoke(session, null);
        var after = SaveHashes();
        var unchanged = before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out var h) && h == kv.Value);
        return $"scanMs={scanMs:F1} running={(bool)running.GetValue(null)!} savesHashed={before.Count}/{after.Count} "
            + $"allUnchanged={unchanged} state={Flat(Describe(state))}";
    }

    /// <summary>SHA-256, size and modification time of every save.</summary>
    private static Dictionary<string, string> SaveHashes()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(GenFilePaths.SavedGamesFolderPath, "*.rws"))
        {
            using var stream = File.OpenRead(path);
            var info = new FileInfo(path);
            result[Path.GetFileName(path)] = Convert.ToHexString(SHA256.HashData(stream)) + "/" + info.Length + "/" + info.LastWriteTimeUtc.Ticks;
        }
        return result;
    }

    private static string Describe(object? o, int depth = 0)
    {
        if (o == null)
            return "null";
        var t = o.GetType();
        if (t.IsPrimitive || t.IsEnum || o is string or decimal or DateTime or TimeSpan)
            return o.ToString() ?? "";
        if (depth > 2)
            return t.Name;
        if (o is System.Collections.IEnumerable list)
            return "[" + string.Join(",", list.Cast<object>().Take(20).Select(x => Describe(x, depth + 1))) + "]";
        var sb = new StringBuilder(t.Name).Append('{');
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetIndexParameters().Length == 0))
        {
            object? v;
            try
            {
                v = p.GetValue(o);
            }
            catch (Exception e)
            {
                v = e.GetType().Name;
            }
            sb.Append(p.Name).Append('=').Append(Describe(v, depth + 1)).Append(';');
        }
        return sb.Append('}').ToString();
    }
}

/// <summary>The probe's own incident (Mod/Defs/Incidents.xml): it only counts that it ran.</summary>
public sealed class IncidentWorker_Probe : IncidentWorker
{
    public static int Executed;

    protected override bool TryExecuteWorker(IncidentParms parms)
    {
        Executed++;
        return true;
    }
}
