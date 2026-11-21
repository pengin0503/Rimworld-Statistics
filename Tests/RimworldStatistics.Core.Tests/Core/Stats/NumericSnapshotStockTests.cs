namespace RimworldStatistics.Core.Tests;

/// <summary>Phase B stock: provenance, ThingID uniqueness, categorization and minima.</summary>
public sealed class NumericSnapshotStockTests
{
    [Fact]
    public void CountsAllEligibleItemsOnceThroughGroundAndInventories()
    {
        var items = new[]
        {
            new StockItemFact("rice-1", "RawRice", 120, 0.05, false, false, false, false),
            new StockItemFact("rice-1", "RawRice", 120, 0.05, false, false, false, false), // same ThingID
            new StockItemFact("silver", "Silver", 5000, 0, false, false, false, false),
            new StockItemFact("steel", "Steel", 200, 0, false, false, false, false),
            new StockItemFact("caravan-steel", "Steel", 500, 0, false, false, false, false, InPlayerHome: false),
            new StockItemFact("component", "ComponentIndustrial", 8, 0, false, false, false, false),
            new StockItemFact("medicine", "MedicineIndustrial", 3, 0, false, true, false, false),
            new StockItemFact("w1", "Gun_BoltActionRifle", 1, 0, true, false, true, false),
            new StockItemFact("w2", "MeleeWeapon_PersonaSword", 1, 0, true, false, false, true),
        };
        var r = StockSnapshot.Build(items);
        Assert.Equal(6d, r[StockSnapshot.FoodLatest].Values[""].RealValue);
        Assert.Equal(6d, r[StockSnapshot.FoodMin].Values[""].RealValue);
        Assert.Equal(5000, r[StockSnapshot.SilverLatest].Values[""].IntegerValue);
        Assert.Equal(208, r[StockSnapshot.MaterialsLatest].Values[""].IntegerValue);
        Assert.Equal(200, r[StockSnapshot.MaterialsLatest].Values["Steel"].IntegerValue);
        Assert.Equal(2, r[StockSnapshot.WeaponsPeak].Values[""].IntegerValue);
        Assert.Equal(2, r[StockSnapshot.WeaponDefsPeak].Values[""].IntegerValue);
        Assert.Equal(1, r[StockSnapshot.LegendaryWeaponsPeak].Values[""].IntegerValue);
        Assert.Equal(1, r[StockSnapshot.PersonaWeaponsPeak].Values[""].IntegerValue);
        Assert.Equal(3, r[StockSnapshot.MedicinePeak].Values[""].IntegerValue);
    }

    [Fact]
    public void UnknownOrMalformedItemsMarkPartialWithoutInventingStock()
    {
        var r = StockSnapshot.Build(new[]
        {
            new StockItemFact("", "Steel", 10, 0, false, false, false, false),
            new StockItemFact("valid", "Steel", 10, 0, false, false, false, false),
            new StockItemFact("bad", "RawRice", 10, double.NaN, false, false, false, false),
        });
        Assert.Equal(MetricCoverage.Partial, r[StockSnapshot.MaterialsLatest].Values[""].Coverage);
        Assert.Equal(10, r[StockSnapshot.MaterialsLatest].Values["Steel"].IntegerValue);
    }

    // RimWorld 1.7.5011 FoodTypeFlags (decompiled). OmnivoreHuman = OmnivoreAnimal | Fluid | AnimalProduct, the mask behind
    // IngestibleProperties.HumanEdible; it has no Plant or Tree bit.
    private const int VegetableOrFruit = 0x1, Meat = 0x2, Fluid = 0x4, Corpse = 0x8, Seed = 0x10, AnimalProduct = 0x20,
        Plant = 0x40, Tree = 0x80, Meal = 0x100, Processed = 0x200, Liquor = 0x400, Kibble = 0x800, Fungus = 0x1001;
    private const int VegetarianAnimal = Fungus | Seed | Meal | Processed | Liquor | Kibble;
    private const int OmnivoreHuman = VegetarianAnimal | Meat | Corpse | Fluid | AnimalProduct;

    // Food types, categories and base nutrition from the 1.7.4985 Def index (Core); meat is generated with foodType Meat.
    [Theory]
    [InlineData("Plant_Grass (growing)", false, Plant, 0.50f, true, false)]
    [InlineData("Plant_TreeOak (growing)", false, Tree, 2.0f, true, false)]
    [InlineData("Plant_Rice (growing crop)", false, Plant, 0.175f, true, false)]
    [InlineData("Hay", true, Plant, 0.05f, true, false)]
    [InlineData("PsychoidLeaves", true, Plant, 0.05f, true, false)]
    [InlineData("Kibble", true, Kibble, 0.05f, true, true)]
    [InlineData("Beer (drug)", true, Fluid | Processed | Liquor, 0.08f, true, true)]
    [InlineData("Ambrosia (drug)", true, VegetableOrFruit, 0.2f, true, true)]
    [InlineData("Luciferium (drug without nutrition)", true, Processed, 0f, true, false)]
    [InlineData("MealSimple", true, Meal, 0.9f, true, true)]
    [InlineData("Pemmican", true, Meal, 0.05f, true, true)]
    [InlineData("RawPotatoes", true, VegetableOrFruit, 0.05f, true, true)]
    [InlineData("RawRice", true, Seed, 0.05f, true, true)]
    [InlineData("RawFungus", true, Fungus, 0.05f, true, true)]
    [InlineData("Meat_Cow", true, Meat, 0.05f, true, true)]
    [InlineData("Milk", true, AnimalProduct | Fluid, 0.05f, true, true)]
    [InlineData("EggChickenUnfertilized", true, AnimalProduct, 0.25f, true, true)]
    [InlineData("Meat_Cow (rotting)", true, Meat, 0.05f, false, false)]
    public void FoodIsTheGamesFreshHumanEdibleItemNutrition(string kind, bool isItem, int foodType, float nutrition, bool fresh, bool counts)
    {
        _ = kind;
        Assert.Equal(0x1F3F, OmnivoreHuman);
        Assert.Equal(counts, StockFood.Counts(isItem, foodType, OmnivoreHuman, nutrition, fresh));
    }

    [Fact]
    public void FoodRevision2_NeverMinimizesOverPlantInflatedRevision1Readings()
    {
        // #317: revision 1 summed growing plants (a winter die-off made a false minimum); it is never mixed with revision 2.
        Assert.Equal(2, MetricRegistry.Find(StockSnapshot.FoodLatest)!.Revision);
        Assert.Equal(2, MetricRegistry.Find(StockSnapshot.FoodMin)!.Revision);
        var built = StockSnapshot.Build(new[] { new StockItemFact("meal", "MealSimple", 10, 0.9, false, false, false, false) });
        Assert.Equal((2, 2), (built[StockSnapshot.FoodLatest].Revision, built[StockSnapshot.FoodMin].Revision));

        var id = new ColonyIdentity
        {
            SeedString = "s", PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 7,
            StartingPawnIds = new List<string> { "Thing_Human1" }, ModCampaignId = "food",
        };
        var now = new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var p = new Profile();
        p.SaveObservations.Add(new SaveObservation
        {
            FileName = "f.rws", FileSize = 1, LastWriteUtc = now, ReaderVersion = SaveReader.Version,
            Status = ObservationStatus.Ok, Identity = id, Values = { TicksGame = 120000 },
        });
        MetricObservation Snapshot(string key, int tick, double food, int revision)
        {
            var o = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Key = key, Identity = id, SaveTick = tick, ObservedUtc = now.AddTicks(tick),
            };
            foreach (var k in new[] { StockSnapshot.FoodLatest, StockSnapshot.FoodMin })
                o.Metrics[k] = MetricEntry.Of(revision, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = food });
            return o;
        }
        p.MetricObservations.Add(Snapshot("old", 60000, 4870, revision: 1));
        p.MetricObservations.Add(Snapshot("winter", 90000, 3, revision: 1));
        p.MetricObservations.Add(Snapshot("new", 120000, 580, revision: 2));
        var colonies = CampaignMatcher.BuildColonies(p);
        var view = MetricCalculator.BuildView(p, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()), MetricRegistry.Catalog);
        var rows = view.Colonies.Single().Rows;
        Assert.Equal(580d, rows.Single(r => r.Definition.Key == StockSnapshot.FoodLatest).Value.Number);
        var min = rows.Single(r => r.Definition.Key == StockSnapshot.FoodMin).Value;
        Assert.Equal(580d, min.Number);
        // The older saves have no reading of this definition, so the minimum covers part of the history only.
        Assert.Equal(MetricCoverage.Partial, min.Coverage);
    }

    [Fact]
    public void MinimumFoodIsAnObservationMinAndPersonaIsDlcScoped()
    {
        Assert.Equal(ColonySelection.ObservationMin, MetricRegistry.Find(StockSnapshot.FoodMin)!.Selection);
        Assert.Equal(AcquisitionUnits.Royalty, MetricRegistry.Find(StockSnapshot.PersonaWeaponsPeak)!.RequiredDlc);
        Assert.False(MetricRegistry.Find(StockSnapshot.FoodLatest)!.Published);
    }
}
