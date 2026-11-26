namespace RimworldStatistics.Core;

/// <summary>
/// The shared search syntax: whitespace-separated terms, where a term starting with '-' and longer than one
/// character excludes matches. All required terms must match, each against any one candidate. Pure and game-independent;
/// both the Details and Rankings selectors use it so a query behaves identically in both places.
/// </summary>
public static class SearchQuery
{
    /// <summary>A parsed query. Immutable, so a caller can parse once and reuse it for a whole row set.</summary>
    public readonly record struct Parsed(string[] Required, string[] Excluded)
    {
        public bool IsEmpty => Required.Length == 0 && Excluded.Length == 0;

        /// <summary>Empty queries match everything; null candidates never match any term.</summary>
        public bool Matches(params string?[]? candidates)
        {
            candidates ??= [];
            if (IsEmpty)
                return true;
            foreach (var term in Required)
            {
                var found = false;
                foreach (var candidate in candidates)
                {
                    if (candidate is not null && candidate.Contains(term, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        break;
                    }
                }
                if (!found)
                    return false;
            }
            foreach (var term in Excluded)
                foreach (var candidate in candidates)
                    if (candidate is not null && candidate.Contains(term, StringComparison.OrdinalIgnoreCase))
                        return false;
            return true;
        }
    }

    /// <summary>Splits on whitespace. A lone '-' remains a literal required term.</summary>
    public static Parsed Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new Parsed([], []);
        var required = new List<string>();
        var excluded = new List<string>();
        foreach (var token in raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Length > 1 && token[0] == '-')
                excluded.Add(token[1..]);
            else
                required.Add(token);
        }
        return new Parsed(required.ToArray(), excluded.ToArray());
    }

    /// <summary>Convenience overload for one-off checks; prefer <see cref="Parse"/> when filtering many rows.</summary>
    public static bool Matches(string? raw, params string?[]? candidates) => Parse(raw).Matches(candidates);
}
