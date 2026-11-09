using RimworldStatistics.SmartApparelAddon;

namespace RimworldStatistics.Core.Tests;

/// <summary>Independent addon event contract without RimWorld or other MOD assemblies.</summary>
public sealed class SauAddonContractTests
{
    [Fact]
    public void IdempotentIdPerIssuedOrderAndBranch()
    {
        var id = SauOrderEventId.Create("Human_123", "Apparel_Parka_71", 900000);
        Assert.Equal("sau.order|900000|Human_123|Apparel_Parka_71", id);
        Assert.Equal(id, SauOrderEventId.Create("Human_123", "Apparel_Parka_71", 900000));
        Assert.NotEqual(id, SauOrderEventId.Create("Human_123", "Apparel_Parka_71", 900001));
        Assert.NotEqual(id, SauOrderEventId.Create("Human_124", "Apparel_Parka_71", 900000));
        Assert.NotEqual(id, SauOrderEventId.Create("Human_123", "Apparel_Parka_72", 900000));
    }

    [Fact]
    public void InvalidOrAmbiguousIdsAreNotRecordable()
    {
        Assert.Null(SauOrderEventId.Create(null, "X", 123));
        Assert.Null(SauOrderEventId.Create("X", null, 123));
        Assert.Null(SauOrderEventId.Create("", "X", 123));
        Assert.Null(SauOrderEventId.Create("X", "  ", 123));
        Assert.Null(SauOrderEventId.Create("X", "Y", -1));
        Assert.Null(SauOrderEventId.Create("X|Y", "A", 3));
        Assert.Null(SauOrderEventId.Create("X\nY", "A", 3));
        Assert.Null(SauOrderEventId.Create("X", "Y/Z", 3));
        Assert.Null(SauOrderEventId.Create(new string('a', 65), "Y", 3));
        Assert.Null(SauOrderEventId.Create(new string('a', 64), new string('b', 64), int.MaxValue));
    }
}
