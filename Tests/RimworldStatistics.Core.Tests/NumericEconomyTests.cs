namespace RimworldStatistics.Core.Tests;

/// <summary>Phase C3: work, food, trade and diplomacy (ledger sections of the same names; plan C3 table).</summary>
public sealed class NumericEconomyTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private const int Day = 60000;

    private static (LiveMetricAccumulator Acc, FamilyState S) NewGame(LiveFamily family)
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { family.Spec }, 0);
        return (acc, acc.Family(family.Name)!);
    }

    private static MetricObservation Save(LiveMetricAccumulator acc, int tick)
    {
        var d = acc.PrepareSave(tick, T0, "S" + tick)!;
        acc.Commit(d);
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "c" }, null, null);
    }

    private static MetricValue V(MetricObservation o, string key) => o.Scalar(key, MetricKeys.Revision(key))!;

    private static Dictionary<string, MetricValue> B(MetricObservation o, string key)
        => o.Metrics[key].Values.Where(kv => kv.Key != MetricEntry.Scalar).ToDictionary(kv => kv.Key, kv => kv.Value);

    private static CraftedThing Item(string def, int count, string? quality = null, double unitValue = 10, bool meal = false, bool art = false)
        => new() { Def = def, Count = count, Quality = quality, UnitMarketValue = unitValue, Meal = meal, Art = art };

    // ----- work -----

    [Fact]
    public void Crafting_CountsProductsQualitiesAndWorkers()
    {
        var (acc, s) = NewGame(WorkFamily.Family);
        WorkFamily.Crafted(s, "Thing_A", new[] { Item("Steel", 10), Item("ComponentIndustrial", 2) }, 1 * Day + 5);
        WorkFamily.Crafted(s, "Thing_B", new[] { Item("Gun_Revolver", 1, "Masterwork", 300), Item("Gun_Revolver", 1, "Masterwork", 320) }, 1 * Day + 9);
        WorkFamily.Crafted(s, "Thing_B", new[] { Item("MealSimple", 3, meal: true, unitValue: 15) }, 2 * Day + 1);
        WorkFamily.Crafted(s, "Thing_A", new[] { Item("SculptureSmall", 1, "Legendary", 900, art: true) }, 2 * Day + 2);
        var o = Save(acc, 3 * Day);
        Assert.Equal(18, V(o, WorkFamily.CraftedUnits).IntegerValue);   // 10 + 2 + 2 + 3 + 1
        var q = B(o, WorkFamily.QualityBreakdown);
        Assert.Equal((2L, 1L, 15L), (q["Masterwork"].IntegerValue!.Value, q["Legendary"].IntegerValue!.Value, q[WorkFamily.NoQuality].IntegerValue!.Value));
        Assert.Equal(2, V(o, WorkFamily.Masterwork).IntegerValue);
        Assert.Equal(1, V(o, WorkFamily.Legendary).IntegerValue);
        Assert.Equal(new[] { "Legendary" }, B(o, WorkFamily.BestQuality).Keys);
        Assert.Equal(900, V(o, WorkFamily.BestPrice).RealValue);
        Assert.Equal(1, V(o, WorkFamily.Sculptures).IntegerValue);
        Assert.Equal(3, B(o, WorkFamily.MealsByDef)["MealSimple"].IntegerValue);
        Assert.Equal(5, V(o, WorkFamily.UniqueCraftedDefs).IntegerValue);
        Assert.Equal(13, V(o, WorkFamily.BestCrafter).IntegerValue);    // A: 10 + 2 + 1
        Assert.Equal(3, V(o, WorkFamily.BestCook).IntegerValue);
        Assert.Equal(14, V(o, WorkFamily.DailyPeak).IntegerValue);      // day 1: 14 units, day 2: 4 (both complete)
    }

    [Fact]
    public void DailyPeak_IgnoresTheUnfinishedDay()
    {
        var (acc, s) = NewGame(WorkFamily.Family);
        WorkFamily.Crafted(s, "Thing_A", new[] { Item("Steel", 14) }, 1 * Day + 5);
        WorkFamily.Crafted(s, "Thing_A", new[] { Item("Steel", 50) }, 2 * Day + 5);   // day 2 is still running at the save
        Assert.Equal(14, V(Save(acc, 2 * Day + Day / 2), WorkFamily.DailyPeak).IntegerValue);
        var late = LiveMetricAccumulator.Resume(null, new[] { WorkFamily.Family.Spec }, 1 * Day + 100);
        WorkFamily.Crafted(late.Family(WorkFamily.Name)!, "Thing_A", new[] { Item("Steel", 9) }, 1 * Day + 200);   // day 1 began unrecorded
        Assert.Equal(MetricCoverage.NotApplicable, V(Save(late, 2 * Day), WorkFamily.DailyPeak).Coverage);
    }

    [Fact]
    public void Harvest_SowingCropLossAndYears()
    {
        var (acc, s) = NewGame(WorkFamily.Family);
        WorkFamily.Sown(s, "Thing_Plant1", "Plant_Rice");
        WorkFamily.Sown(s, "Thing_Plant2", "Plant_Rice");
        WorkFamily.Sown(s, "Thing_Plant3", "Plant_Corn");
        WorkFamily.Harvested(s, "Thing_A", "Thing_Plant1", "Plant_Rice", 10, 10 * Day);
        WorkFamily.Harvested(s, "Thing_A", "Thing_Plant1", "Plant_Rice", 4, 11 * Day);   // regrowth: the cohort counts once
        WorkFamily.CropDied(s, "Thing_Plant2", cold: true);
        WorkFamily.CropDied(s, "Thing_Plant9", cold: false);    // never seen sown: a loss, not in the cohort
        WorkFamily.Harvested(s, "Thing_B", "Thing_Plant3", null, 6, 70 * Day);           // crop not known
        WorkFamily.HarvestFailed(s);
        WorkFamily.Sown(s, "Thing_Plant4", "Plant_Corn");
        WorkFamily.PlantRemoved(s, "Thing_Plant4");   // cut by hand or a failed harvest: undecided, not in the rate
        WorkFamily.Harvested(s, "Thing_B", "Thing_Plant4", "Plant_Corn", 0, 71 * Day);
        var o = Save(acc, 130 * Day);
        Assert.Equal(20, V(o, WorkFamily.HarvestUnits).IntegerValue);
        Assert.Equal(4, V(o, WorkFamily.Sown_).IntegerValue);
        Assert.Equal(2, V(o, WorkFamily.SownDefs).IntegerValue);
        Assert.Equal((14L, 6L), (B(o, WorkFamily.HarvestByCrop)["Plant_Rice"].IntegerValue!.Value, B(o, WorkFamily.HarvestByCrop)[MetricKeys.UnknownBucket].IntegerValue!.Value));
        Assert.Equal(new[] { "Plant_Rice" }, B(o, WorkFamily.TopCrop).Keys);
        Assert.Equal((1L, 1L), (V(o, WorkFamily.ColdLoss).IntegerValue!.Value, V(o, WorkFamily.OtherLoss).IntegerValue!.Value));
        var rate = V(o, WorkFamily.LossRate);
        Assert.Equal((1.0, 3.0), (rate.Numerator!.Value, rate.Denominator!.Value));   // Plant2 died; Plant1 and Plant3 harvested
        Assert.Equal(1, V(o, WorkFamily.HarvestFailures).IntegerValue);
        Assert.Equal(14, V(o, WorkFamily.BestHarvester).IntegerValue);
        Assert.Equal(14, V(o, WorkFamily.HarvestYearPeak).IntegerValue);   // year 0: 14, year 1 (60…120 days): 6; year 2 is not complete
    }

    [Fact]
    public void Construction_MiningRepairsAndPods()
    {
        var (acc, s) = NewGame(WorkFamily.Family);
        WorkFamily.Constructed(s, "Thing_A", new Dictionary<string, int> { ["WoodLog"] = 25 });
        WorkFamily.Constructed(s, "Thing_A", new Dictionary<string, int> { ["Steel"] = 5, ["ComponentIndustrial"] = 1 });
        WorkFamily.ConstructionFailed(s);
        WorkFamily.Deconstructed(s);
        WorkFamily.Mined(s, "Thing_B", "Steel", 35);
        WorkFamily.Mined(s, "Thing_C", "ChunkGranite", 1);
        WorkFamily.Repaired(s);
        WorkFamily.PodsLaunched(s, 3);
        var o = Save(acc, 1000);
        Assert.Equal(2, V(o, WorkFamily.Constructions).IntegerValue);
        Assert.Equal(25, B(o, WorkFamily.ConstructionMaterials)["WoodLog"].IntegerValue);
        Assert.Equal((1L, 1L), (V(o, WorkFamily.ConstructionFailures).IntegerValue!.Value, V(o, WorkFamily.Deconstructions).IntegerValue!.Value));
        Assert.Equal(36, V(o, WorkFamily.MiningUnits).IntegerValue);
        Assert.Equal(35, V(o, WorkFamily.BestMiner).IntegerValue);
        Assert.Equal(2, V(o, WorkFamily.BestBuilder).IntegerValue);
        Assert.Equal((1L, 3L), (V(o, WorkFamily.Repairs).IntegerValue!.Value, V(o, WorkFamily.PodsLaunched_).IntegerValue!.Value));
    }

    // ----- food -----

    [Fact]
    public void Food_EatenNutritionAndSpoilage()
    {
        var (acc, s) = NewGame(FoodFamily.Family);
        FoodFamily.Ate(s, 0.2, freeColonist: true);
        FoodFamily.Ate(s, 0.2, freeColonist: true);
        FoodFamily.Ate(s, 0.2, freeColonist: true);
        FoodFamily.Ate(s, 0.9, freeColonist: false);   // an animal or a guest
        FoodFamily.Spoiled(s, 2);
        var o = Save(acc, 1000);
        Assert.Equal(0.6, Math.Round(V(o, FoodFamily.Nutrition).RealValue!.Value, 9));
        Assert.Equal(2, V(o, FoodFamily.SpoiledUnits).IntegerValue);
    }

    // ----- trade -----

    private static TradeLine Sell(string def, int count, double total) => new() { Def = def, Count = count, Total = total, PlayerSells = true };
    private static TradeLine Buy(string def, int count, double total) => new() { Def = def, Count = count, Total = total, PlayerSells = false };

    [Fact]
    public void Trade_OneDealWithBothSides()
    {
        var (acc, s) = NewGame(TradeFamily.Family);
        TradeFamily.Traded(s, TradeFamily.Orbital, "TradersGuild", new[] { Sell("Leather_Plain", 10, 30), Buy("MedicineIndustrial", 2, 40) });
        TradeFamily.Traded(s, TradeFamily.VisitingCaravan, "OutlanderCivil", Array.Empty<TradeLine>());   // an empty deal
        TradeFamily.Traded(s, TradeFamily.Settlement, null, new[] { Sell("Leather_Plain", 1, 5) });
        var o = Save(acc, 1000);
        Assert.Equal(2, V(o, TradeFamily.Deals).IntegerValue);
        Assert.Equal((35.0, 40.0), (V(o, TradeFamily.SalesTotal).RealValue!.Value, V(o, TradeFamily.BuysTotal).RealValue!.Value));
        Assert.Equal((30.0, 40.0), (V(o, TradeFamily.SalesPeak).RealValue!.Value, V(o, TradeFamily.BuysPeak).RealValue!.Value));
        Assert.Equal(5.0, V(o, TradeFamily.SalesPeakUnit).RealValue);
        Assert.Equal((11L, 2L), (V(o, TradeFamily.SalesUnits).IntegerValue!.Value, V(o, TradeFamily.BuysUnits).IntegerValue!.Value));
        Assert.Equal((1L, 0L), (V(o, TradeFamily.OrbitalDeals).IntegerValue!.Value, V(o, TradeFamily.VisitingCaravanDeals).IntegerValue!.Value));
        Assert.Equal(new[] { "Leather_Plain" }, B(o, TradeFamily.TopSold).Keys);
        Assert.Equal(new[] { "MedicineIndustrial" }, B(o, TradeFamily.TopBought).Keys);
        Assert.Equal((1L, 1L, 1L), (V(o, TradeFamily.SoldDefs).IntegerValue!.Value, V(o, TradeFamily.BoughtDefs).IntegerValue!.Value, V(o, TradeFamily.PartnerFactionDefs).IntegerValue!.Value));
        Assert.Equal(1, B(o, TradeFamily.PartnerFactionDefs)[MetricKeys.UnknownBucket].IntegerValue);   // the faction-less deal, shown apart
    }

    // ----- diplomacy -----

    [Fact]
    public void Diplomacy_GiftsAndRelationTransitions()
    {
        var (acc, s) = NewGame(DiplomacyFamily.Family);
        DiplomacyFamily.Gift(s, before: 10, after: 12);
        DiplomacyFamily.Gift(s, before: 100, after: 100);   // at the limit: no real increase
        DiplomacyFamily.RelationChanged(s, "Faction_3", "Hostile", "Neutral", 100);
        DiplomacyFamily.RelationChanged(s, "Faction_3", "Hostile", "Neutral", 100);   // the other side's notification
        DiplomacyFamily.RelationChanged(s, "Faction_4", "Ally", "Hostile", 200);
        DiplomacyFamily.RelationChanged(s, "Faction_5", "Neutral", "Ally", 300);
        DiplomacyFamily.AidRequested(s);
        var o = Save(acc, 1000);
        Assert.Equal(2.0, V(o, DiplomacyFamily.GiftGoodwill).RealValue);
        Assert.Equal((1L, 1L), (V(o, DiplomacyFamily.Reconciled).IntegerValue!.Value, V(o, DiplomacyFamily.AllyToHostile).IntegerValue!.Value));
        Assert.Equal((1L, 0L), (V(o, DiplomacyFamily.AidRequests).IntegerValue!.Value, V(o, DiplomacyFamily.AidArrivals).IntegerValue!.Value));
    }

    [Fact]
    public void Catalog_HasEveryC3Output()
    {
        var keys = new[]
        {
            WorkFamily.HarvestUnits, WorkFamily.MiningUnits, WorkFamily.CraftedUnits, WorkFamily.Constructions, WorkFamily.BestQuality,
            WorkFamily.QualityBreakdown, WorkFamily.ConstructionMaterials, WorkFamily.Deconstructions, WorkFamily.ConstructionFailures,
            WorkFamily.Legendary, WorkFamily.Masterwork, WorkFamily.BestPrice, WorkFamily.Sculptures, WorkFamily.Sown_, WorkFamily.HarvestFailures,
            WorkFamily.MealsByDef, WorkFamily.UniqueCraftedDefs, WorkFamily.ColdLoss, WorkFamily.PodsLaunched_, WorkFamily.DailyPeak,
            WorkFamily.SownDefs, WorkFamily.HarvestByCrop, WorkFamily.TopCrop, WorkFamily.OtherLoss, WorkFamily.LossRate, WorkFamily.HarvestYearPeak,
            WorkFamily.BestCrafter, WorkFamily.BestHarvester, WorkFamily.BestBuilder, WorkFamily.BestCook, WorkFamily.BestMiner, WorkFamily.Repairs,
            FoodFamily.SpoiledUnits, FoodFamily.Nutrition,
            TradeFamily.SalesTotal, TradeFamily.BuysTotal, TradeFamily.Deals, TradeFamily.SalesPeak, TradeFamily.BuysPeak, TradeFamily.TopSold,
            TradeFamily.OrbitalDeals, TradeFamily.VisitingCaravanDeals, TradeFamily.SalesPeakUnit, TradeFamily.PartnerFactionDefs, TradeFamily.SoldDefs,
            TradeFamily.BoughtDefs, TradeFamily.SalesUnits, TradeFamily.BuysUnits, TradeFamily.TopBought,
            DiplomacyFamily.GiftGoodwill, DiplomacyFamily.AllyToHostile, DiplomacyFamily.Reconciled, DiplomacyFamily.AidRequests, DiplomacyFamily.AidArrivals,
        };
        foreach (var key in keys)
            Assert.True(MetricRegistry.Find(key) is { Source: MetricObservationSource.Checkpoint, Published: false }, key);
        Assert.Contains("N2-F-012", MetricRegistry.Find(WorkFamily.MealsByDef)!.LedgerIds);
        // Held: no safe low-cost source (continuous stock, growing-area integral).
        Assert.Null(MetricRegistry.Find("food.no_shortage.longest"));
        Assert.Null(MetricRegistry.Find("food.harvest.per_growing_cell"));
    }
}
