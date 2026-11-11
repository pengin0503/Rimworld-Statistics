using System.Globalization;

namespace RimworldStatistics.RecruitmentAddon;

/// <summary>One confirmed paid request, not an arrival or a colonist joined.</summary>
internal static class RecruitmentDriveAccounting
{
    internal static (string Id, int PaidSilver, int RolledApplicants)? Create(
        int id, int paidSilver, int rolledApplicants)
    {
        if (id <= 0 || paidSilver < 0 || paidSilver > 1_000_000_000 ||
            rolledApplicants < 0 || rolledApplicants > 1_000_000)
            return null;
        return ("recruitment.drive|" + id.ToString(CultureInfo.InvariantCulture), paidSilver, rolledApplicants);
    }
}
