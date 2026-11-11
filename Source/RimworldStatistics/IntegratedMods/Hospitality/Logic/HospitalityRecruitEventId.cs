using System.Globalization;

namespace RimworldStatistics.HospitalityAddon;

/// <summary>Stable target pawn and game tick; never persist a name or a raw pawn ID in the host.</summary>
internal static class HospitalityRecruitEventId
{
    internal static string? Create(string? pawnLoadId, int tick)
    {
        if (string.IsNullOrWhiteSpace(pawnLoadId) || pawnLoadId.Length > 96 || tick < 0 ||
            pawnLoadId != pawnLoadId.Trim())
            return null;
        var id = "hosp.recruit|" + pawnLoadId + "|" + tick.ToString(CultureInfo.InvariantCulture);
        return id.Length <= 128 ? id : null;
    }
}
