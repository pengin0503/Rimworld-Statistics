namespace RimworldStatistics.Core;

public enum RosterKind { Other, FreeColonist, Prisoner, ColonyAnimal, ColonyMech, Slave }

public readonly record struct RosterSkillFact(string Def, int Level, string Passion);

/// <summary>Detached pawn state, suitable for fixtures and snapshots without retaining game objects.</summary>
public sealed record RosterPawnFact
{
    public required string Id { get; init; }
    public RosterKind Kind { get; init; }
    public string? RaceDef { get; init; }
    public double? AgeYears { get; init; }
    public bool Augmented { get; init; }
    public bool Injured { get; init; }
    public IReadOnlyList<RosterSkillFact> Skills { get; init; } = Array.Empty<RosterSkillFact>();
}

/// <summary>All Phase B roster outputs describe a single successful-save observation.</summary>
public static class RosterSnapshot
{
    public const string Free = "population.snapshot.free";
    public const string Prisoners = "population.snapshot.prisoners";
    public const string Slaves = "population.snapshot.slaves";
    public const string Animals = "population.snapshot.animals";
    public const string Mechs = "population.snapshot.mechs";
    public const string AugmentedPeak = "population.snapshot.augmented_peak";
    public const string Skill20PawnsPeak = "population.snapshot.skill20_pawns_peak";
    public const string Skill20PerPawnPeak = "population.snapshot.skill20_per_pawn_peak";
    public const string Oldest = "population.snapshot.oldest";
    public const string AnimalDefsPeak = "population.snapshot.animal_defs_peak";
    public const string Passions = "population.snapshot.passion";
    public const string AgeMean = "population.snapshot.age_mean";
    public const string InjuredPeak = "medicine.injured_peak";

    public static Dictionary<string, MetricEntry> Build(IEnumerable<RosterPawnFact> pawns, bool partial = false)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var free = 0L;
        var prisoners = 0L;
        var slaves = 0L;
        var animals = 0L;
        var mechs = 0L;
        var augmented = 0L;
        var skill20Pawns = 0L;
        var skill20PerPawn = 0L;
        var injured = 0L;
        var animalDefs = new HashSet<string>(StringComparer.Ordinal);
        var passions = new Dictionary<string, long>(StringComparer.Ordinal);
        double oldest = 0, sumAge = 0;
        var ageN = 0L;
        foreach (var p in pawns)
        {
            if (string.IsNullOrWhiteSpace(p.Id))
            {
                partial = true;
                continue;
            }
            // Pawn can appear in several map/world/transport indices; one unique ThingID is one living pawn.
            if (!seen.Add(p.Id))
                continue;
            switch (p.Kind)
            {
                case RosterKind.FreeColonist:
                    free++;
                    if (p.Augmented) augmented++;
                    if (p.Injured) injured++;
                    var n20 = 0;
                    foreach (var skill in p.Skills)
                    {
                        if (skill.Level >= 20) n20++;
                        if (!string.IsNullOrEmpty(skill.Def))
                        {
                            var kind = skill.Passion switch
                            {
                                "None" => "None", "Minor" => "Minor", "Major" => "Major",
                                _ => "Other",
                            };
                            var key = skill.Def + ":" + kind;
                            passions[key] = passions.GetValueOrDefault(key) + 1;
                        }
                        else partial = true;
                    }
                    if (n20 > 0) skill20Pawns++;
                    skill20PerPawn = Math.Max(skill20PerPawn, n20);
                    if (p.AgeYears is { } age && double.IsFinite(age) && age >= 0)
                    {
                        oldest = Math.Max(oldest, age);
                        sumAge += age;
                        ageN++;
                    }
                    else partial = true;
                    break;
                case RosterKind.Prisoner:
                    prisoners++;
                    break;
                case RosterKind.Slave:
                    slaves++;
                    break;
                case RosterKind.ColonyAnimal:
                    animals++;
                    if (!string.IsNullOrEmpty(p.RaceDef)) animalDefs.Add(p.RaceDef);
                    else partial = true;
                    break;
                case RosterKind.ColonyMech:
                    mechs++;
                    break;
            }
        }

        var c = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        void Count(string key, long value) => result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
        {
            Coverage = c, IntegerValue = value,
        });
        void Age(string key, double? value) => result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
        {
            Coverage = value.HasValue ? c : MetricCoverage.NotApplicable, RealValue = value,
        });
        Count(Free, free);
        Count(Prisoners, prisoners);
        Count(Slaves, slaves);
        Count(Animals, animals);
        Count(Mechs, mechs);
        Count(AugmentedPeak, augmented);
        Count(Skill20PawnsPeak, skill20Pawns);
        Count(Skill20PerPawnPeak, skill20PerPawn);
        Count(AnimalDefsPeak, animalDefs.Count);
        Count(InjuredPeak, injured);
        Age(Oldest, ageN > 0 ? oldest : null);
        result[AgeMean] = MetricEntry.Of(MetricKeys.Revision(AgeMean), new MetricValue
        {
            Coverage = ageN > 0 ? c : MetricCoverage.NotApplicable,
            RealValue = ageN > 0 ? sumAge / ageN : null, Numerator = sumAge, Denominator = ageN,
        });
        var nPassions = passions.Values.Sum();
        var passion = MetricEntry.Of(MetricKeys.Revision(Passions), new MetricValue { Coverage = c, IntegerValue = nPassions });
        foreach (var (k, value) in passions)
            passion.Values[k] = new MetricValue { Coverage = c, IntegerValue = value };
        result[Passions] = passion;
        return result;
    }
}
