namespace RimworldStatistics.Core.Tests;

public sealed class NumericSnapshotFacilitiesTests
{
    [Fact]
    public void TotalsAreFromDistinctHomeMapsAndTemperatureIsAnExtreme()
    {
        var a = new FacilityMapFact
        {
            Id = "map-1", MedicalBeds = 2, Turrets = 3, GrowingCells = 60,
            IndoorRooms = 8, GenerationWatts = 2500, BatteryWattDays = 1200, OutdoorTempC = -45.2,
        };
        var b = new FacilityMapFact
        {
            Id = "map-2", MedicalBeds = 1, Turrets = 0, GrowingCells = 40, IndoorRooms = 3,
            GenerationWatts = 500, BatteryWattDays = 400, OutdoorTempC = 22.5,
        };
        var r = FacilitiesSnapshot.Build(new[] { a, a, b });
        Assert.Equal(2, r[FacilitiesSnapshot.HomeMapsPeak].Values[""].IntegerValue);
        Assert.Equal(3, r[FacilitiesSnapshot.MedicalBedsPeak].Values[""].IntegerValue);
        Assert.Equal(100, r[FacilitiesSnapshot.GrowingCellsPeak].Values[""].IntegerValue);
        Assert.Equal(3000d, r[FacilitiesSnapshot.GenerationPeak].Values[""].RealValue);
        Assert.Equal(1600d, r[FacilitiesSnapshot.BatteryPeak].Values[""].RealValue);
        Assert.Equal(-45.2d, r[FacilitiesSnapshot.OutdoorTempMin].Values[""].RealValue);
        Assert.Equal(22.5d, r[FacilitiesSnapshot.OutdoorTempMax].Values[""].RealValue);
        Assert.DoesNotContain("facilities.running_generators.latest", r.Keys);
        Assert.Null(MetricRegistry.Find("facilities.running_generators.latest"));
        Assert.Equal(MetricCoverage.Partial, r[FacilitiesSnapshot.HomeMapsPeak].Values[""].Coverage);
    }

    [Fact]
    public void ZeroHomeMapsIsKnownZeroButTemperatureIsNotApplicable()
    {
        var r = FacilitiesSnapshot.Build(Array.Empty<FacilityMapFact>());
        Assert.Equal(0, r[FacilitiesSnapshot.HomeMapsPeak].Values[""].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable, r[FacilitiesSnapshot.OutdoorTempMin].Values[""].Coverage);
    }

    [Fact]
    public void CatalogSelectsAcrossSnapshotsForPeakAndTemperature()
    {
        Assert.Equal(ColonySelection.ObservationMax, MetricRegistry.Find(FacilitiesSnapshot.GenerationPeak)!.Selection);
        Assert.Equal(ColonySelection.ObservationMin, MetricRegistry.Find(FacilitiesSnapshot.OutdoorTempMin)!.Selection);
        Assert.False(MetricRegistry.Find(FacilitiesSnapshot.HomeMapsPeak)!.Published);
    }
}
