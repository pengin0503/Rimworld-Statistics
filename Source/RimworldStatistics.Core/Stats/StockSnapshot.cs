namespace RimworldStatistics.Core;

/// <summary>One distinct player-owned item from a loaded home map or a player pawn's owned container.</summary>
public readonly record struct StockItemFact(
    string Id, string Def, int Count, double NutritionPerUnit,
    bool Weapon, bool Medicine, bool Legendary, bool Persona, bool InPlayerHome = true);

/// <summary>
/// Stock measured at one save. No direct game Thing or ThingOwner reference is retained.
/// Equipped and minified items are counted once by their actual ThingID.
/// </summary>
public static class StockSnapshot
{
    public const string FoodLatest = "stock.food_nutrition.latest";
    public const string FoodMin = "stock.food_nutrition.min";
    public const string SilverLatest = "stock.silver.latest";
    public const string MaterialsLatest = "stock.materials.latest";
    public const string WeaponsPeak = "stock.weapons.peak";
    public const string WeaponDefsPeak = "stock.weapon_defs.peak";
    public const string LegendaryWeaponsPeak = "stock.legendary_weapons.peak";
    public const string PersonaWeaponsPeak = "stock.persona_weapons.peak";
    public const string MedicinePeak = "stock.medicine.peak";

    public static Dictionary<string, MetricEntry> Build(IEnumerable<StockItemFact> items, bool partial = false)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var weaponDefs = new HashSet<string>(StringComparer.Ordinal);
        var materials = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            ["Steel"] = 0, ["ComponentIndustrial"] = 0, ["ComponentSpacer"] = 0,
        };
        double food = 0;
        long silver = 0, weapons = 0, legendary = 0, persona = 0, medicine = 0;
        foreach (var item in items)
        {
            if (!item.InPlayerHome)
                continue;
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrEmpty(item.Def)
                || item.Count < 0 || !double.IsFinite(item.NutritionPerUnit) || item.NutritionPerUnit < 0)
            {
                partial = true;
                continue;
            }
            if (!ids.Add(item.Id))
                continue;
            food += item.Count * item.NutritionPerUnit;
            if (item.Def == "Silver") silver += item.Count;
            if (materials.ContainsKey(item.Def))
                materials[item.Def] += item.Count;
            if (item.Medicine) medicine += item.Count;
            if (item.Weapon)
            {
                weapons += item.Count;
                weaponDefs.Add(item.Def);
                if (item.Legendary) legendary += item.Count;
                if (item.Persona) persona += item.Count;
            }
        }
        var c = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        void Count(string key, long n) => result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue { Coverage = c, IntegerValue = n });
        void Nutrition(string key) => result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue { Coverage = c, RealValue = food });
        Nutrition(FoodLatest);
        Nutrition(FoodMin);
        Count(SilverLatest, silver);
        Count(WeaponsPeak, weapons);
        Count(WeaponDefsPeak, weaponDefs.Count);
        Count(LegendaryWeaponsPeak, legendary);
        Count(PersonaWeaponsPeak, persona);
        Count(MedicinePeak, medicine);
        var m = MetricEntry.Of(MetricKeys.Revision(MaterialsLatest), new MetricValue { Coverage = c, IntegerValue = materials.Values.Sum() });
        foreach (var (key, count) in materials)
            m.Values[key] = new MetricValue { Coverage = c, IntegerValue = count };
        result[MaterialsLatest] = m;
        return result;
    }
}
