namespace RimworldStatistics.Core.Tests;

public sealed class SearchQueryTests
{
    [Fact]
    public void AllRequiredTermsMustMatchAcrossDifferentCandidates()
    {
        string?[] candidates = ["Wealth peak", "colony.wealth.peak", "H-001", "Provider: Vanilla"];

        Assert.True(SearchQuery.Matches("wealth peak", candidates));
        Assert.True(SearchQuery.Matches("wealth H-001", candidates));
        Assert.True(SearchQuery.Matches("peak vanilla wealth", candidates));
        Assert.False(SearchQuery.Matches("wealth missing", candidates));
        Assert.False(SearchQuery.Matches("wealth peak H-999", candidates));
    }

    [Fact]
    public void ExcludedTermsRejectAnyCandidateThatContainsThem()
    {
        string?[] candidates = ["Raids per year", "colony.raids.year", "H-031"];

        Assert.True(SearchQuery.Matches("raids -mood", candidates));
        Assert.False(SearchQuery.Matches("raids -year", candidates));
        Assert.False(SearchQuery.Matches("raids -H-031", candidates));
        Assert.False(SearchQuery.Matches("-raids", candidates));
        Assert.False(SearchQuery.Matches("mood -raids", candidates));
    }

    [Fact]
    public void ALoneDashIsALiteralRequiredTerm()
    {
        var parsed = SearchQuery.Parse("-");
        Assert.Equal(["-"], parsed.Required);
        Assert.Empty(parsed.Excluded);
        Assert.True(parsed.Matches("dash - marker"));
        Assert.False(parsed.Matches("no marker here"));
        Assert.False(SearchQuery.Matches("-", "no marker here"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n")]
    public void EmptyQueryMatchesEverything(string? raw)
    {
        var parsed = SearchQuery.Parse(raw);
        Assert.True(parsed.IsEmpty);
        Assert.True(parsed.Matches());
        Assert.True(parsed.Matches(null, "anything"));
        Assert.True(SearchQuery.Matches(raw, (string?)null));
    }

    [Fact]
    public void MatchingIsCaseInsensitiveForTermsAndCandidates()
    {
        string?[] candidates = ["WEALTH Peak", "COLONY.Wealth.Peak"];

        Assert.True(SearchQuery.Matches("wealth PEAK", candidates));
        Assert.True(SearchQuery.Matches("COLONY -mood", candidates));
        Assert.False(SearchQuery.Matches("wealth -PEAK", candidates));
    }

    [Fact]
    public void NullCandidatesNeverSatisfyATerm()
    {
        string?[] candidates = [null, null, "Wealth"];

        Assert.True(SearchQuery.Matches("wealth", candidates!));
        Assert.False(SearchQuery.Matches("mood", candidates!));
        Assert.False(SearchQuery.Matches("mood", (string?[]?)null!));
    }

    [Fact]
    public void ParsedQueryCanBeReusedForManyRows()
    {
        var query = SearchQuery.Parse("  wealth   -mood  ");
        Assert.Equal(["wealth"], query.Required);
        Assert.Equal(["mood"], query.Excluded);
        Assert.True(query.Matches("Wealth peak", "H-001"));
        Assert.False(query.Matches("Wealth mood", "H-001"));
    }
}
