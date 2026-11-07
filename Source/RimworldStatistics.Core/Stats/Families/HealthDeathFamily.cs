namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · health_death (ledger B-001, B-020…B-022, P-004, N1-B-004, N1-B-006, N1-C-002/P-010, N1-C-009, N2-C-005, N2-C-006,
/// N2-C-009, N3-H-003, N3-X-006, N5-B-001…N5-B-005, N5-B-009, N5-M-003, A-003; evidence #health-death). One call per confirmed
/// death: the adapter calls it only when Pawn.Kill turned a living pawn dead, so a resurrected pawn's next death is another one.
/// </summary>
public static class HealthDeathFamily
{
    public const string Name = "health_death";

    public const string HostileKills = "combat.hostile_kills";
    public const string LongestDeathGap = "population.death.longest_gap";
    public const string DeathsByRaid = "population.death.raid_causes";
    public const string KillsPerPawnMax = "combat.hostile_kills.per_pawn_max";
    public const string DeathCauses = "population.death.causes";
    public const string TrapKills = "combat.kills.trap";
    public const string KillsByVictimKind = "combat.kills.victim_kind";
    public const string OldestDeathAge = "population.death.oldest";
    public const string DiseaseDeathRate = "population.death.disease_rate";
    public const string TurretKills = "combat.kills.turret";
    public const string TameAnimalKills = "combat.kills.tame_animal";
    public const string ColonyAnimalRaidDeaths = "animals.death.raid";
    public const string DiseaseDeaths = "population.death.disease";
    public const string CaravanMemberDeaths = "caravan.member_deaths";
    public const string RangedKills = "combat.kills.ranged";
    public const string MeleeKills = "combat.kills.melee";
    public const string UnarmedKills = "combat.kills.unarmed";
    public const string ExplosiveKills = "combat.kills.explosive";
    public const string EnvironmentKills = "combat.kills.environment";
    public const string PlayerMechKills = "combat.kills.player_mech";
    public const string FirstDeathDay = "population.death.first_day";
    public const string ColonyAnimalDeaths = "animals.death.count";

    public const string RaidCause = "raid";
    public const string OtherCause = "other";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Kills = "kills";
    private const string ByPawn = "kills_by_pawn";
    private const string ByKind = "kills_by_kind";
    private const string ByActor = "kills_by_actor";
    private const string ByMethod = "kills_by_method";
    private const string ColonistDeaths = "colonist_deaths";   // gaps and first tick
    private const string Causes = "causes";
    private const string RaidCauses = "raid_causes";
    private const string Classified = "classified";
    private const string Disease = "disease";
    private const string Age = "age";
    private const string Caravan = "caravan";
    private const string AnimalDeaths = "animal_deaths";
    private const string AnimalRaid = "animal_raid";

    public static void Died(FamilyState s, DeathFacts d, int tick)
    {
        if (d.HostileToPlayer && d.Killer is not (KillerSide.Unknown or KillerSide.NotPlayer))
        {
            s.Count(Kills);
            s.CountBy(ByKind, d.VictimKind);
            s.CountBy(ByActor, d.Killer.ToString());
            s.CountBy(ByMethod, d.Method.ToString());
            // Only a kill credited to one player pawn counts for a person; a kill known only by its cause does not.
            if (d.Killer == KillerSide.PlayerPawn && d.KillerPawnId is { } killer)
                s.CountBy(ByPawn, killer);
        }

        if (d.FreeColonist)
        {
            s.Gap(ColonistDeaths, tick);
            s.CountBy(Causes, d.Cause ?? MetricKeys.UnknownBucket);
            if (d.Cause != null)
            {
                s.Count(Classified);
                if (d.Disease)
                    s.Count(Disease);
            }
            s.CountBy(RaidCauses, d.KillerIsRaider switch
            {
                true => RaidCause,
                false when d.Cause != null => OtherCause,
                _ => MetricKeys.UnknownBucket,
            });
            if (d.AgeYears is { } age)
                s.Observe(Age, age);
            if (d.InPlayerCaravan)
                s.Count(Caravan);
        }

        if (d.ColonyAnimal)
        {
            s.Count(AnimalDeaths);
            if (d.KillerIsRaider == true)
                s.Count(AnimalRaid);
        }
    }

    private static void Build(FamilyOutputContext c)
    {
        var s = c.State;
        c.PutCount(Kills, HostileKills);
        c.PutTop(ByPawn, KillsPerPawnMax);
        c.PutBreakdown(KillsByVictimKind, c.Counts(s.Peek(ByKind)), c.Value(integer: s.Peek(Kills)?.N ?? 0));
        var actor = s.Peek(ByActor)?.By;
        var method = s.Peek(ByMethod)?.By;
        long Of(Dictionary<string, double>? by, object key) => by != null && by.TryGetValue(key.ToString()!, out var v) ? (long)v : 0;
        c.Put(TrapKills, c.Value(integer: Of(actor, KillerSide.PlayerTrap)));
        c.Put(TurretKills, c.Value(integer: Of(actor, KillerSide.PlayerTurret)));
        c.Put(TameAnimalKills, c.Value(integer: Of(actor, KillerSide.PlayerAnimal)));
        c.Put(PlayerMechKills, c.Value(integer: Of(actor, KillerSide.PlayerMech)));
        c.Put(RangedKills, c.Value(integer: Of(method, KillMethod.Ranged)));
        c.Put(MeleeKills, c.Value(integer: Of(method, KillMethod.Melee)));
        c.Put(UnarmedKills, c.Value(integer: Of(method, KillMethod.Unarmed)));
        c.Put(ExplosiveKills, c.Value(integer: Of(method, KillMethod.Explosive)));
        c.Put(EnvironmentKills, c.Value(integer: Of(method, KillMethod.Environment)));

        // B-020: completed gaps between consecutive colonist deaths only (the open ends are not intervals).
        c.PutExtreme(ColonistDeaths, LongestDeathGap, max: true, 1.0 / StatFormat.TicksPerDay);
        c.PutFirstDay(ColonistDeaths, FirstDeathDay);
        var deaths = (long)(s.Peek(Causes)?.By?.Values.Sum() ?? 0);
        c.PutBreakdown(DeathCauses, c.Counts(s.Peek(Causes)), c.Value(integer: deaths, partial: s.Peek(Causes)?.Overflow == true));
        c.PutBreakdown(DeathsByRaid, c.Counts(s.Peek(RaidCauses)), c.Value(integer: deaths));
        var classified = s.Peek(Classified)?.N ?? 0;
        var disease = s.Peek(Disease)?.N ?? 0;
        c.Put(DiseaseDeaths, c.Value(integer: disease));
        c.Put(DiseaseDeathRate, classified == 0
            ? c.Value(numerator: 0, denominator: 0)
            : c.Value(real: disease / (double)classified, numerator: disease, denominator: classified));
        c.PutExtreme(Age, OldestDeathAge, max: true);
        c.PutCount(Caravan, CaravanMemberDeaths);
        c.PutCount(AnimalDeaths, ColonyAnimalDeaths);
        c.PutCount(AnimalRaid, ColonyAnimalRaidDeaths);
    }
}
