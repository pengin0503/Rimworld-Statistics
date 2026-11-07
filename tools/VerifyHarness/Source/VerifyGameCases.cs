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
}
