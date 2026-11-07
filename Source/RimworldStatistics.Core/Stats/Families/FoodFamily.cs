namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · food (ledger W-016, W-019; evidence #food). N2-F-010 (longest time with food in stock) and N1-A-008 (harvest per
/// growing-cell day) are held: they need the stock or the growing area continuously, which no low-cost confirmed notification
/// gives (the evidence's condition: without interpolation between saves or periodic scans of every thing, not implemented).
/// </summary>
public static class FoodFamily
{
    public const string Name = "food";
    public const string SpoiledUnits = "food.spoiled.units";
    public const string Nutrition = "food.consumed.nutrition";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Spoil = "spoiled";
    private const string Eaten = "eaten";

    /// <summary>A player food stack was destroyed by rotting (CompRottable): its whole stack is lost.</summary>
    public static void Spoiled(FamilyState s, int units)
    {
        if (units > 0)
            s.Count(Spoil, units);
    }

    /// <summary>Thing.Ingested returned the nutrition actually taken.</summary>
    public static void Ate(FamilyState s, double nutrition, bool freeColonist)
    {
        if (!freeColonist || !(nutrition > 0) || !double.IsFinite(nutrition))
            return;
        var a = s.Acc(Eaten);
        a.Sum = (a.Sum ?? 0) + nutrition;
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Spoil, SpoiledUnits);
        c.PutSum(Eaten, Nutrition);
    }
}
