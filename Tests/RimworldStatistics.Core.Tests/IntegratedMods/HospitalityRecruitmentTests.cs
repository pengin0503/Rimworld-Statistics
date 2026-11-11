using RimworldStatistics.HospitalityAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class HospitalityRecruitmentTests
{
    [Fact]
    public void ForcedAndVoluntaryRecruitmentHaveStableDistinctIds()
    {
        Assert.Equal("hosp.recruit|pawn-93|1800", HospitalityRecruitEventId.Create("pawn-93", 1800));
        Assert.Equal(HospitalityRecruitEventId.Create("pawn-93", 1800), HospitalityRecruitEventId.Create("pawn-93", 1800));
        Assert.NotEqual(HospitalityRecruitEventId.Create("pawn-93", 1800), HospitalityRecruitEventId.Create("pawn-93", 1801));
    }

    [Theory]
    [InlineData(null, 12)]
    [InlineData("", 12)]
    [InlineData("pawn-A", -1)]
    public void MissingIdentityOrInvalidGameTickCannotBecomeConfirmedEvent(string? key, int tick)
        => Assert.Null(HospitalityRecruitEventId.Create(key, tick));
}