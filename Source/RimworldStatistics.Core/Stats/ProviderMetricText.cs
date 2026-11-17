namespace RimworldStatistics.Core;

/// <summary>
/// The title and description of a provider's statistic (a definition with a <see cref="MetricDefinition.ProviderId"/>):
/// an external SDK statistic, or a built-in integration that uses the same API (#336). Such a statistic may have no
/// translation, or lose it when its MOD is removed, and its key (<c>mod.owner.family.name</c>) is never shown in place of
/// text: the title falls back to the last part of the key, and the description is left out.
/// </summary>
public static class ProviderMetricText
{
    /// <param name="tryTranslate">The translation of a key, or null when the language has none.</param>
    public static string Label(MetricDefinition definition, Func<string, string?> tryTranslate)
        => Translated(definition.LabelKey, definition.Key, tryTranslate) ?? KeyTail(definition.Key);

    /// <summary>The translated description, or null when there is none to show.</summary>
    public static string? Description(MetricDefinition definition, Func<string, string?> tryTranslate)
        => Translated(definition.DescriptionKey, definition.Key, tryTranslate);

    private static string? Translated(string key, string metricKey, Func<string, string?> tryTranslate)
    {
        if (string.IsNullOrWhiteSpace(key) || string.Equals(key, metricKey, StringComparison.Ordinal))
            return null;
        var text = tryTranslate(key)?.Trim();
        return string.IsNullOrEmpty(text) || string.Equals(text, key, StringComparison.Ordinal) ? null : text;
    }

    private static string KeyTail(string key) => key[(key.LastIndexOf('.') + 1)..].Replace('_', ' ').Replace('-', ' ');
}
