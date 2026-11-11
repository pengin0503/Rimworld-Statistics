using System.Globalization;

namespace RimworldStatistics.SmartApparelAddon;

/// <summary>
/// One idempotent issued-job event. Pawn/thing technical IDs are passed only to
/// the SDK, which persists their SHA-256 digest rather than the raw ID.
/// This identifies an ORDER, not a completed equip operation.
/// </summary>
public static class SauOrderEventId
{
    public static string? Create(string? pawnId, string? apparelId, int tick)
    {
        if (tick < 0 || !Valid(pawnId) || !Valid(apparelId))
            return null;
        var id = "sau.order|" + tick.ToString(CultureInfo.InvariantCulture) + "|" + pawnId + "|" + apparelId;
        return id.Length <= 128 ? id : null;
    }

    private static bool Valid(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= 64
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-');
}
