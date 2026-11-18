namespace RimworldStatistics.Core;

/// <summary>Phase C · animals (ledger A-001, A-002, A-004, N1-D-006; evidence #animals). A-003 is in health_death.</summary>
public static class AnimalsFamily
{
    public const string Name = "animals";
    public const string Tames = "animals.tamed";
    public const string TamesBySpecies = "animals.tamed_by_species";
    public const string Births = "animals.birth";
    public const string BirthsBySpecies = "animals.birth_by_species";
    public const string Slaughtered = "animals.slaughtered";
    public const string SlaughteredBySpecies = "animals.slaughtered_by_species";
    public const string Hunted = "animals.hunted";
    public const string HuntedBySpecies = "animals.hunted_by_species";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Tamed_ = "tamed";
    private const string TamedSpecies = "tamed_species";
    private const string Born_ = "born";
    private const string BornSpecies = "born_species";
    private const string Slaughter = "slaughtered";
    private const string SlaughterSpecies = "slaughtered_species";
    private const string Hunt = "hunted";
    private const string HuntSpecies = "hunted_species";

    /// <summary>A taming attempt finished: counted when a wild animal now belongs to the player.</summary>
    public static void Tamed(FamilyState s, bool wasWild, bool nowPlayer, string? species = null)
    {
        if (!wasWild || !nowPlayer)
            return;
        s.Count(Tamed_);
        if (species is not null)
            OptionalTransitionRecording.RecordCount(s, TamedSpecies, key: species, withBreakdown: true);
    }

    /// <summary>A pawn was born or hatched alive (one call per pawn).</summary>
    public static void Born(FamilyState s, bool playerAnimal, string? species = null)
    {
        if (!playerAnimal)
            return;
        s.Count(Born_);
        if (species is not null)
            OptionalTransitionRecording.RecordCount(s, BornSpecies, key: species, withBreakdown: true);
    }

    /// <summary>A confirmed animal death: slaughter (designated, by execution cut) or a designated hunt by a player pawn.</summary>
    public static void Died(FamilyState s, DeathFacts d)
    {
        if (d.ColonyAnimal && d.SlaughterDesignated && d.ExecutionCut)
        {
            s.Count(Slaughter);
            if (d.AnimalSpecies is not null)
                OptionalTransitionRecording.RecordCount(s, SlaughterSpecies, key: d.AnimalSpecies, withBreakdown: true);
        }
        if (d.Wild && d.HuntDesignated && d.Killer == KillerSide.PlayerPawn)
        {
            s.Count(Hunt);
            if (d.AnimalSpecies is not null)
                OptionalTransitionRecording.RecordCount(s, HuntSpecies, key: d.AnimalSpecies, withBreakdown: true);
        }
    }

    private static void Build(FamilyOutputContext c)
    {
        PutWithSpecies(c, Tamed_, Tames, TamedSpecies, TamesBySpecies);
        PutWithSpecies(c, Born_, Births, BornSpecies, BirthsBySpecies);
        PutWithSpecies(c, Slaughter, Slaughtered, SlaughterSpecies, SlaughteredBySpecies);
        PutWithSpecies(c, Hunt, Hunted, HuntSpecies, HuntedBySpecies);
    }

    private static void PutWithSpecies(FamilyOutputContext c, string countAcc, string countKey, string speciesAcc, string speciesKey)
    {
        var s = c.State;
        c.PutCount(countAcc, countKey);
        OptionalTransitionRecording.PutCount(c, speciesAcc, speciesKey, withBreakdown: true);
        if ((s.Peek(speciesAcc)?.N ?? 0) != (s.Peek(countAcc)?.N ?? 0))
            c.MarkPartial(speciesKey);
    }
}
