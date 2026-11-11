using RimworldStatistics.HospitalityAddon;

namespace RimworldStatistics.Core.Tests;

/// <summary>Event identity must remain stable across save reloads and distinct across visitor groups.</summary>
public sealed class HospitalityVisitEventIdTests
{
    [Fact]
    public void ARealLordVisitGetsTheSameBoundedIdentityOnReload()
    {
        var id = HospitalityVisitEventId.Create(42, "Lord_123");
        Assert.Equal("hospitality.visit|42|Lord_123", id);
        Assert.Equal(id, HospitalityVisitEventId.Create(42, "Lord_123"));
        Assert.NotNull(HospitalityVisitEventId.Create(0, "Lord_1")); // Some game builds begin map IDs at zero.
        Assert.NotEqual(id, HospitalityVisitEventId.Create(43, "Lord_123"));
        Assert.NotEqual(id, HospitalityVisitEventId.Create(42, "Lord_124"));
    }

    [Theory]
    [InlineData(-1, "Lord_1")]
    [InlineData(1, "")]
    [InlineData(1, "  ")]
    [InlineData(1, "a|b")]
    [InlineData(1, "a\nb")]
    public void InvalidOrAmbiguousInputIsNeverRecorded(int mapId, string lordId)
        => Assert.Null(HospitalityVisitEventId.Create(mapId, lordId));

    [Fact]
    public void ExcessivelyLongIdentityIsRejectedNotTruncated()
        => Assert.Null(HospitalityVisitEventId.Create(9999, new string('A', 120)));
}
