namespace RimworldStatistics.Core.Tests;

public sealed class RateUnitsTests
{
    [Fact]
    public void PerYearUsesSixtyGameDays()
    {
        Assert.Equal(0d, RateUnits.PerYear(0d));
        Assert.Equal(60d, RateUnits.PerYear(1d));
        Assert.Equal(7.5d, RateUnits.PerYear(0.125d));
        Assert.Equal(-120d, RateUnits.PerYear(-2d));
    }

    [Fact]
    public void PerRealHourUsesThreePointSixGameDaysAtOneX()
    {
        Assert.Equal(0d, RateUnits.PerRealHour(0d));
        Assert.Equal(3.6d, RateUnits.PerRealHour(1d));
        Assert.Equal(36d, RateUnits.PerRealHour(10d));
        Assert.Equal(-3.6d, RateUnits.PerRealHour(-1d));
    }

    [Fact]
    public void PerDayIsTheIdentityConversionAndConvertDispatches()
    {
        const double perDay = 1.25d;
        Assert.Equal(perDay, RateUnits.Convert(perDay, RateUnit.PerDay));
        Assert.Equal(RateUnits.PerYear(perDay), RateUnits.Convert(perDay, RateUnit.PerYear));
        Assert.Equal(RateUnits.PerRealHour(perDay), RateUnits.Convert(perDay, RateUnit.PerRealHour));
    }
}
