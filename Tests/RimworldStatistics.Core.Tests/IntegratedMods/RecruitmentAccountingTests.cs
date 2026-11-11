using RimworldStatistics.RecruitmentAddon;

namespace RimworldStatistics.Core.Tests;

public sealed class RecruitmentAccountingTests
{
    [Fact]
    public void PaidSilverAndRolledApplicantsHaveSameStableDriveIdentity()
    {
        var eventInfo = RecruitmentDriveAccounting.Create(7, 2500, 3);
        Assert.NotNull(eventInfo);
        Assert.Equal("recruitment.drive|7", eventInfo.Value.Id);
        Assert.Equal(2500, eventInfo.Value.PaidSilver);
        Assert.Equal(3, eventInfo.Value.RolledApplicants);
    }

    [Theory]
    [InlineData(0, 2, 1)]
    [InlineData(1, -1, 1)]
    [InlineData(1, 5, -1)]
    [InlineData(1, 5, 1000001)]
    public void RejectsBadOrUnreasonableDrivePayload(int id, int silver, int applicants)
        => Assert.Null(RecruitmentDriveAccounting.Create(id, silver, applicants));

    [Fact]
    public void PaidNothingAndRolledNoApplicantsStillRepresentOneConfirmedRequest()
    {
        var e = RecruitmentDriveAccounting.Create(1, 0, 0);
        Assert.Equal(0, e!.Value.PaidSilver);
        Assert.Equal(0, e.Value.RolledApplicants);
    }
}