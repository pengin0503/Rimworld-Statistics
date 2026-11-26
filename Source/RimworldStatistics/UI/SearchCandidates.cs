using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Search candidates for keyed UI text (U46). A search hits when any candidate matches: the label shown in the active
/// language, the label in the default (English) language, the key itself, and any caller-supplied aliases (DefName,
/// MOD package id or resolved MOD name). The default-language lookup follows DetailsTab's existing pattern; callers
/// build the array when a query changes over a filtered set, never once per rendered row.
/// </summary>
internal static class SearchCandidates
{
    /// <summary>Localized label, English label, translation key and extra aliases, in that order.</summary>
    internal static string?[] For(string? translationKey, string? localizedLabel = null, params string?[]? aliases)
    {
        var extra = aliases ?? [];
        var candidates = new string?[3 + extra.Length];
        candidates[0] = localizedLabel;
        candidates[1] = EnglishText(translationKey);
        candidates[2] = translationKey;
        Array.Copy(extra, 0, candidates, 3, extra.Length);
        return candidates;
    }

    /// <summary>
    /// All names of a metric: shown label, English label, translation and metric keys, ledger ids, MOD source label,
    /// package id and persisted provider name. Callers may add aliases specific to their row.
    /// </summary>
    internal static string?[] ForMetric(MetricDefinition? definition, string? localizedLabel, params string?[]? aliases)
    {
        // Cross-colony rows carry a synthetic label key; their source's label is the English surface.
        var source = definition is not null && CrossColonyCatalog.IsDerived(definition)
            && MetricRegistry.Find(definition.StoredKey) is { } parent ? parent : definition;
        var english = source is null ? null : source.ProviderId is not null
            ? ProviderMetricText.Label(source, EnglishText)
            : EnglishText(source.LabelKey);

        var extra = new List<string?>
        {
            definition?.Key,
            definition?.StoredKey,
            definition?.LedgerIds,
            definition?.OwnerPackageId,
            definition?.OwnerDisplayName,
            definition is null ? null : MetricText.ModSourceLabel(definition),
        };
        if (aliases is { Length: > 0 })
            extra.AddRange(aliases);
        return For(source?.LabelKey, localizedLabel, extra.ToArray());
    }

    /// <summary>The key's text in the default (English) language, or null when it has no distinct translation.</summary>
    internal static string? EnglishText(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;
        if (LanguageDatabase.defaultLanguage is { } fallback
            && fallback.TryGetTextFromKey(key, out var text)
            && !string.Equals(text, key, StringComparison.Ordinal))
            return (string)text;
        return null;
    }
}
