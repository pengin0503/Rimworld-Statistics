namespace RimworldStatistics.Core.Tests;

public sealed class QualifiedBreakdownLabelTests
{
    [Theory]
    [InlineData("author.demo/GuestMeal", "author.demo", "GuestMeal")]
    [InlineData("author.demo/Project_Name", "author.demo", "Project_Name")]
    [InlineData("GuestMeal", null, null)]
    [InlineData("author.demo/", null, null)]
    [InlineData("/GuestMeal", null, null)]
    [InlineData("author.demo/category/GuestMeal", null, null)]
    public void ResolvesOnlyQualifiedKeys(string key, string? expectedOwner, string? expectedDef)
    {
        var parsed = OptionalTransitionRecording.TryParseQualifiedDefKey(key, out var owner, out var defName);
        Assert.Equal(expectedOwner is not null, parsed);
        Assert.Equal(expectedOwner, owner);
        Assert.Equal(expectedDef, defName);
    }

    [Fact]
    public void ProducerAndDisplayParserAgreeOnOwnerAndCaseSensitiveDefName()
    {
        var key = OptionalTransitionRecording.QualifiedDefKey("Author.Demo", "GuestMeal");
        Assert.True(OptionalTransitionRecording.TryParseQualifiedDefKey(key!, out var owner, out var defName));
        Assert.Equal("author.demo", owner);
        Assert.Equal("GuestMeal", defName);
    }
}
