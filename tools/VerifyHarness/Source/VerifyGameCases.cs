using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// The automated in-game cases. Each one drives a real game path (the same method the game calls, chosen from the 1.7.4985 code),
/// measures the recorded values before and after it with <see cref="VerifyProbe.Values"/>, and compares the change with the
/// expected one of the implementation notes §6. A case that needs something the colony does not have is skipped. Run them in a
/// throwaway test colony: they finish research, start inspirations and mental breaks, spawn and kill enemies and start a raid.
/// </summary>
internal static class VerifyGameCases
{
    public sealed record GameCase(string Id, string Unit, string Description, Func<GameCase, CaseResult> Run);

    public static IReadOnlyList<GameCase> All { get; } = new GameCase[]
    {
        new("research.direct", ResearchFamily.Name, "FinishProject on an unfinished project whose prerequisites are done: +1", ResearchDirect),
        new("research.prerequisites", ResearchFamily.Name, "FinishProject on a project with unfinished prerequisites: +1 per project the game finishes", ResearchPrerequisites),
        new("research.repeat", ResearchFamily.Name, "FinishProject on a finished project: +0", ResearchRepeat),
        new("research.threshold", ResearchFamily.Name, "AddProgress that crosses the cost: +1 and one duration sample", ResearchThreshold),
        new("mental.inspiration_debug", MentalFamily.Name, "TryStartInspiration with the dev tool's reason \"Debug gain\": +0", c => Inspiration(c, "Debug gain", 0)),
        new("mental.inspiration", MentalFamily.Name, "TryStartInspiration with another reason: +1", c => Inspiration(c, "RWS verify", 1)),
        new("mental.forced_break", MentalFamily.Name, "TryDoMentalBreak called directly (not from the mood check): +0", ForcedBreak),
        new("health_death.player_kill", HealthDeathFamily.Name, "Kill an enemy with a colonist's ranged damage: +1 kill, +1 ranged", c => Kill(c, KillBy.Colonist)),
        new("health_death.enemy_kill", HealthDeathFamily.Name, "Kill an enemy with another enemy's damage: +0", c => Kill(c, KillBy.Enemy)),
        new("health_death.unattributed", HealthDeathFamily.Name, "Kill an enemy without damage info: +0", c => Kill(c, KillBy.Nobody)),
        new("down.enemy", DownFamily.Name, "Damage a spawned enemy until downed: +1 enemy downed", EnemyDowned),
        new("raid.enemy", RaidFamily.Name, "Execute RaidEnemy (immediate attack, edge walk-in): +1 raid, +1 EdgeWalkIn", RaidEnemy),
        new("raid.last_kill", RaidFamily.Name, "Execute a new raid and kill every spawned raider through Pawn.Kill: last player kill is included in raid.kills.max", RaidLastKill),
        new("medicine.withdrawal", MedicineFamily.Name, "Addict a colonist, then set the chemical need 0 → 0.005 → 1 → 0: +2 withdrawal onsets", Withdrawal),
        new("medicine.hospital_bed", MedicineFamily.Name, "Tuck a colonist into a medical bed, then end the job: a stay opens, then the colonist has got up from it", HospitalBed),
        new("food.stock_recount", FoodFamily.Name, "UpdateResourceCounts on the current home map: the stock hook fires, nothing else changes", FoodStockRecount),
        new("food.growing_cells", FoodFamily.Name, "Add then remove one cell of a new growing zone: the tracked cells go +1 then back", GrowingCells),
        new("caravan.ancient_danger", CaravanFamily.Name, "Unfog a cell outside, then the centre of a watched sealed shrine twice: +1 ancient danger opened", AncientDanger),
    };

    public static CaseResult Run(GameCase c)
    {
        try
        {
            if (!MetricGates.IsRecording(c.Unit))
                return CaseCheck.Skip(c.Id, c.Unit, "the unit does not record in this session");
            if (Find.CurrentMap is null)
                return CaseCheck.Skip(c.Id, c.Unit, "no current map");
            return c.Run(c);
        }
        catch (Exception e)
        {
            return CaseCheck.Error(c.Id, c.Unit, e);
        }
    }

    /// <summary>Measures <paramref name="act"/>: the values and the unit's accepted events before and after.</summary>
    private static CaseResult Measure(GameCase c, Action act, IReadOnlyList<Expect> expected, bool requireHooks, string? note = null)
    {
        var before = VerifyProbe.Values(out var why);
        if (why != null)
            return CaseCheck.Skip(c.Id, c.Unit, why);
        var hits = VerifyProbe.Hits(c.Unit);
        act();
        var after = VerifyProbe.Values(out _);
        return CaseCheck.Evaluate(c.Id, c.Unit, expected, before, after, VerifyProbe.Hits(c.Unit) - hits, requireHooks, note);
    }

    // ----- research -----

    /// <summary>Projects already counted by the research family (their completion day is in the breakdown).</summary>
    private static HashSet<string> CountedResearch()
    {
        var prefix = ResearchFamily.CompletedTicks + "[";
        return VerifyProbe.Values(out _).Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Select(k => k[prefix.Length..^1]).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>A base-cost project the game can finish by FinishProject without techprints or analysis, not counted yet.</summary>
    private static bool Plain(ResearchProjectDef p, HashSet<string> counted)
        => p.baseCost > 0f && p.TechprintCount == 0 && p.RequiredAnalyzedThingCount == 0 && !counted.Contains(p.defName);

    /// <summary>The projects FinishProject(p) finishes (1.7.4985: it first finishes each unfinished prerequisite, recursively).</summary>
    private static void FinishedBy(ResearchProjectDef p, HashSet<ResearchProjectDef> into)
    {
        if (p.prerequisites != null)
            foreach (var q in p.prerequisites)
                if (!q.IsFinished && !into.Contains(q))
                    FinishedBy(q, into);
        into.Add(p);
    }

    private static IEnumerable<ResearchProjectDef> Projects()
        => DefDatabase<ResearchProjectDef>.AllDefsListForReading.OrderBy(p => p.defName, StringComparer.Ordinal);

    private static CaseResult ResearchDirect(GameCase c)
    {
        var counted = CountedResearch();
        var p = Projects().FirstOrDefault(p => !p.IsFinished && p.PrerequisitesCompleted && Plain(p, counted));
        if (p is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no unfinished plain project with its prerequisites done");
        return Measure(c, () => Find.ResearchManager.FinishProject(p, doCompletionDialog: false, researcher: null, doCompletionLetter: false),
            new[] { new Expect(ResearchFamily.Completed, 1) }, requireHooks: true, note: p.defName);
    }

    private static CaseResult ResearchPrerequisites(GameCase c)
    {
        var counted = CountedResearch();
        foreach (var p in Projects())
        {
            if (p.IsFinished || p.PrerequisitesCompleted || !Plain(p, counted))
                continue;
            var set = new HashSet<ResearchProjectDef>();
            FinishedBy(p, set);
            if (set.Count < 2 || !set.All(q => Plain(q, counted)))
                continue;
            return Measure(c, () => Find.ResearchManager.FinishProject(p, doCompletionDialog: false, researcher: null, doCompletionLetter: false),
                new[] { new Expect(ResearchFamily.Completed, set.Count) }, requireHooks: true,
                note: p.defName + " finishes " + string.Join(",", set.Select(q => q.defName).Order(StringComparer.Ordinal)));
        }
        return CaseCheck.Skip(c.Id, c.Unit, "no unfinished plain project with unfinished plain prerequisites");
    }

    private static CaseResult ResearchRepeat(GameCase c)
    {
        var p = Projects().FirstOrDefault(p => p.IsFinished && p.baseCost > 0f);
        if (p is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no finished project");
        return Measure(c, () => Find.ResearchManager.FinishProject(p, doCompletionDialog: false, researcher: null, doCompletionLetter: false),
            new[] { new Expect(ResearchFamily.Completed, 0) }, requireHooks: true, note: p.defName);
    }

    private static CaseResult ResearchThreshold(GameCase c)
    {
        var counted = CountedResearch();
        var p = Projects().FirstOrDefault(p => !p.IsFinished && p.PrerequisitesCompleted && Plain(p, counted));
        if (p is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no unfinished plain project with its prerequisites done");
        // AddProgress clamps to the cost and, with the prerequisites done, finishes the project (with the completion dialog).
        return Measure(c, () => Find.ResearchManager.AddProgress(p, p.Cost, null),
            new[] { new Expect(ResearchFamily.Completed, 1), new Expect(ResearchFamily.DurationMax, 1, Samples: true) }, requireHooks: true, note: p.defName);
    }

    // ----- mental -----

    private static Pawn? Colonist(Func<Pawn, bool> ok)
        => Find.CurrentMap.mapPawns.FreeColonistsSpawned.Where(p => p.IsFreeNonSlaveColonist && !p.Dead && !p.Downed && ok(p))
            .OrderBy(p => p.thingIDNumber).FirstOrDefault();

    private static CaseResult Inspiration(GameCase c, string reason, int expected)
    {
        var pawn = Colonist(p => p.mindState?.inspirationHandler != null && !p.Inspired);
        if (pawn is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no free colonist without an inspiration");
        var def = DefDatabase<InspirationDef>.AllDefsListForReading.OrderBy(d => d.defName, StringComparer.Ordinal)
            .FirstOrDefault(d => d.Worker.InspirationCanOccur(pawn));
        if (def is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no inspiration can occur for " + pawn.LabelShort);
        var started = false;
        var result = Measure(c, () => started = pawn.mindState.inspirationHandler.TryStartInspiration(def, reason, sendLetter: false),
            new[] { new Expect(MentalFamily.Inspirations, expected) }, requireHooks: true, note: def.defName);
        if (pawn.Inspiration is { } inspiration)
            pawn.mindState.inspirationHandler.EndInspiration(inspiration);
        // A refused inspiration says nothing about either case: the debug one must be refused by the mod, not by the game.
        return started ? result : result with { Outcome = CaseOutcome.Skip, Note = def.defName + ": the game refused the inspiration" };
    }

    private static CaseResult ForcedBreak(GameCase c)
    {
        var pawn = Colonist(p => p.mindState?.mentalBreaker != null && !p.InMentalState);
        if (pawn is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no free colonist outside a mental state");
        var def = DefDatabase<MentalBreakDef>.AllDefsListForReading.OrderBy(d => d.defName, StringComparer.Ordinal)
            .FirstOrDefault(d => d.intensity == MentalBreakIntensity.Minor && d.Worker.BreakCanOccur(pawn));
        if (def is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no minor mental break can occur for " + pawn.LabelShort);
        var result = Measure(c, () => pawn.mindState.mentalBreaker.TryDoMentalBreak("RWS verify", def),
            new[] { new Expect(MentalFamily.Breaks, 0) }, requireHooks: true, note: def.defName);
        pawn.MentalState?.RecoverFromState();
        return result;
    }

    // ----- combat -----

    private enum KillBy
    {
        Colonist,
        Enemy,
        Nobody,
    }

    private static Faction? EnemyFaction()
        => Find.FactionManager.RandomEnemyFaction(allowHidden: false, allowDefeated: false, allowNonHumanlike: false);

    /// <summary>A hostile humanlike of <paramref name="faction"/>, spawned near <paramref name="near"/>.</summary>
    private static Pawn SpawnEnemy(Faction faction, Pawn near)
    {
        var kind = faction.def.basicMemberKind ?? faction.RandomPawnKind();
        var pawn = PawnGenerator.GeneratePawn(kind, faction);
        var cell = CellFinder.RandomClosewalkCellNear(near.Position, near.Map, 6);
        GenSpawn.Spawn(pawn, cell, near.Map);
        return pawn;
    }

    private static ThingDef? RangedWeapon(Pawn colonist)
        => colonist.equipment?.Primary?.def is { IsRangedWeapon: true } own ? own
            : DefDatabase<ThingDef>.AllDefsListForReading.Where(d => d.IsRangedWeapon).OrderBy(d => d.defName, StringComparer.Ordinal).FirstOrDefault();

    private static CaseResult Kill(GameCase c, KillBy by)
    {
        var colonist = Colonist(_ => true);
        var faction = EnemyFaction();
        if (colonist is null || faction is null)
            return CaseCheck.Skip(c.Id, c.Unit, colonist is null ? "no free colonist on the map" : "no hostile humanlike faction");
        var victim = SpawnEnemy(faction, colonist);
        Pawn? other = by == KillBy.Enemy ? SpawnEnemy(faction, colonist) : null;
        var weapon = RangedWeapon(colonist);
        if (by == KillBy.Colonist && weapon is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no ranged weapon def");
        DamageInfo? dinfo = by switch
        {
            KillBy.Colonist => new DamageInfo(DamageDefOf.Bullet, 99999f, 999f, -1f, colonist, null, weapon),
            KillBy.Enemy => new DamageInfo(DamageDefOf.Bullet, 99999f, 999f, -1f, other, null, weapon),
            _ => null,
        };
        var expected = by == KillBy.Colonist ? 1 : 0;
        var result = Measure(c, () => victim.Kill(dinfo),
            new[] { new Expect(HealthDeathFamily.HostileKills, expected), new Expect(HealthDeathFamily.RangedKills, expected) },
            requireHooks: true, note: victim.kindDef.defName + (weapon is null ? "" : " by " + weapon.defName));
        if (other is { Destroyed: false })
            other.Destroy();
        return result;
    }

    private static CaseResult EnemyDowned(GameCase c)
    {
        var colonist = Colonist(_ => true);
        var faction = EnemyFaction();
        if (colonist is null || faction is null)
            return CaseCheck.Skip(c.Id, c.Unit, colonist is null ? "no free colonist on the map" : "no hostile humanlike faction");
        var enemy = SpawnEnemy(faction, colonist);
        var result = Measure(c, () => HealthUtility.DamageUntilDowned(enemy, allowBleedingWounds: false),
            new[] { new Expect(DownFamily.EnemiesDowned, 1), new Expect(DownFamily.ColonistsDowned, 0) }, requireHooks: true,
            note: enemy.Dead ? "the enemy died instead of going down" : null);
        if (!enemy.Destroyed)
            enemy.Destroy();
        return result;
    }

    private static CaseResult RaidEnemy(GameCase c)
    {
        var map = Find.CurrentMap;
        var faction = EnemyFaction();
        if (faction is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no hostile humanlike faction");
        var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
        parms.faction = faction;
        parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
        parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
        var executed = false;
        var result = Measure(c, () => executed = IncidentDefOf.RaidEnemy.Worker.TryExecute(parms),
            new[] { new Expect(RaidFamily.Raids, 1), new Expect(RaidFamily.ByArrivalMode + "[EdgeWalkIn]", 1), new Expect(RaidFamily.PawnsMax, 1, Samples: true) },
            requireHooks: true, note: faction.def.defName + " points=" + parms.points.ToString("0"));
        return executed ? result : result with { Outcome = CaseOutcome.Skip, Note = "the game refused the raid (" + result.Note + ")" };
    }
    /// <summary>
    /// Integration check of the real Pawn.Kill -> Lord.Notify_PawnLost ordering.
    /// The final member must be credited before the raid is closed, unlike a direct
    /// Core-only call to RaidDeathScope.Complete.
    /// </summary>
    private static CaseResult RaidLastKill(GameCase c)
    {
        var map = Find.CurrentMap;
        var colonist = Colonist(_ => true);
        var faction = EnemyFaction();
        if (colonist is null || faction is null)
            return CaseCheck.Skip(c.Id, c.Unit, colonist is null ? "no free colonist" : "no hostile humanlike faction");
        var weapon = RangedWeapon(colonist);
        if (weapon is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no ranged weapon def");

        var active = LiveMetrics.Family(RaidFamily.Name);
        if (active is null)
            return CaseCheck.Skip(c.Id, c.Unit, "the raid family has no active accumulator");

        var priorPawns = map.mapPawns.AllPawns.Where(p => p.Faction == faction && p.Spawned)
            .Select(p => p.GetUniqueLoadID()).ToHashSet(StringComparer.Ordinal);
        var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
        parms.faction = faction;
        parms.raidStrategy = RaidStrategyDefOf.ImmediateAttack;
        parms.raidArrivalMode = PawnsArrivalModeDefOf.EdgeWalkIn;
        if (!IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
            return CaseCheck.Skip(c.Id, c.Unit, "RaidEnemy refused the incident");
        var raiders = map.mapPawns.AllPawns.Where(p => p.Faction == faction && p.Spawned
                && !priorPawns.Contains(p.GetUniqueLoadID()) && RaidFamily.IsMember(active, p.GetUniqueLoadID()))
            .OrderBy(p => p.thingIDNumber).ToArray();
        if (raiders.Length == 0)
            return CaseCheck.Skip(c.Id, c.Unit, "raid executed but spawned no tracked raiders");

        var before = VerifyProbe.Values(out var why);
        if (why != null)
            return CaseCheck.Skip(c.Id, c.Unit, why);
        var priorMax = before.GetValueOrDefault(RaidFamily.KillsMax)?.Number ?? 0;
        var expectedMaxChange = Math.Max(priorMax, raiders.Length) - priorMax;
        return Measure(c, () =>
        {
            foreach (var pawn in raiders)
                pawn.Kill(new DamageInfo(DamageDefOf.Bullet, 99999f, 999f, -1f, colonist, null, weapon));
        }, new[] { new Expect(RaidFamily.KillsMax, expectedMaxChange),
            new Expect(RaidFamily.KillsMax, 1, Samples: true) },
            requireHooks: true, note: "real Pawn.Kill, raiders=" + raiders.Length + ", prior-max=" + priorMax);
    }

    // ----- held until 2026-11-08: N3-H-005, N3-H-011, N2-F-010, N1-A-008, Q-014 -----

    /// <summary>
    /// N3-H-005: the real Need_Chemical.CurLevel setter → CategoryChanged → Hediff_Addiction.Notify_NeedCategoryChanged path.
    /// Entering withdrawal twice counts twice; a change inside withdrawal is not a change of category.
    /// </summary>
    private static CaseResult Withdrawal(GameCase c)
    {
        var chemical = DefDatabase<ChemicalDef>.AllDefsListForReading.OrderBy(d => d.defName, StringComparer.Ordinal)
            .FirstOrDefault(d => d.addictionHediff?.chemicalNeed != null);
        if (chemical is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no chemical with an addiction need");
        var pawn = Colonist(p => p.health.hediffSet.GetFirstHediffOfDef(chemical.addictionHediff) == null && p.needs != null);
        if (pawn is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no free colonist without that addiction");
        var addiction = pawn.health.AddHediff(chemical.addictionHediff);
        if (addiction is not Hediff_Addiction { Need: { } need })
            return CaseCheck.Skip(c.Id, c.Unit, "the addiction gave no chemical need");
        var result = Measure(c, () =>
        {
            need.CurLevel = 0f;
            need.CurLevel = 0.005f;
            need.CurLevel = 1f;
            need.CurLevel = 0f;
        }, new[] { new Expect(MedicineFamily.Withdrawals, 2) }, requireHooks: true, note: chemical.defName);
        pawn.health.RemoveHediff(addiction);
        return result;
    }

    /// <summary>
    /// N3-H-011: the real Pawn_JobTracker.Notify_TuckedIntoBed → LayDown job → Toils_LayDown.LayDown init, and the toil's finish
    /// action at the job's end. The stay's length needs game time (and the merge window) and is checked by hand.
    /// </summary>
    private static CaseResult HospitalBed(GameCase c)
    {
        var map = Find.CurrentMap;
        var bed = map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>()
            .Where(b => b.Medical && b.SleepingSlotsCount > 0 && !b.CurOccupants.Any()).OrderBy(b => b.thingIDNumber).FirstOrDefault();
        if (bed is null)
            return CaseCheck.Skip(c.Id, c.Unit, "no free medical bed on the current map");
        var pawn = Colonist(p => !p.InBed());
        if (pawn is null || LiveMetrics.Family(MedicineFamily.Name) is not { } family)
            return CaseCheck.Skip(c.Id, c.Unit, pawn is null ? "no free colonist out of bed" : "the medicine family does not record");
        // A synchronous debug action cannot advance the real LayDown toil. Enqueuing
        // Notify_TuckedIntoBed and inspecting immediately falsely FAILs; worse, the
        // queued job can run after this test and unexpectedly change later cases.
        // Do not touch the pawn or its jobs. A normal, time-advanced medical stay is
        // the only valid positive test of this game-side hook.
        return CaseCheck.Skip(c.Id, c.Unit,
            "real LayDown toil requires game ticks; verify a medical-bed stay manually (bed " + bed.thingIDNumber + ")");
    }

    /// <summary>N2-F-010: a recount of a player home map reaches the stock hook (the spans need game time and are checked by hand).</summary>
    private static CaseResult FoodStockRecount(GameCase c)
    {
        var map = Find.CurrentMap;
        if (!map.IsPlayerHome)
            return CaseCheck.Skip(c.Id, c.Unit, "the current map is not a player home");
        return Measure(c, () => map.resourceCounter.UpdateResourceCounts(),
            new[] { new Expect(FoodFamily.SpoiledUnits, 0) }, requireHooks: true, note: "hook only");
    }

    /// <summary>N1-A-008: the real Zone.AddCell / RemoveCell path of a growing zone (the yield needs game time).</summary>
    private static CaseResult GrowingCells(GameCase c)
    {
        var map = Find.CurrentMap;
        if (LiveMetrics.Family(FoodFamily.Name) is not { } family || FoodFamily.TrackedGrowingCells(family) is not { } before)
            return CaseCheck.Skip(c.Id, c.Unit, "the food family has no tracked growing cells");
        var cell = map.AllCells.Where(x => x.InBounds(map) && !x.Fogged(map) && map.zoneManager.ZoneAt(x) == null && x.GetEdifice(map) == null
                && x.GetThingList(map).All(t => t.def.CanOverlapZones) && map.fertilityGrid.FertilityAt(x) > 0f)
            .OrderBy(x => x.z).ThenBy(x => x.x).FirstOrDefault();
        if (!cell.IsValid || map.zoneManager.ZoneAt(cell) != null)
            return CaseCheck.Skip(c.Id, c.Unit, "no free fertile cell");
        var hits = VerifyProbe.Hits(c.Unit);
        var zone = new Zone_Growing(map.zoneManager);
        map.zoneManager.RegisterZone(zone);
        zone.AddCell(cell);
        var added = FoodFamily.TrackedGrowingCells(family);
        zone.RemoveCell(cell);   // the last cell: the zone deregisters itself
        var removed = FoodFamily.TrackedGrowingCells(family);
        var ok = added == before + 1 && removed == before;
        return new CaseResult(c.Id, c.Unit, ok ? CaseOutcome.Pass : CaseOutcome.Fail, $"cells:{before}->{before + 1}->{before}",
            $"cells:{before}->{added?.ToString() ?? "?"}->{removed?.ToString() ?? "?"}", VerifyProbe.Hits(c.Unit) - hits, cell.ToString());
    }

    /// <summary>
    /// Q-014: the real FogGrid.Unfog. A cell outside every watched shrine changes nothing; unfogging a watched shrine's centre is
    /// one opening, and unfogging it again adds nothing. Needs a still sealed shrine on the current map (the starting map usually has one).
    /// </summary>
    private static CaseResult AncientDanger(GameCase c)
    {
        var map = Find.CurrentMap;
        if (LiveMetrics.Family(CaravanFamily.Name) is not { } family)
            return CaseCheck.Skip(c.Id, c.Unit, "the caravan family does not record");
        var prefix = "Map_" + map.uniqueID + "|";
        CellRect? shrine = null;
        foreach (var key in CaravanFamily.WatchedShrines(family))
            if (key.StartsWith(prefix, StringComparison.Ordinal)
                && CaravanFamily.TryParseShrineKey(key, out _, out var minX, out var minZ, out var maxX, out var maxZ))
            {
                shrine = CellRect.FromLimits(minX, minZ, maxX, maxZ);
                break;
            }
        if (shrine is not { } rect || !map.fogGrid.IsFogged(rect.CenterCell))
            return CaseCheck.Skip(c.Id, c.Unit, "no watched sealed shrine on the current map");
        var outside = map.AllCells.FirstOrDefault(x => map.fogGrid.IsFogged(x) && !rect.ExpandedBy(2).Contains(x));
        return Measure(c, () =>
        {
            if (outside.IsValid)
                map.fogGrid.Unfog(outside);
            map.fogGrid.Unfog(rect.CenterCell);
            map.fogGrid.Unfog(rect.CenterCell);
        }, new[] { new Expect(CaravanFamily.AncientDangersOpened, 1) }, requireHooks: true, note: rect.ToString());
    }
}
