namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Phase C2: combat and population families (ledger sections health_death, down, membership, raid, lifetime, social, animals;
/// evidence of the same names; plan C2 table). Facts come from the adapters; the decisions are fixed here.
/// </summary>
public sealed class NumericCombatPopulationTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private const int Day = 60000;

    private static (LiveMetricAccumulator Acc, Func<string, FamilyState> F) NewGame(params LiveFamily[] families)
    {
        var acc = LiveMetricAccumulator.StartNew(families.Select(f => f.Spec).ToList(), 0);
        return (acc, n => acc.Family(n)!);
    }

    private static MetricObservation Save(LiveMetricAccumulator acc, int tick)
    {
        var d = acc.PrepareSave(tick, T0, "S" + tick)!;
        acc.Commit(d);
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "c" }, null, null);
    }

    private static MetricValue V(MetricObservation o, string key) => o.Scalar(key, MetricKeys.Revision(key))!;

    private static Dictionary<string, MetricValue> B(MetricObservation o, string key)
        => o.Metrics[key].Values.Where(kv => kv.Key != MetricEntry.Scalar).ToDictionary(kv => kv.Key, kv => kv.Value);

    private static DeathFacts Enemy(string id, KillerSide killer, KillMethod method = KillMethod.Ranged, string? killerPawn = null,
        string kind = DeathFacts.Humanlike, bool? raider = null) => new()
    {
        PawnId = id, HostileToPlayer = true, VictimKind = kind, Killer = killer, KillerPawnId = killerPawn, Method = method,
        Cause = "damage:Bullet", KillerIsRaider = raider,
    };

    private static DeathFacts Colonist(string id, double age, string? cause, bool disease = false, bool caravan = false, bool? raider = null) => new()
    {
        PawnId = id, FreeColonist = true, VictimKind = DeathFacts.Humanlike, AgeYears = age, Cause = cause, Disease = disease,
        InPlayerCaravan = caravan, KillerIsRaider = raider, Killer = KillerSide.Unknown,
    };

    // ----- health-death -----

    [Fact]
    public void Kills_CountOnlyHostileVictimsOfThePlayerSide()
    {
        var (acc, f) = NewGame(HealthDeathFamily.Family);
        var s = f(HealthDeathFamily.Name);
        HealthDeathFamily.Died(s, Enemy("Thing_Raider1", KillerSide.PlayerPawn, KillMethod.Ranged, "Thing_Human1"), 100);
        HealthDeathFamily.Died(s, Enemy("Thing_Raider2", KillerSide.NotPlayer), 110);                // enemies fighting each other
        HealthDeathFamily.Died(s, Enemy("Thing_Raider3", KillerSide.Unknown, KillMethod.Unknown), 120); // bled out, attribution lost
        // Resurrected and killed again: a second death is a second kill.
        HealthDeathFamily.Died(s, Enemy("Thing_Raider1", KillerSide.PlayerPawn, KillMethod.Melee, "Thing_Human1"), 200);
        HealthDeathFamily.Died(s, Enemy("Thing_Bug1", KillerSide.PlayerTurret, KillMethod.Ranged, kind: DeathFacts.Insect), 300);
        HealthDeathFamily.Died(s, Enemy("Thing_Mech1", KillerSide.PlayerTrap, KillMethod.Unknown, kind: DeathFacts.Mechanoid), 310);
        HealthDeathFamily.Died(s, Enemy("Thing_Raider4", KillerSide.PlayerAnimal, KillMethod.Melee, "Thing_Wolf1"), 320);
        HealthDeathFamily.Died(s, Enemy("Thing_Raider5", KillerSide.PlayerPawn, KillMethod.Explosive, "Thing_Human2"), 330);
        HealthDeathFamily.Died(s, Enemy("Thing_Raider6", KillerSide.PlayerMech, KillMethod.Ranged, "Thing_Mech9"), 340);
        HealthDeathFamily.Died(s, Enemy("Thing_Raider7", KillerSide.PlayerOther, KillMethod.Environment), 350);
        HealthDeathFamily.Died(s, Enemy("Thing_Raider8", KillerSide.PlayerPawn, KillMethod.Unarmed, "Thing_Human2"), 360);
        var o = Save(acc, 1000);
        Assert.Equal(9, V(o, HealthDeathFamily.HostileKills).IntegerValue);
        Assert.Equal(1, V(o, HealthDeathFamily.TrapKills).IntegerValue);
        Assert.Equal(1, V(o, HealthDeathFamily.TurretKills).IntegerValue);
        Assert.Equal(1, V(o, HealthDeathFamily.TameAnimalKills).IntegerValue);
        Assert.Equal(1, V(o, HealthDeathFamily.PlayerMechKills).IntegerValue);
        Assert.Equal((3L, 2L, 1L, 1L, 1L), (V(o, HealthDeathFamily.RangedKills).IntegerValue!.Value, V(o, HealthDeathFamily.MeleeKills).IntegerValue!.Value,
            V(o, HealthDeathFamily.UnarmedKills).IntegerValue!.Value, V(o, HealthDeathFamily.ExplosiveKills).IntegerValue!.Value,
            V(o, HealthDeathFamily.EnvironmentKills).IntegerValue!.Value));
        var kinds = B(o, HealthDeathFamily.KillsByVictimKind);
        Assert.Equal((7L, 1L, 1L), (kinds[DeathFacts.Humanlike].IntegerValue!.Value, kinds[DeathFacts.Insect].IntegerValue!.Value, kinds[DeathFacts.Mechanoid].IntegerValue!.Value));
        Assert.Equal(2, V(o, HealthDeathFamily.KillsPerPawnMax).IntegerValue);    // Human1 and Human2 tie at 2
        Assert.Equal(new[] { "Thing_Human1", "Thing_Human2" }, B(o, HealthDeathFamily.KillsPerPawnMax).Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ColonistDeaths_CausesGapsDiseaseAndAge()
    {
        var (acc, f) = NewGame(HealthDeathFamily.Family);
        var s = f(HealthDeathFamily.Name);
        HealthDeathFamily.Died(s, Colonist("Thing_Human1", 30, "damage:Bullet", raider: true), 2 * Day);
        HealthDeathFamily.Died(s, Colonist("Thing_Human2", 61.5, "hediff:Plague", disease: true), 5 * Day);
        HealthDeathFamily.Died(s, Colonist("Thing_Human3", 20, "hediff:BloodLoss", raider: false, caravan: true), 6 * Day);
        HealthDeathFamily.Died(s, Colonist("Thing_Human4", 25, null), 10 * Day);   // no cause known
        var o = Save(acc, 11 * Day);
        Assert.Equal(4.0, V(o, HealthDeathFamily.LongestDeathGap).RealValue);    // gaps 3, 1, 4 days
        Assert.Equal(2.0, V(o, HealthDeathFamily.FirstDeathDay).RealValue);
        var causes = B(o, HealthDeathFamily.DeathCauses);
        Assert.Equal(new[] { MetricKeys.UnknownBucket, "damage:Bullet", "hediff:BloodLoss", "hediff:Plague" }, causes.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(1, V(o, HealthDeathFamily.DiseaseDeaths).IntegerValue);
        var rate = V(o, HealthDeathFamily.DiseaseDeathRate);
        Assert.Equal((1.0, 3.0), (rate.Numerator!.Value, rate.Denominator!.Value));   // unknown causes are not in the denominator
        Assert.Equal(61.5, V(o, HealthDeathFamily.OldestDeathAge).RealValue);
        Assert.Equal(1, V(o, HealthDeathFamily.CaravanMemberDeaths).IntegerValue);
        var raid = B(o, HealthDeathFamily.DeathsByRaid);
        Assert.Equal((1L, 1L, 2L), (raid["raid"].IntegerValue!.Value, raid["other"].IntegerValue!.Value, raid[MetricKeys.UnknownBucket].IntegerValue!.Value));
    }

    [Fact]
    public void AnimalDeaths_AndRaidAttribution()
    {
        var (acc, f) = NewGame(HealthDeathFamily.Family, AnimalsFamily.Family);
        var hd = f(HealthDeathFamily.Name);
        var an = f(AnimalsFamily.Name);
        var slaughter = new DeathFacts { PawnId = "Thing_Cow1", ColonyAnimal = true, VictimKind = DeathFacts.Other, SlaughterDesignated = true, ExecutionCut = true, Killer = KillerSide.PlayerPawn };
        var battle = new DeathFacts { PawnId = "Thing_Cow2", ColonyAnimal = true, VictimKind = DeathFacts.Other, KillerIsRaider = true, Killer = KillerSide.NotPlayer };
        var hunt = new DeathFacts { PawnId = "Thing_Deer1", Wild = true, VictimKind = DeathFacts.Other, HuntDesignated = true, Killer = KillerSide.PlayerPawn };
        var accident = new DeathFacts { PawnId = "Thing_Boar1", Wild = true, VictimKind = DeathFacts.Other, Killer = KillerSide.PlayerPawn };
        foreach (var d in new[] { slaughter, battle, hunt, accident })
        {
            HealthDeathFamily.Died(hd, d, 100);
            AnimalsFamily.Died(an, d);
        }
        var o = Save(acc, 1000);
        Assert.Equal(2, V(o, HealthDeathFamily.ColonyAnimalDeaths).IntegerValue);
        Assert.Equal(1, V(o, HealthDeathFamily.ColonyAnimalRaidDeaths).IntegerValue);
        Assert.Equal(1, V(o, AnimalsFamily.Slaughtered).IntegerValue);
        Assert.Equal(1, V(o, AnimalsFamily.Hunted).IntegerValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnimalDeaths_UnknownRaidAttributionMakesOnlyRaidCountPartial(bool confirmedRaidDeath)
    {
        var (acc, f) = NewGame(HealthDeathFamily.Family);
        var s = f(HealthDeathFamily.Name);
        HealthDeathFamily.Died(s, new DeathFacts { PawnId = "Thing_Cow1", VictimKind = DeathFacts.Other, ColonyAnimal = true, KillerIsRaider = confirmedRaidDeath }, Day);
        Assert.Equal(MetricCoverage.Complete, V(Save(acc, 2 * Day), HealthDeathFamily.ColonyAnimalRaidDeaths).Coverage);

        HealthDeathFamily.Died(s, new DeathFacts { PawnId = "Thing_Cow2", VictimKind = DeathFacts.Other, ColonyAnimal = true, KillerIsRaider = null }, 3 * Day);
        var o = Save(acc, 4 * Day);
        Assert.Equal(confirmedRaidDeath ? 1L : 0L, V(o, HealthDeathFamily.ColonyAnimalRaidDeaths).IntegerValue);
        Assert.Equal(MetricCoverage.Partial, V(o, HealthDeathFamily.ColonyAnimalRaidDeaths).Coverage);
        Assert.Equal(2L, V(o, HealthDeathFamily.ColonyAnimalDeaths).IntegerValue);
        Assert.Equal(MetricCoverage.Complete, V(o, HealthDeathFamily.ColonyAnimalDeaths).Coverage);
    }

    // ----- down -----

    [Fact]
    public void Downs_CountTransitionsOnly()
    {
        var (acc, f) = NewGame(DownFamily.Family);
        var s = f(DownFamily.Name);
        DownFamily.Downed(s, new DownFacts { FreeColonist = true });
        DownFamily.Downed(s, new DownFacts { FreeColonist = true });                       // recovered, then downed again
        DownFamily.Downed(s, new DownFacts { FreeColonist = true, Toxic = true });
        DownFamily.Downed(s, new DownFacts { FreeColonist = true, SocialFight = true });
        DownFamily.Downed(s, new DownFacts { HostileToPlayer = true });
        DownFamily.Downed(s, new DownFacts());                                            // a neutral visitor
        var o = Save(acc, 1000);
        Assert.Equal(4, V(o, DownFamily.ColonistsDowned).IntegerValue);
        Assert.Equal(1, V(o, DownFamily.EnemiesDowned).IntegerValue);
        Assert.Equal(1, V(o, DownFamily.ToxicDowns).IntegerValue);
        Assert.Equal(1, V(o, DownFamily.SocialFightDowns).IntegerValue);
    }

    // ----- membership and lifetime -----

    [Fact]
    public void Membership_InitialThreeNewOneLeaveAndRejoin()
    {
        var (acc, f) = NewGame(MembershipFamily.Family, LifetimeFamily.Family);
        var m = f(MembershipFamily.Name);
        var l = f(LifetimeFamily.Name);
        MembershipFamily.Baseline(m, new[] { "Thing_A", "Thing_B", "Thing_C" });
        LifetimeFamily.Baseline(l, new[] { "Thing_A", "Thing_B", "Thing_C" }, 0, _ => new Dictionary<string, int> { ["Shooting"] = 5 });
        MembershipFamily.BecamePrisoner(m, "Thing_P", 1 * Day);
        MembershipFamily.Joined(m, "Thing_P", MembershipFamily.RoutePrisoner, 30, 4 * Day);
        LifetimeFamily.Joined(l, "Thing_P", 4 * Day, new Dictionary<string, int> { ["Shooting"] = 3 });
        MembershipFamily.Left(m, "Thing_A", 5 * Day);
        LifetimeFamily.Left(l, "Thing_A", 5 * Day);
        MembershipFamily.Joined(m, "Thing_A", MembershipFamily.RouteOther, 40, 6 * Day);   // a rejoin, not a new member
        MembershipFamily.Joined(m, "Thing_A", MembershipFamily.RouteOther, 40, 6 * Day);   // a duplicate notification
        MembershipFamily.Born(m, "Thing_Baby", 7 * Day);
        MembershipFamily.Joined(m, "Thing_Baby", MembershipFamily.RouteOther, 0, 7 * Day);  // a birth is not a join
        var o = Save(acc, 10 * Day);
        Assert.Equal(1, V(o, MembershipFamily.FirstJoin).IntegerValue);
        Assert.Equal(1, V(o, MembershipFamily.PrisonerRecruit).IntegerValue);
        Assert.Equal(5, V(o, MembershipFamily.Unique).IntegerValue);    // A, B, C, P, the baby
        Assert.Equal(1, V(o, MembershipFamily.Rejoin).IntegerValue);
        Assert.Equal(1, V(o, MembershipFamily.HumanBirths).IntegerValue);
        Assert.Equal(4.0, V(o, MembershipFamily.FirstJoinDay).RealValue);
        Assert.Equal(30.0, V(o, MembershipFamily.JoinAgeMean).RealValue);
        Assert.Equal(1, B(o, MembershipFamily.Routes)[MembershipFamily.RoutePrisoner].IntegerValue);
        Assert.Equal((3.0, 3.0), (V(o, MembershipFamily.RecruitTimeMin).RealValue!.Value, V(o, MembershipFamily.RecruitTimeMax).RealValue!.Value));
        Assert.Equal(5.0, V(o, LifetimeFamily.TenureMean).RealValue);   // A: 0 → 5 days; the open intervals are not samples
    }

    [Fact]
    public void Membership_RecaptureStartsANewRecruitmentInterval()
    {
        var (acc, f) = NewGame(MembershipFamily.Family);
        var s = f(MembershipFamily.Name);
        MembershipFamily.BecamePrisoner(s, "Thing_P", Day);
        // The capture hook reports a new transition after the escape even without an escape notification.
        MembershipFamily.BecamePrisoner(s, "Thing_P", 5 * Day);
        MembershipFamily.Joined(s, "Thing_P", MembershipFamily.RoutePrisoner, 30, 7 * Day);

        var o = Save(acc, 8 * Day);
        Assert.Equal(2.0, V(o, MembershipFamily.RecruitTimeMin).RealValue);
        Assert.Equal(2.0, V(o, MembershipFamily.RecruitTimeMax).RealValue);
    }

    /// <summary>Codex review: past the member cap, everything that needs "is this pawn new" is partial.</summary>
    [Fact]
    public void Membership_PastTheCap_DependentOutputsArePartial()
    {
        var (acc, f) = NewGame(MembershipFamily.Family);
        var m = f(MembershipFamily.Name);
        MembershipFamily.Baseline(m, Enumerable.Range(0, FamilyOps.KeyCap).Select(i => "Thing_P" + i));
        MembershipFamily.Joined(m, "Thing_New", MembershipFamily.RouteOther, 20, 100);
        var o = Save(acc, 1000);
        foreach (var key in new[] { MembershipFamily.FirstJoin, MembershipFamily.Routes, MembershipFamily.PrisonerRecruit, MembershipFamily.HumanBirths,
                     MembershipFamily.JoinAgeMean, MembershipFamily.FirstJoinDay, MembershipFamily.Unique, MembershipFamily.Rejoin })
            Assert.Equal(MetricCoverage.Partial, o.Scalar(key, 1)!.Coverage);
    }

    [Fact]
    public void Membership_SlavesKidnapsAndRescues()
    {
        var (acc, f) = NewGame(MembershipFamily.Family);
        var m = f(MembershipFamily.Name);
        MembershipFamily.Baseline(m, new[] { "Thing_A" });
        MembershipFamily.SlaveFreed(m, "Thing_S");
        MembershipFamily.Joined(m, "Thing_S", MembershipFamily.RouteSlave, 25, 100);
        MembershipFamily.Kidnapped(m, "Thing_A", 1 * Day);
        MembershipFamily.KidnapReturned(m, "Thing_A", 4 * Day);
        MembershipFamily.KidnapReturned(m, "Thing_A", 5 * Day);   // not kidnapped any more
        MembershipFamily.Kidnapped(m, "Thing_S", 2 * Day);
        MembershipFamily.KidnapLost(m, "Thing_S");                 // joined the kidnappers
        var o = Save(acc, 10 * Day);
        Assert.Equal(1, V(o, MembershipFamily.SlavesFreed).IntegerValue);
        Assert.Equal(2, V(o, MembershipFamily.Kidnaps).IntegerValue);
        Assert.Equal(1, V(o, MembershipFamily.KidnapRescues).IntegerValue);
        Assert.Equal(3.0, V(o, MembershipFamily.CaptiveMax).RealValue);
        Assert.Equal(1, B(o, MembershipFamily.Routes)[MembershipFamily.RouteSlave].IntegerValue);
    }

    [Fact]
    public void Lifetime_SkillTwentyOnceTenureAndGains()
    {
        var (acc, f) = NewGame(LifetimeFamily.Family);
        var l = f(LifetimeFamily.Name);
        LifetimeFamily.Joined(l, "Thing_A", 0, new Dictionary<string, int> { ["Crafting"] = 12 });
        LifetimeFamily.SkillChanged(l, "Thing_A", "Crafting", 19, 20);
        LifetimeFamily.SkillChanged(l, "Thing_A", "Crafting", 20, 19);
        LifetimeFamily.SkillChanged(l, "Thing_A", "Crafting", 19, 20);
        LifetimeFamily.SkillChanged(l, "Thing_B", "Crafting", 19, 20);   // no baseline: no gain sample, but a first 20
        LifetimeFamily.Joined(l, "Thing_C", 1 * Day, new Dictionary<string, int>());
        LifetimeFamily.Left(l, "Thing_C", 3 * Day);                      // 2 days
        LifetimeFamily.Left(l, "Thing_C", 4 * Day);                      // a duplicate: the interval is already closed
        LifetimeFamily.Joined(l, "Thing_D", 0, new Dictionary<string, int>());   // still here after 5 days: right-censored
        LifetimeFamily.Died(l, "Thing_E", freeColonist: true, ageYears: 70, 4 * Day);
        LifetimeFamily.Died(l, "Thing_F", freeColonist: true, ageYears: 30, 4 * Day);
        var o = Save(acc, 5 * Day);
        Assert.Equal(2, V(o, LifetimeFamily.FirstSkill20).IntegerValue);
        Assert.Equal(8, V(o, LifetimeFamily.JoinGainPeak).IntegerValue);
        Assert.Equal(2.0, V(o, LifetimeFamily.TenureMean).RealValue);
        Assert.Equal(50.0, V(o, LifetimeFamily.LifeMean).RealValue);
    }

    // ----- raid -----

    private static RaidArrival Arrival(string map, int pawns, string strategy = "ImmediateAttack", string mode = "EdgeWalkIn", double points = 500)
        => new() { MapId = map, PawnIds = Enumerable.Range(0, pawns).Select(i => map + "_r" + i + "_" + strategy + mode).ToList(), Points = points,
            ArrivalMode = mode, Strategy = strategy, FactionDef = "Pirate" };

    [Theory]
    [InlineData("resume")]
    [InlineData("missed event")]
    [InlineData("stop and reload at the same tick")]
    public void Interruptions_KeepCompletedDeathAndRaidGapsWithoutConnectingEvents(string interruption)
    {
        var specs = new[] { HealthDeathFamily.Family.Spec, RaidFamily.Family.Spec };
        var acc = LiveMetricAccumulator.StartNew(specs, 0);
        void Events(int day)
        {
            var death = acc.Family(HealthDeathFamily.Name)!;
            HealthDeathFamily.Died(death, Colonist("Thing_P" + day, 25, null), day * Day);
            RaidFamily.Arrived(acc.Family(RaidFamily.Name)!, Arrival("M" + day, 1), day * Day);
        }

        Events(1);
        Events(3);
        const int boundary = 4 * Day;
        if (interruption == "missed event")
        {
            foreach (var spec in specs)
                acc.MarkGap(spec.Name);
        }
        else
        {
            if (interruption == "stop and reload at the same tick")
                foreach (var spec in specs)
                    acc.Stop(spec.Name, boundary);
            var checkpoint = acc.PrepareSave(boundary, T0, "P")!;
            var resumeAt = interruption == "resume" ? 50 * Day : boundary;
            acc = LiveMetricAccumulator.Resume(checkpoint.Json, specs, resumeAt);
        }

        Events(100);
        Events(101);
        var o = Save(acc, 102 * Day);
        Assert.Equal(2.0, V(o, HealthDeathFamily.LongestDeathGap).RealValue);
        Assert.Equal(2.0, V(o, RaidFamily.LongestGap).RealValue);
        Assert.Equal(1.5, V(o, RaidFamily.MeanGap).RealValue);
        Assert.Equal(2, V(o, RaidFamily.MeanGap).SampleCount);
        Assert.Equal(4, V(o, RaidFamily.Raids).IntegerValue);
        Assert.Equal(MetricCoverage.Partial, V(o, RaidFamily.MeanGap).Coverage);
    }

    [Fact]
    public void Raids_CountGapsAndKinds()
    {
        var (acc, f) = NewGame(RaidFamily.Family);
        var s = f(RaidFamily.Name);
        RaidFamily.Arrived(s, Arrival("M1", 3), 1 * Day);
        RaidFamily.Arrived(s, Arrival("M1", 5, "Siege", "EdgeWalkIn", 1200), 3 * Day);
        RaidFamily.Arrived(s, Arrival("M1", 2, "ImmediateAttackBreachingSmart", "CenterDrop"), 4 * Day);
        RaidFamily.Incident(s, "Infestation");
        RaidFamily.Incident(s, "DeepDrillInfestation");
        RaidFamily.Incident(s, "Infestation_Jelly");   // insect jelly, not insects
        RaidFamily.Incident(s, "MechCluster");
        RaidFamily.Incident(s, "ManhunterPack");
        var o = Save(acc, 5 * Day);
        Assert.Equal(3, V(o, RaidFamily.Raids).IntegerValue);
        Assert.Equal(2.0, V(o, RaidFamily.LongestGap).RealValue);
        Assert.Equal(1.5, V(o, RaidFamily.MeanGap).RealValue);
        Assert.Equal(1.0, V(o, RaidFamily.FirstDay).RealValue);
        Assert.Equal(5, V(o, RaidFamily.PawnsMax).IntegerValue);
        Assert.Equal(1200, V(o, RaidFamily.PointsMax).RealValue);
        Assert.Equal((1L, 1L, 1L), (V(o, RaidFamily.Sieges).IntegerValue!.Value, V(o, RaidFamily.Breaches).IntegerValue!.Value, V(o, RaidFamily.Drops).IntegerValue!.Value));
        Assert.Equal((2L, 1L, 1L), (V(o, RaidFamily.Infestations).IntegerValue!.Value, V(o, RaidFamily.MechClusters).IntegerValue!.Value, V(o, RaidFamily.Manhunters).IntegerValue!.Value));
        Assert.Equal(3, B(o, RaidFamily.ByFaction)["Pirate"].IntegerValue);
        Assert.Equal(2, B(o, RaidFamily.ByArrivalMode)["EdgeWalkIn"].IntegerValue);
    }

    [Theory]
    [InlineData(true, true, 2L, 1L)]
    [InlineData(false, true, 0L, 1L)]
    [InlineData(true, false, 0L, 0L)]
    public void Raids_LordLossBeforeDeathConfirmation(bool playerKill, bool dead, long expectedKills, long expectedWipes)
    {
        var (acc, f) = NewGame(RaidFamily.Family);
        var s = f(RaidFamily.Name);
        var raid = Arrival("M1", 2);
        RaidFamily.Arrived(s, raid, Day);

        foreach (var pawn in raid.PawnIds)
        {
            // Lord.Notify_PawnLost runs inside Pawn.Kill, before the outer call confirms the death.
            var death = new RaidDeathScope(pawn, playerKill);
            death.DeferKilledLoss(2 * Day);
            death.DeferKilledLoss(2 * Day);
            Assert.True(RaidFamily.IsMember(s, pawn));
            death.Complete(s, dead);
            Assert.False(RaidFamily.IsMember(s, pawn));
        }

        var o = Save(acc, 3 * Day);
        Assert.Equal(expectedKills, V(o, RaidFamily.KillsMax).IntegerValue);
        Assert.Equal(expectedWipes, V(o, RaidFamily.Wiped).IntegerValue);
        Assert.Equal(1, V(o, RaidFamily.KillsMax).SampleCount);
    }

    [Fact]
    public void Raids_OutcomesDurationsAndKillsPerRaid()
    {
        var (acc, f) = NewGame(RaidFamily.Family);
        var s = f(RaidFamily.Name);
        var a = Arrival("M1", 2);
        var b = Arrival("M2", 2, "StageThenAttack");
        RaidFamily.Arrived(s, a, 1 * Day);
        RaidFamily.Arrived(s, b, 1 * Day);                      // overlapping raids on two maps
        Assert.True(RaidFamily.IsMember(s, a.PawnIds[0]));
        RaidFamily.ColonistDowned(s, "M2");
        RaidFamily.MemberKilledByPlayer(s, a.PawnIds[0]);
        RaidFamily.MemberLost(s, a.PawnIds[0], RaidLoss.Killed, 2 * Day);
        RaidFamily.MemberLost(s, a.PawnIds[1], RaidLoss.Killed, 3 * Day);   // killed by another raid's fire: not a player kill
        RaidFamily.MemberLost(s, a.PawnIds[1], RaidLoss.Killed, 3 * Day);   // a duplicate notification
        RaidFamily.MemberLost(s, b.PawnIds[0], RaidLoss.Exited, 2 * Day);
        RaidFamily.MemberLost(s, b.PawnIds[1], RaidLoss.Captured, 4 * Day);
        var d = Arrival("M4", 1, "ImmediateAttackSmart");
        RaidFamily.Arrived(s, d, 1 * Day);
        RaidFamily.MemberLost(s, d.PawnIds[0], RaidLoss.Captured, 2 * Day);   // ended with a prisoner: neither retreat nor wipe
        var c = Arrival("M3", 1);
        RaidFamily.Arrived(s, c, 5 * Day);
        RaidFamily.MapRemoved(s, "M3", 6 * Day);                 // the map vanished: outcome unknown
        var o = Save(acc, 7 * Day);
        Assert.Equal(1, V(o, RaidFamily.Wiped).IntegerValue);
        Assert.Equal(1, V(o, RaidFamily.Retreated).IntegerValue);
        Assert.Equal(1, V(o, RaidFamily.KillsMax).IntegerValue);
        var rate = V(o, RaidFamily.RepelledRate);
        Assert.Equal((2.0, 3.0), (rate.Numerator!.Value, rate.Denominator!.Value));
        Assert.Equal(1, V(o, RaidFamily.RepelledNoDown).IntegerValue);       // the M2 raid downed a colonist
        Assert.Equal(2.0, V(o, RaidFamily.DurationMean).RealValue);          // (2 + 3 + 1) ÷ 3 decided raids
        Assert.Equal(3.0, V(o, RaidFamily.DurationMax).RealValue);
        Assert.Equal(2.0, V(o, RaidFamily.DurationMinRepelled).RealValue);
        Assert.False(RaidFamily.IsMember(s, c.PawnIds[0]));
    }

    // ----- social -----

    [Fact]
    public void Social_EachTransitionOnce()
    {
        var (acc, f) = NewGame(SocialFamily.Family);
        var s = f(SocialFamily.Name);
        SocialFamily.Relation(s, SocialFamily.Spouse, "Thing_A|Thing_B", 10);
        SocialFamily.Relation(s, SocialFamily.Spouse, "Thing_A|Thing_B", 10);   // the other side's notification
        SocialFamily.Relation(s, SocialFamily.Lover, "Thing_C|Thing_D", 10);
        SocialFamily.Relation(s, "Fiance", "Thing_C|Thing_D", 10);
        SocialFamily.Divorce(s);
        SocialFamily.SocialFight(s);
        SocialFamily.PrisonBreak(s, escaping: 3);
        SocialFamily.PrisonBreak(s, escaping: 0);
        SocialFamily.PrisonerReleased(s);
        SocialFamily.Captured(s);
        SocialFamily.AnimalBond(s);
        var o = Save(acc, 1000);
        Assert.Equal((1L, 1L, 1L, 1L, 1L, 1L, 1L, 1L), (V(o, SocialFamily.Marriages).IntegerValue!.Value, V(o, SocialFamily.Lovers).IntegerValue!.Value,
            V(o, SocialFamily.Divorces).IntegerValue!.Value, V(o, SocialFamily.Fights).IntegerValue!.Value, V(o, SocialFamily.PrisonBreaks).IntegerValue!.Value,
            V(o, SocialFamily.PrisonersReleased).IntegerValue!.Value, V(o, SocialFamily.PrisonersCaptured).IntegerValue!.Value, V(o, SocialFamily.AnimalBonds).IntegerValue!.Value));
    }

    [Fact]
    public void Social_PairDuplicatesAreDroppedWithinOneTickOnly()
    {
        var (acc, f) = NewGame(SocialFamily.Family);
        var s = f(SocialFamily.Name);
        SocialFamily.Relation(s, SocialFamily.Spouse, "Thing_A|Thing_B", 10);
        SocialFamily.Relation(s, SocialFamily.Spouse, "Thing_A|Thing_B", 900);   // remarried later
        Assert.Equal(2, Save(acc, 1000).Scalar(SocialFamily.Marriages, 1)!.IntegerValue);
    }

    // ----- animals -----

    [Fact]
    public void Animals_TamingBirthsOnlyRealTransitions()
    {
        var (acc, f) = NewGame(AnimalsFamily.Family);
        var s = f(AnimalsFamily.Name);
        AnimalsFamily.Tamed(s, wasWild: true, nowPlayer: true);
        AnimalsFamily.Tamed(s, wasWild: false, nowPlayer: true);   // bought or already owned
        AnimalsFamily.Tamed(s, wasWild: true, nowPlayer: false);   // a failed attempt
        AnimalsFamily.Born(s, playerAnimal: true);
        AnimalsFamily.Born(s, playerAnimal: false);                // a wild birth
        var o = Save(acc, 1000);
        Assert.Equal(1, V(o, AnimalsFamily.Tames).IntegerValue);
        Assert.Equal(1, V(o, AnimalsFamily.Births).IntegerValue);
    }

    [Fact]
    public void Catalog_HasEveryC2Output()
    {
        var keys = new[]
        {
            HealthDeathFamily.HostileKills, HealthDeathFamily.LongestDeathGap, HealthDeathFamily.DeathsByRaid, HealthDeathFamily.KillsPerPawnMax,
            HealthDeathFamily.DeathCauses, HealthDeathFamily.TrapKills, HealthDeathFamily.KillsByVictimKind, HealthDeathFamily.OldestDeathAge,
            HealthDeathFamily.DiseaseDeathRate, HealthDeathFamily.TurretKills, HealthDeathFamily.TameAnimalKills, HealthDeathFamily.ColonyAnimalRaidDeaths,
            HealthDeathFamily.DiseaseDeaths, HealthDeathFamily.CaravanMemberDeaths, HealthDeathFamily.RangedKills, HealthDeathFamily.MeleeKills,
            HealthDeathFamily.UnarmedKills, HealthDeathFamily.ExplosiveKills, HealthDeathFamily.EnvironmentKills, HealthDeathFamily.PlayerMechKills,
            HealthDeathFamily.FirstDeathDay, HealthDeathFamily.ColonyAnimalDeaths,
            DownFamily.ColonistsDowned, DownFamily.EnemiesDowned, DownFamily.ToxicDowns, DownFamily.SocialFightDowns,
            MembershipFamily.FirstJoin, MembershipFamily.PrisonerRecruit, MembershipFamily.HumanBirths, MembershipFamily.Unique, MembershipFamily.Routes,
            MembershipFamily.Rejoin, MembershipFamily.JoinAgeMean, MembershipFamily.SlavesFreed, MembershipFamily.RecruitTimeMin, MembershipFamily.RecruitTimeMax,
            MembershipFamily.Kidnaps, MembershipFamily.KidnapRescues, MembershipFamily.CaptiveMax, MembershipFamily.FirstJoinDay,
            RaidFamily.PawnsMax, RaidFamily.ByArrivalMode, RaidFamily.LongestGap, RaidFamily.KillsMax, RaidFamily.Infestations, RaidFamily.MechClusters,
            RaidFamily.Manhunters, RaidFamily.Sieges, RaidFamily.Breaches, RaidFamily.Drops, RaidFamily.PointsMax, RaidFamily.DurationMean,
            RaidFamily.RepelledNoDown, RaidFamily.RepelledRate, RaidFamily.MeanGap, RaidFamily.DurationMax, RaidFamily.Retreated, RaidFamily.Wiped,
            RaidFamily.ByFaction, RaidFamily.DurationMinRepelled, RaidFamily.FirstDay, RaidFamily.Raids,
            LifetimeFamily.LifeMean, LifetimeFamily.TenureMean, LifetimeFamily.FirstSkill20, LifetimeFamily.JoinGainPeak,
            SocialFamily.Marriages, SocialFamily.Divorces, SocialFamily.Lovers, SocialFamily.Fights, SocialFamily.PrisonBreaks,
            SocialFamily.PrisonersReleased, SocialFamily.PrisonersCaptured, SocialFamily.AnimalBonds,
            AnimalsFamily.Tames, AnimalsFamily.Births, AnimalsFamily.Slaughtered, AnimalsFamily.Hunted,
        };
        foreach (var key in keys)
            Assert.True(MetricRegistry.Find(key) is { Source: MetricObservationSource.Checkpoint, Published: false }, key);
        Assert.Contains("P-010", MetricRegistry.Find(HealthDeathFamily.OldestDeathAge)!.LedgerIds);
        Assert.Contains("B-002", MetricRegistry.Find(DownFamily.EnemiesDowned)!.LedgerIds);
        Assert.Contains("N2-C-002", MetricRegistry.Find(RaidFamily.LongestGap)!.LedgerIds);
        Assert.Equal(AcquisitionUnits.Biotech, MetricRegistry.Find(DownFamily.ToxicDowns)!.RequiredDlc);
        Assert.Equal(AcquisitionUnits.Biotech, MetricRegistry.Find(HealthDeathFamily.PlayerMechKills)!.RequiredDlc);
        Assert.Equal(AcquisitionUnits.Biotech, MetricRegistry.Find(MembershipFamily.HumanBirths)!.RequiredDlc);
        Assert.Equal(AcquisitionUnits.Royalty, MetricRegistry.Find(RaidFamily.MechClusters)!.RequiredDlc);
    }
}
