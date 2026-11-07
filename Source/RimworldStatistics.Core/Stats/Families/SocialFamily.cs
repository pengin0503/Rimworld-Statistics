namespace RimworldStatistics.Core;

/// <summary>Phase C · social (ledger P-012…P-017, B-003, N1-D-009; evidence #social). Each call is a confirmed transition.</summary>
public static class SocialFamily
{
    public const string Name = "social";
    public const string Marriages = "social.marriages";
    public const string Divorces = "social.divorces";
    public const string Lovers = "social.lovers";
    public const string Fights = "social.fights";
    public const string PrisonBreaks = "social.prison_breaks";
    public const string PrisonersReleased = "social.prisoners_released";
    public const string PrisonersCaptured = "social.prisoners_captured";
    public const string AnimalBonds = "social.animal_bonds";

    /// <summary>PawnRelationDef names of 1.7.4985 (Core PawnRelationDefs).</summary>
    public const string Spouse = "Spouse";
    public const string Lover = "Lover";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Married = "married";
    private const string Loved = "lovers";
    private const string Divorced = "divorced";
    private const string Fought = "fights";
    private const string Broke = "prison_breaks";
    private const string Released = "released";
    private const string Captures = "captured";
    private const string Bonds = "bonds";
    private const string Pairs = "pairs";         // active: relation|pair → tick, only for the current tick
    private const string PairTick = "pair_tick";

    /// <summary>
    /// A direct relation was created between a normalized pair. The other side's notification for the same pair and relation in
    /// the same tick is the same transition.
    /// </summary>
    public static void Relation(FamilyState s, string relationDef, string pairKey, int tick)
    {
        if (relationDef is not (Spouse or Lover))
            return;
        if (s.Tracked(PairTick, "t") != tick)
        {
            s.Active.Remove(Pairs);
            s.Track(PairTick, "t", tick);
        }
        var key = relationDef + "|" + pairKey;
        if (s.Tracked(Pairs, key) is not null)
            return;
        // Past the cap the other side's notification could not be recognized: not counting keeps the totals lower bounds.
        if (!s.Track(Pairs, key, tick))
            return;
        s.Count(relationDef == Spouse ? Married : Loved);
    }

    public static void Divorce(FamilyState s) => s.Count(Divorced);
    public static void SocialFight(FamilyState s) => s.Count(Fought);

    public static void PrisonBreak(FamilyState s, int escaping)
    {
        if (escaping > 0)
            s.Count(Broke);
    }

    public static void PrisonerReleased(FamilyState s) => s.Count(Released);
    public static void Captured(FamilyState s) => s.Count(Captures);
    public static void AnimalBond(FamilyState s) => s.Count(Bonds);

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Married, Marriages);
        c.PutCount(Divorced, Divorces);
        c.PutCount(Loved, Lovers);
        c.PutCount(Fought, Fights);
        c.PutCount(Broke, PrisonBreaks);
        c.PutCount(Released, PrisonersReleased);
        c.PutCount(Captures, PrisonersCaptured);
        c.PutCount(Bonds, AnimalBonds);
        if (c.State.TrackerOverflowed(Pairs))
            c.MarkPartial(Marriages, Lovers);
    }
}
