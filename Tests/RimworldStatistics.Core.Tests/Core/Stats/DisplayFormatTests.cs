namespace RimworldStatistics.Core.Tests;

/// <summary>PR-6a U10: the pure display formatter shared by Details, Rankings and the window's day labels.</summary>
public sealed class DisplayFormatTests
{
    private static string Template(string decimalDays, string years, string days)
        => decimalDays + "|" + years + "|" + days;

    private static long Ticks(double days) => (long)Math.Round(days * StatFormat.TicksPerDay);

    [Fact]
    public void Days_DecimalFormat_UsesN1WithOptionalGrouping()
    {
        var ticks = Ticks(12345.6);
        Assert.Equal("12,345.6", DisplayFormat.Days(ticks, DisplayDayFormat.DecimalDays, grouping: true, Template));
        Assert.Equal("12345.6", DisplayFormat.Days(ticks, DisplayDayFormat.DecimalDays, grouping: false, Template));
    }

    [Fact]
    public void Days_YearsAndDaysFormat_SplitsTotalDays()
    {
        Assert.Equal("123.4|2|3",
            DisplayFormat.Days(Ticks(123.4), DisplayDayFormat.YearsAndDays, grouping: true, Template));
        Assert.Equal("60.0|1|0",
            DisplayFormat.Days(Ticks(60), DisplayDayFormat.YearsAndDays, grouping: true, Template));
        Assert.Equal("0.0|0|0",
            DisplayFormat.Days(0, DisplayDayFormat.YearsAndDays, grouping: true, Template));
    }

    [Fact]
    public void DayCount_MatchesTheTickBasedSplit()
    {
        Assert.Equal("123.4|2|3", DisplayFormat.DayCount(123.4, DisplayDayFormat.YearsAndDays, grouping: true, Template));
        Assert.Equal("123.4", DisplayFormat.DayCount(123.4, DisplayDayFormat.DecimalDays, grouping: true, Template));
        Assert.Equal("12345.6", DisplayFormat.DayCount(12345.6, DisplayDayFormat.DecimalDays, grouping: false, Template));
    }

    [Fact]
    public void DayCount_NonFiniteAndHugeValues_NeverThrow()
    {
        Assert.Equal("NaN|0|0", DisplayFormat.DayCount(double.NaN, DisplayDayFormat.YearsAndDays, true, Template));
        Assert.Contains("|0|", DisplayFormat.DayCount(double.PositiveInfinity, DisplayDayFormat.YearsAndDays, true, Template));
        Assert.Contains("|", DisplayFormat.DayCount(1e300, DisplayDayFormat.YearsAndDays, true, Template));
    }

    [Theory]
    [InlineData(1234567.891, 1, true, "1,234,567.9")]
    [InlineData(1234567.891, 1, false, "1234567.9")]
    [InlineData(1234567.891, 2, true, "1,234,567.89")]
    [InlineData(1234567.891, 0, false, "1234568")]
    public void Number_GroupingTogglesTheSeparators(double value, int decimals, bool grouping, string expected)
        => Assert.Equal(expected, DisplayFormat.Number(value, decimals, grouping));

    [Fact]
    public void Number_ClampsFractionDigits()
    {
        Assert.Equal("2", DisplayFormat.Number(1.5, -1, false));
        Assert.Equal("1.500000", DisplayFormat.Number(1.5, 20, false));
    }

    [Theory]
    [InlineData(2.5, true, "3")]
    [InlineData(2.5, false, "3")]
    [InlineData(1234.6, true, "1,235")]
    [InlineData(1234.6, false, "1235")]
    [InlineData(-2.5, true, "-3")]
    public void WholeNumber_RoundsAwayFromZeroAndHonoursGrouping(double value, bool grouping, string expected)
        => Assert.Equal(expected, DisplayFormat.WholeNumber(value, grouping));

    [Fact]
    public void WholeNumber_GroupingOn_MatchesStatFormatGroup()
        => Assert.Equal(StatFormat.Group(4364583.0615), DisplayFormat.WholeNumber(4364583.0615, grouping: true));
}
