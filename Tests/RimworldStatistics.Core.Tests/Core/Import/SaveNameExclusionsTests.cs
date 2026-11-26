namespace RimworldStatistics.Core.Tests;

/// <summary>S20: user patterns keep save files out of an import.</summary>
public sealed class SaveNameExclusionsTests
{
    [Theory]
    [InlineData("test.rws", "test.rws", true)]              // exact
    [InlineData("TEST.RWS", "test.rws", true)]              // exact, case-insensitive
    [InlineData("test.rws.old", "test.rws", false)]
    [InlineData("test1.rws", "test*", true)]                // prefix
    [InlineData("mytest.rws", "test*", false)]
    [InlineData("My backup.rws", "*backup.rws", true)]      // suffix
    [InlineData("backup.rwsx", "*backup.rws", false)]
    [InlineData("colony-tmp-1.rws", "*tmp*", true)]         // contains
    [InlineData("colony.rws", "*tmp*", false)]
    [InlineData("test.rws", " test.rws ", true)]            // trimmed
    [InlineData("test.rws", "", false)]                     // empty ignores
    [InlineData("test.rws", "   ", false)]
    [InlineData("test.rws", "*", false)]                    // invalid: only a star
    [InlineData("test.rws", "**", false)]                   // invalid: empty contains
    [InlineData("test.rws", "te*st", false)]                // invalid: star in the middle
    [InlineData("test.rws", "*a*b", false)]                 // invalid: two stars in the contains part
    public void IsExcluded_MatchesOnlySupportedForms(string fileName, string pattern, bool expected)
        => Assert.Equal(expected, SaveNameExclusions.IsExcluded(fileName, new[] { pattern }));

    [Fact]
    public void IsExcluded_NullOrEmptyPatternList_MatchesNothing()
    {
        Assert.False(SaveNameExclusions.IsExcluded("a.rws", null));
        Assert.False(SaveNameExclusions.IsExcluded("a.rws", Array.Empty<string>()));
        Assert.False(SaveNameExclusions.IsExcluded("", new[] { "*" }));
    }

    [Fact]
    public void IsExcluded_AnyMatchingPatternExcludes()
    {
        Assert.True(SaveNameExclusions.IsExcluded("backup-2026.rws", new[] { "test*", "*2026.rws", "*tmp*" }));
    }

    [Fact]
    public void Normalize_TrimsDropsEmptyAndDeduplicatesCaseInsensitively()
    {
        var normalized = SaveNameExclusions.Normalize(new[] { " a.rws ", "", "A.RWS", "b*", "   ", "b*" });
        Assert.Equal(new[] { "a.rws", "b*" }, normalized);
    }

    [Fact]
    public void Normalize_Null_IsEmpty()
        => Assert.Empty(SaveNameExclusions.Normalize(null));
}
