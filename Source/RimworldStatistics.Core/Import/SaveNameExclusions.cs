namespace RimworldStatistics.Core;

/// <summary>
/// User patterns that keep save files out of an import (S20). A pattern is an exact file name (<c>test.rws</c>), a
/// prefix (<c>test*</c>), a suffix (<c>*backup.rws</c>) or a contains match (<c>*tmp*</c>). Matching is
/// <see cref="StringComparison.OrdinalIgnoreCase"/>; surrounding whitespace is ignored and an empty pattern matches
/// nothing. A pattern with a <c>*</c> anywhere other than the supported edge positions is invalid and never matches.
/// </summary>
public static class SaveNameExclusions
{
    /// <summary>Whether <paramref name="fileName"/> matches any of <paramref name="patterns"/>.</summary>
    public static bool IsExcluded(string fileName, IReadOnlyList<string>? patterns)
    {
        if (patterns is null || patterns.Count == 0 || string.IsNullOrEmpty(fileName))
            return false;
        for (var i = 0; i < patterns.Count; i++)
        {
            var pattern = patterns[i]?.Trim();
            if (!string.IsNullOrEmpty(pattern) && Matches(fileName, pattern))
                return true;
        }
        return false;
    }

    /// <summary>Trims every pattern, drops empty ones and removes case-insensitive duplicates, keeping the first form.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? patterns)
    {
        if (patterns is null)
            return Array.Empty<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = new List<string>();
        foreach (var raw in patterns)
        {
            var pattern = raw?.Trim();
            if (string.IsNullOrEmpty(pattern) || !seen.Add(pattern))
                continue;
            normalized.Add(pattern);
        }
        return normalized;
    }

    private static bool Matches(string fileName, string pattern)
    {
        var startsWithStar = pattern[0] == '*';
        var endsWithStar = pattern.Length > 1 && pattern[^1] == '*';
        if (startsWithStar && endsWithStar)
        {
            // Contains, for example *tmp*.
            if (pattern.Length <= 2)
                return false;
            var inner = pattern[1..^1];
            if (inner.Contains('*'))
                return false;
            return fileName.Contains(inner, StringComparison.OrdinalIgnoreCase);
        }
        if (startsWithStar)
        {
            // Suffix, for example *backup.rws.
            var suffix = pattern[1..];
            if (suffix.Length == 0 || suffix.Contains('*'))
                return false;
            return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }
        if (endsWithStar)
        {
            // Prefix, for example test*.
            var prefix = pattern[..^1];
            if (prefix.Length == 0 || prefix.Contains('*'))
                return false;
            return fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        // Exact file name. A star in the middle is not one of the supported forms, so it matches nothing.
        if (pattern.Contains('*'))
            return false;
        return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
