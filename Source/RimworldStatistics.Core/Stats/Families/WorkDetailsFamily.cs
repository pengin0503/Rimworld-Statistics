namespace RimworldStatistics.Core;

/// <summary>Independent opt-in counters for completed products and mining yield.
/// Never changes the revision or meaning of the original WorkFamily.</summary>
public static class WorkDetailsFamily
{
    public const string Name = "work_details";
    public const string CraftedByDef = "work.crafting.completed_by_def";
    public const string MinedByDef = "work.mining.units_by_def";

    private const string Crafted = "crafted";
    private const string Mined = "mined";
    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    public static void CraftedProducts(FamilyState s, IEnumerable<CraftedThing> products)
    {
        foreach (var item in products)
        {
            if (item.Count <= 0) continue;
            s.Count(Crafted, item.Count);
            s.CountBy(Crafted, string.IsNullOrEmpty(item.Def) ? MetricKeys.UnknownBucket : item.Def, item.Count);
        }
    }

    public static void MinedYield(FamilyState s, string? def, int count)
    {
        if (count <= 0) return;
        s.Count(Mined, count);
        s.CountBy(Mined, string.IsNullOrEmpty(def) ? MetricKeys.UnknownBucket : def, count);
    }

    private static void Build(FamilyOutputContext c)
    {
        var crafted = c.State.Peek(Crafted);
        var mined = c.State.Peek(Mined);
        // A dropped new key weakens only the breakdown, not the independent total.
        c.PutBreakdown(CraftedByDef, c.Counts(crafted), c.Value(integer: crafted?.N ?? 0));
        c.PutBreakdown(MinedByDef, c.Counts(mined), c.Value(integer: mined?.N ?? 0));
    }
}
