using System.Globalization;
using System.Text.Json;

namespace RimworldStatistics.Core;

/// <summary>One element of the save's lineageEndings list, stored as a "type|ordinal|ticks" token.</summary>
public readonly record struct LineageEntry(EndingType Type, int Ordinal, int TicksGame)
{
    public string ToToken() => string.Create(CultureInfo.InvariantCulture, $"{TypeToken(Type)}|{Ordinal}|{TicksGame}");

    public static bool TryParse(string? token, out LineageEntry entry)
    {
        entry = default;
        if (token is null)
            return false;
        var parts = token.Split('|');
        if (parts.Length != 3)
            return false;
        if (!TryParseType(parts[0], out var type))
            return false;
        if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ordinal))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
            return false;
        entry = new LineageEntry(type, ordinal, ticks);
        return true;
    }

    internal static string TypeToken(EndingType type) => JsonNamingPolicy.CamelCase.ConvertName(type.ToString());

    private static bool TryParseType(string text, out EndingType type)
    {
        foreach (var t in Enum.GetValues<EndingType>())
        {
            if (string.Equals(TypeToken(t), text, StringComparison.Ordinal))
            {
                type = t;
                return true;
            }
        }
        type = default;
        return false;
    }
}
