namespace RimworldStatistics.Core;

/// <summary>
/// Derived cross-colony records. Inputs stay in their original metric keys and
/// acquisition units: these definitions never create a new saved counter or hook.
/// </summary>
public static class CrossColonyCatalog
{
    public const string MaxPrefix = "cross.max.";
    public const string MinPrefix = "cross.min.";
    public const string TopPrefix = "cross.top.";

    // Representative saved-state values and selected lifetime event totals.
    // Do not add historical peaks here: their original Max/Min aggregations
    // already expose the observed record holder, and a "minimum peak" is not
    // the minimum value the colony ever held.
    private static readonly string[] ComparableKeys =
    {
        RosterSnapshot.Free, RosterSnapshot.Prisoners, RosterSnapshot.Animals, RosterSnapshot.Mechs,
        StockSnapshot.FoodLatest, StockSnapshot.SilverLatest, StockSnapshot.MaterialsLatest,
        FacilitiesSnapshot.RunningGenerators,
        ResearchStateSnapshot.Finished, ResearchStateSnapshot.FinishedRate,
        FactionStateSnapshot.Relations,
        MetricKeys.WealthItemsLatest, MetricKeys.WealthBuildingsLatest, MetricKeys.WealthPawnsLatest,
        MetricKeys.ModsLatest,
        ResearchFamily.Completed, MedicineFamily.Tends, MedicineFamily.MedicineUsed,
        WorkFamily.CraftedUnits, WorkFamily.MiningUnits, WorkFamily.HarvestUnits,
        TradeFamily.Deals, TradeFamily.SalesUnits, TradeFamily.BuysUnits,
        TradeFamily.SalesTotal, TradeFamily.BuysTotal,
        HealthDeathFamily.HostileKills, MembershipFamily.FirstJoin,
        BiotechFamily.MechsCreated, RoyaltyFamily.Psycasts,
    };

    // Each source stores an exhaustive per-Def split when it is measured
    // continuously and without cap overflow; legacy/incomplete splits are Partial.
    private static readonly string[] TopTypeKeys =
    {
        WorkFamily.HarvestByCrop, TradeFamily.SalesUnits, TradeFamily.BuysUnits,
        WorkDetailsFamily.CraftedByDef, WorkDetailsFamily.MinedByDef,
        WorkFamily.MealsByDef, WorkFamily.Sown_,
        StockSnapshot.MaterialsLatest, FacilitiesSnapshot.RunningGenerators,
    };

    public static bool IsDerived(MetricDefinition definition)
        => definition.Key.StartsWith(MaxPrefix, StringComparison.Ordinal)
            || definition.Key.StartsWith(MinPrefix, StringComparison.Ordinal)
            || definition.Key.StartsWith(TopPrefix, StringComparison.Ordinal);

    /// <summary>Only call after the unmodified base catalog is assembled.</summary>
    public static IReadOnlyList<MetricDefinition> Build(IReadOnlyList<MetricDefinition> baseDefinitions)
    {
        var byKey = baseDefinitions.ToDictionary(d => d.Key, StringComparer.Ordinal);
        var result = new List<MetricDefinition>();
        foreach (var key in ComparableKeys)
        {
            if (!byKey.TryGetValue(key, out var source))
                continue;
            result.Add(Derive(source, MaxPrefix, "RWS_CrossDesc_MaxColony"));
            result.Add(Derive(source, MinPrefix, "RWS_CrossDesc_MinColony"));
        }
        foreach (var key in TopTypeKeys)
            if (byKey.TryGetValue(key, out var source) && source.HasBreakdown)
                result.Add(Derive(source, TopPrefix, "RWS_CrossDesc_TopType"));
        return result;
    }

    private static MetricDefinition Derive(MetricDefinition source, string prefix, string description)
        => source with
        {
            Key = prefix + source.Key,
            // The key of the existing displayed input (not a newly persisted metric).
            StoredKey = source.Key,
            Overall = OverallAggregation.Ranking,
            HasBreakdown = false,
            // Every published definition requires a unique RWS_Metric_ key.
            // The UI resolves the localized generic template using the source label.
            LabelKey = source.LabelKey +
                (prefix == MaxPrefix ? "_CrossMax" : prefix == MinPrefix ? "_CrossMin" : "_CrossTop"),
            DescriptionKey = description,
        };
}
