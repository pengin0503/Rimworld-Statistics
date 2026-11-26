namespace RimworldStatistics.Core.Tests;

public sealed class UnitSearchTests
{
    [Theory]
    [InlineData("Research events", "research", "research", true)]
    [InlineData("Research events", "research", "RESEARCH", true)]
    [InlineData("Research events", "research", "search", true)]
    [InlineData("Research events", "research", "events", true)]
    [InlineData("Research events", "research", "zzz", false)]
    public void MatchesLabelAndIdCaseInsensitively(string label, string id, string query, bool expected)
        => Assert.Equal(expected, UnitSearch.Matches(label, id, query));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyQueryMatchesEverything(string? query)
        => Assert.True(UnitSearch.Matches("Research", "research", query));

    [Fact]
    public void QueryIsTrimmedAndNullOrEmptyTextIsSafe()
    {
        Assert.True(UnitSearch.Matches("Research", "research", "  research  "));
        Assert.False(UnitSearch.Matches(null, null, "research"));
        Assert.False(UnitSearch.Matches("", "   ", "research"));
    }

    [Fact]
    public void MatchesIdWhenLabelDiffers()
    {
        Assert.True(UnitSearch.Matches("イベント", "mod.example.events", "example"));
        Assert.False(UnitSearch.Matches("イベント", "mod.example.events", "スナップショット"));
    }

    [Theory]
    [InlineData("WEALTH", true)]
    [InlineData("RWS_Stat_PeakWealthSum", true)]
    [InlineData("mod.example", true)]
    [InlineData("Hospitality", true)]
    [InlineData("missing", false)]
    public void MatchesAnySearchesEveryNameOfAnEntry(string query, bool expected)
        => Assert.Equal(expected, UnitSearch.MatchesAny(query,
            "資産", "Wealth", "RWS_Stat_PeakWealthSum", "mod.example.events", "Hospitality"));

    [Fact]
    public void MatchesAnyHandlesEmptyQueriesAndNullCandidates()
    {
        Assert.True(UnitSearch.MatchesAny(null, (string?[]?)null));
        Assert.True(UnitSearch.MatchesAny("  ", "anything"));
        Assert.False(UnitSearch.MatchesAny("wealth", (string?[]?)null));
        Assert.False(UnitSearch.MatchesAny("wealth", null, "other"));
    }
}
