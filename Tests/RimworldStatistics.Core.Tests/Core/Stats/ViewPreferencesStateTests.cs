namespace RimworldStatistics.Core.Tests;

/// <summary>PR-6a: additive presentation-only view state must round-trip and normalize like the older preference fields.</summary>
public sealed class ViewPreferencesStateTests
{
    [Fact]
    public void FreshPreferences_KeepTheExistingDefaults()
    {
        var prefs = ViewPreferences.Parse("");
        Assert.Equal((int)DetailsViewModePreference.All, prefs.DetailsViewMode);
        Assert.False(prefs.DetailsPerColony);
        Assert.Null(prefs.DetailsColonyKey);
        Assert.Null(prefs.DetailsCategory);
        Assert.Equal("", prefs.DetailsSearch);
        Assert.False(prefs.DetailsByValue);
        Assert.True(prefs.DetailsAscending);
        Assert.Equal((int)DetailsStatusPreference.All, prefs.DetailsStatusFilter);
        Assert.True(prefs.DetailsGroupDerived);
        Assert.Equal((int)MetricSourceFilter.All, prefs.DetailsSourceFilter);
        Assert.False(prefs.DetailsHideZero);
        Assert.Empty(prefs.DetailsExpandedKeys);
        Assert.Equal("", prefs.RankingsSearch);
        Assert.Null(prefs.RankingsCategory);
        Assert.Null(prefs.RankingsSelectedKey);
        Assert.False(prefs.RankingsSelectorCollapsed);
        Assert.True(prefs.RankingsDescending);
        Assert.False(prefs.RankingsShowAll);
        Assert.False(prefs.RankingsPerDayMode);
        Assert.Equal((int)RateUnit.PerDay, prefs.RankingsRateUnit);
        Assert.Equal((int)DisplayDayFormat.DecimalDays, prefs.DayFormat);
        Assert.True(prefs.NumberGrouping);
    }

    [Fact]
    public void AllNewFields_RoundTripThroughJson()
    {
        var json = """
            {"version":1,"detailsViewMode":1,"detailsPerColony":true,"detailsColonyKey":" colony-1 ",
            "detailsCategory":7,"detailsSearch":" raid ","detailsByValue":true,"detailsAscending":false,
            "detailsStatusFilter":3,"detailsGroupDerived":false,"detailsSourceFilter":2,"detailsHideZero":true,
            "detailsExpandedKeys":["a:key","c:key"],"rankingsSearch":"wealth","rankingsCategory":5,
            "rankingsSelectedKey":"kills","rankingsSelectorCollapsed":true,"rankingsDescending":false,
            "rankingsShowAll":true,"rankingsPerDayMode":true,"rankingsRateUnit":2,"dayFormat":1,"numberGrouping":false}
            """;
        var prefs = ViewPreferences.Parse(json);
        Assert.Equal((int)DetailsViewModePreference.Other, prefs.DetailsViewMode);
        Assert.True(prefs.DetailsPerColony);
        Assert.Equal("colony-1", prefs.DetailsColonyKey);
        Assert.Equal((int)MetricCategory.Mind, prefs.DetailsCategory);
        Assert.Equal("raid", prefs.DetailsSearch);
        Assert.True(prefs.DetailsByValue);
        Assert.False(prefs.DetailsAscending);
        Assert.Equal((int)DetailsStatusPreference.Missing, prefs.DetailsStatusFilter);
        Assert.False(prefs.DetailsGroupDerived);
        Assert.Equal((int)MetricSourceFilter.Dlc, prefs.DetailsSourceFilter);
        Assert.True(prefs.DetailsHideZero);
        Assert.Equal(["a:key", "c:key"], prefs.DetailsExpandedKeys);
        Assert.Equal("wealth", prefs.RankingsSearch);
        Assert.Equal((int)MetricCategory.Rankings, prefs.RankingsCategory);
        Assert.Equal("kills", prefs.RankingsSelectedKey);
        Assert.True(prefs.RankingsSelectorCollapsed);
        Assert.False(prefs.RankingsDescending);
        Assert.True(prefs.RankingsShowAll);
        Assert.True(prefs.RankingsPerDayMode);
        Assert.Equal((int)RateUnit.PerRealHour, prefs.RankingsRateUnit);
        Assert.Equal((int)DisplayDayFormat.YearsAndDays, prefs.DayFormat);
        Assert.False(prefs.NumberGrouping);

        var reparsed = ViewPreferences.Parse(prefs.ToJson());
        Assert.Equal(prefs.ToJson(), reparsed.ToJson());
        Assert.Equal((int)MetricSourceFilter.Dlc, reparsed.DetailsSourceFilter);
        Assert.Equal((int)DisplayDayFormat.YearsAndDays, reparsed.DayFormat);
    }

    [Fact]
    public void UnknownEnums_OversizedListsAndStrings_FallBackToDefaults()
    {
        var keys = Enumerable.Range(0, ViewPreferences.MaxExpandedKeys + 40).Select(i => $"a:k{i}").ToList();
        keys.AddRange([" a:last ", " a:last ", "a:last", "   "]);
        var longText = new string('x', ViewPreferences.MaxViewStateTextLength + 100);
        var json = System.Text.Json.JsonSerializer.Serialize(new
        {
            version = 1,
            detailsViewMode = 99,
            detailsStatusFilter = -1,
            detailsSourceFilter = 1000,
            detailsCategory = 999,
            rankingsCategory = -7,
            rankingsRateUnit = 99,
            dayFormat = 42,
            detailsColonyKey = "  " + longText + "  ",
            rankingsSelectedKey = "  " + longText + "  ",
            detailsSearch = "  " + longText + "  ",
            rankingsSearch = "   ",
            detailsExpandedKeys = keys,
        });
        var prefs = ViewPreferences.Parse(json);
        Assert.Equal((int)DetailsViewModePreference.All, prefs.DetailsViewMode);
        Assert.Equal((int)DetailsStatusPreference.All, prefs.DetailsStatusFilter);
        Assert.Equal((int)MetricSourceFilter.All, prefs.DetailsSourceFilter);
        Assert.Equal((int)DisplayDayFormat.DecimalDays, prefs.DayFormat);
        Assert.Null(prefs.DetailsCategory);
        Assert.Null(prefs.RankingsCategory);
        Assert.Equal((int)RateUnit.PerDay, prefs.RankingsRateUnit);
        Assert.Equal(ViewPreferences.MaxViewStateTextLength, prefs.DetailsColonyKey!.Length);
        Assert.Equal(longText[..ViewPreferences.MaxViewStateTextLength], prefs.DetailsColonyKey);
        Assert.Equal(longText[..ViewPreferences.MaxViewStateTextLength], prefs.RankingsSelectedKey);
        Assert.Equal(longText[..ViewPreferences.MaxViewStateTextLength], prefs.DetailsSearch);
        Assert.Equal("", prefs.RankingsSearch);
        Assert.Equal(ViewPreferences.MaxExpandedKeys, prefs.DetailsExpandedKeys.Count);
        Assert.Equal(prefs.DetailsExpandedKeys.Count, prefs.DetailsExpandedKeys.Distinct(StringComparer.Ordinal).Count());
        Assert.DoesNotContain("   ", prefs.DetailsExpandedKeys);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"version\":2,\"detailsHideZero\":true}")]
    [InlineData("{\"detailsExpandedKeys\":[1,2,3]}")]
    [InlineData("{\"numberGrouping\":\"yes\"}")]
    public void MalformedOrNewerJson_NeverThrows(string json)
    {
        var prefs = ViewPreferences.Parse(json);
        Assert.Equal((int)DisplayDayFormat.DecimalDays, prefs.DayFormat);
        Assert.True(prefs.NumberGrouping);
        Assert.Empty(prefs.DetailsExpandedKeys);
    }
}
