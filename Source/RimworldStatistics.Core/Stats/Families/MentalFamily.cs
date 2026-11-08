namespace RimworldStatistics.Core;

/// <summary>Phase C · mental (ledger P-005, P-006, P-025; evidence #mental).</summary>
public static class MentalFamily
{
    public const string Name = "mental";
    public const string Breaks = "mental.breaks.count";
    public const string BreaksByDef = "mental.breaks.by_def";
    public const string Inspirations = "mental.inspiration.count";
    public const string InspirationsByDef = "mental.inspiration.by_def";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string BreakCount = "breaks";
    private const string InspirationCount = "inspirations";

    /// <summary>A mental break attempt: counted when it succeeded, was caused by mood, and the pawn is a free (non-slave) colonist.</summary>
    public static void Break(FamilyState s, string def, bool success, bool moodCaused, bool freeColonist)
    {
        if (!success || !moodCaused || !freeColonist)
            return;
        s.Count(BreakCount);
        s.CountBy(BreakCount, def);
    }

    /// <summary>An inspiration attempt: counted when it succeeded and really started, for a free colonist, outside dev tools.</summary>
    public static void Inspiration(FamilyState s, string def, bool success, bool started, bool freeColonist, bool debug)
    {
        if (!success || !started || !freeColonist || debug)
            return;
        s.Count(InspirationCount);
        s.CountBy(InspirationCount, def);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.CountAndBreakdown(BreakCount, Breaks, BreaksByDef);
        c.CountAndBreakdown(InspirationCount, Inspirations, InspirationsByDef);
    }
}
