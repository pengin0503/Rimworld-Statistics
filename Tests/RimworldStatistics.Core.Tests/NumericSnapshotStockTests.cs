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

    [Fact]
    public void MinimumFoodIsAnObservationMinAndPersonaIsDlcScoped()
    {
        Assert.Equal(ColonySelection.ObservationMin, MetricRegistry.Find(StockSnapshot.FoodMin)!.Selection);
        Assert.Equal(AcquisitionUnits.Royalty, MetricRegistry.Find(StockSnapshot.PersonaWeaponsPeak)!.RequiredDlc);
        Assert.False(MetricRegistry.Find(StockSnapshot.FoodLatest)!.Published);
    }
}
