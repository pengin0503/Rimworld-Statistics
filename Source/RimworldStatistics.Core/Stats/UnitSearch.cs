namespace RimworldStatistics.Core;

/// <summary>
/// The settings window's unit search (U1): a case-insensitive contains match over the name the user sees and the
/// technical unit ID, so one field finds a unit by either. An empty or whitespace-only query matches every unit.
/// </summary>
public static class UnitSearch
{
    public static bool Matches(string? label, string? id, string? query)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term))
            return true;
        return Contains(label, term) || Contains(id, term);
    }

    /// <summary>
    /// U46: one case-insensitive term matched against any name of an entry (shown label, English label, key,
    /// MOD id or MOD name). Empty queries match everything; null candidates never match.
    /// </summary>
    public static bool MatchesAny(string? query, params string?[]? candidates)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term))
            return true;
        if (candidates is null)
            return false;
        foreach (var candidate in candidates)
            if (Contains(candidate, term))
                return true;
        return false;
    }

    private static bool Contains(string? text, string term)
        => !string.IsNullOrEmpty(text) && text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
