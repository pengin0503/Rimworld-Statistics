using System.Globalization;

namespace RimworldStatistics.HospitalityAddon;

/// <summary>
/// Stable identity for one confirmed Hospitality visitor group. The host hashes
/// this before persisting; no pawn names or identifiers enter the profile.
/// </summary>
internal static class HospitalityVisitEventId
{
    internal static string? Create(int mapId, string? lordId)
    {
        if (mapId < 0 || string.IsNullOrWhiteSpace(lordId) || lordId.Length > 96 ||
            lordId.Any(ch => char.IsControl(ch) || ch == '|' || ch == '/' || ch == '\\'))
            return null;
        var id = "hospitality.visit|" + mapId.ToString(CultureInfo.InvariantCulture) + "|" + lordId;
        return id.Length <= 128 ? id : null;
    }
}
