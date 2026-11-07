namespace RimworldStatistics.Core;

/// <summary>Phase C · animals (ledger A-001, A-002, A-004, N1-D-006; evidence #animals). A-003 is in health_death.</summary>
public static class AnimalsFamily
{
    public const string Name = "animals";
    public const string Tames = "animals.tamed";
    public const string Births = "animals.birth";
    public const string Slaughtered = "animals.slaughtered";
    public const string Hunted = "animals.hunted";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Tamed_ = "tamed";
    private const string Born_ = "born";
    private const string Slaughter = "slaughtered";
    private const string Hunt = "hunted";

    /// <summary>A taming attempt finished: counted when a wild animal now belongs to the player.</summary>
    public static void Tamed(FamilyState s, bool wasWild, bool nowPlayer)
    {
        if (wasWild && nowPlayer)
            s.Count(Tamed_);
    }

    /// <summary>A pawn was born or hatched alive (one call per pawn).</summary>
    public static void Born(FamilyState s, bool playerAnimal)
    {
        if (playerAnimal)
            s.Count(Born_);
    }

    /// <summary>A confirmed animal death: slaughter (designated, by execution cut) or a designated hunt by a player pawn.</summary>
    public static void Died(FamilyState s, DeathFacts d)
    {
        if (d.ColonyAnimal && d.SlaughterDesignated && d.ExecutionCut)
            s.Count(Slaughter);
        if (d.Wild && d.HuntDesignated && d.Killer == KillerSide.PlayerPawn)
            s.Count(Hunt);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Tamed_, Tames);
        c.PutCount(Born_, Births);
        c.PutCount(Slaughter, Slaughtered);
        c.PutCount(Hunt, Hunted);
    }
}
