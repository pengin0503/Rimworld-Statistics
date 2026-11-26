using System.Text.Json;

namespace RimworldStatistics.Core;

/// <summary>Presentation-only preferences: never stored in the statistics profile.</summary>
public sealed class ViewPreferences
{
    public const int MaxGroupCount = 64;
    public const int MaxNameLength = 64;
    public const int MaxColonyKeys = 10000;
    public const int MaxFavoriteCount = 500;
    public const int MaxExpandedKeys = 200;
    public const int MaxViewStateTextLength = 512;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public int Version { get; set; } = 1;
    public List<ColonyGroup> Groups { get; set; } = new();
    public List<string> FavoriteMetricKeys { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    private HashSet<string>? _favoriteMetricKeySet;
    [System.Text.Json.Serialization.JsonIgnore]
    private List<string>? _favoriteMetricKeySetSource;
    [System.Text.Json.Serialization.JsonIgnore]
    private int _favoriteMetricKeySetCount = -1;

    public bool IsFavoriteMetric(string key)
    {
        var list = FavoriteMetricKeys;
        if (list is null || list.Count == 0)
            return false;
        if (_favoriteMetricKeySet is null
            || !ReferenceEquals(_favoriteMetricKeySetSource, list)
            || _favoriteMetricKeySetCount != list.Count)
        {
            var set = new HashSet<string>(list.Count, StringComparer.Ordinal);
            foreach (var item in list)
                set.Add(item);
            _favoriteMetricKeySet = set;
            _favoriteMetricKeySetSource = list;
            _favoriteMetricKeySetCount = list.Count;
        }
        return _favoriteMetricKeySet.Contains(key);
    }

    public bool ToggleFavoriteMetric(string key)
    {
        FavoriteMetricKeys ??= new List<string>();
        bool changed;
        if (FavoriteMetricKeys.Remove(key))
        {
            changed = true;
        }
        else if (FavoriteMetricKeys.Count < MaxFavoriteCount)
        {
            FavoriteMetricKeys.Add(key);
            changed = true;
        }
        else
        {
            changed = false;
        }
        if (changed)
            InvalidateFavoriteMetricCache();
        return changed;
    }

    public bool RemoveFavoriteMetric(string key)
    {
        if (FavoriteMetricKeys is null || !FavoriteMetricKeys.Remove(key))
            return false;
        InvalidateFavoriteMetricCache();
        return true;
    }

    public void InvalidateFavoriteMetricCache()
    {
        _favoriteMetricKeySet = null;
        _favoriteMetricKeySetSource = null;
        _favoriteMetricKeySetCount = -1;
    }
    public Dictionary<string, string> ColonyDisplayNames { get; set; } = new(StringComparer.Ordinal);
    public List<ColonyMetricFavorite> FavoriteColonyMetrics { get; set; } = new();

    // ----- presentation-only view state (PR-6a) -----
    // Enum choices are stored as validated ints: an unknown value falls back to the same default as a missing field.
    public int DetailsViewMode { get; set; } = (int)DetailsViewModePreference.All;
    public bool DetailsPerColony { get; set; }
    public string? DetailsColonyKey { get; set; }
    public int? DetailsCategory { get; set; }
    public string DetailsSearch { get; set; } = "";
    public bool DetailsByValue { get; set; }
    public bool DetailsAscending { get; set; } = true;
    public int DetailsStatusFilter { get; set; } = (int)DetailsStatusPreference.All;
    public bool DetailsGroupDerived { get; set; } = true;
    public int DetailsSourceFilter { get; set; } = (int)MetricSourceFilter.All;
    public bool DetailsHideZero { get; set; }
    public List<string> DetailsExpandedKeys { get; set; } = new();

    public string RankingsSearch { get; set; } = "";
    public int? RankingsCategory { get; set; }
    public string? RankingsSelectedKey { get; set; }
    public bool RankingsSelectorCollapsed { get; set; }
    public bool RankingsDescending { get; set; } = true;
    public bool RankingsShowAll { get; set; }
    public bool RankingsPerDayMode { get; set; }

    public int DayFormat { get; set; } = (int)DisplayDayFormat.DecimalDays;
    public bool NumberGrouping { get; set; } = true;

    private static string? NormalizeStateText(string? value)
    {
        if (value is null)
            return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(MaxViewStateTextLength, trimmed.Length)];
    }

    private static int ValidEnum<T>(int value, int fallback) where T : struct, Enum
        => Enum.IsDefined(typeof(T), value) ? value : fallback;

    private static int? ValidCategory(int? value)
        => value.HasValue && Enum.IsDefined(typeof(MetricCategory), value.Value) ? value : null;

    public static ViewPreferences Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new ViewPreferences();
        try
        {
            var parsed = JsonSerializer.Deserialize<ViewPreferences>(json, JsonOptions) ?? new ViewPreferences();
            if (parsed.Version > 1)
                return new ViewPreferences();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            parsed.Groups = (parsed.Groups ?? new List<ColonyGroup>())
                .Where(g => g is not null && !string.IsNullOrWhiteSpace(g.Id) && seen.Add(g.Id))
                .Take(MaxGroupCount).Select(g => new ColonyGroup
                {
                    Id = g.Id,
                    Name = string.IsNullOrWhiteSpace(g.Name) ? g.Id : g.Name.Trim()[..Math.Min(MaxNameLength, g.Name.Trim().Length)],
                    ColonyKeys = (g.ColonyKeys ?? new List<string>())
                        .Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).Take(MaxColonyKeys).ToList(),
                }).ToList();
            parsed.FavoriteMetricKeys = (parsed.FavoriteMetricKeys ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).Take(MaxFavoriteCount).ToList();

            var displayNames = new Dictionary<string, string>(StringComparer.Ordinal);
            if (parsed.ColonyDisplayNames is not null)
            {
                foreach (var (key, value) in parsed.ColonyDisplayNames)
                {
                    if (displayNames.Count >= MaxColonyKeys)
                        break;
                    if (string.IsNullOrWhiteSpace(key) || ColonyPresentation.NormalizeDisplayName(value) is not { } normalized)
                        continue;
                    displayNames.TryAdd(key, normalized);
                }
            }
            parsed.ColonyDisplayNames = displayNames;

            var seenPairs = new HashSet<(string ColonyKey, string MetricKey)>();
            parsed.FavoriteColonyMetrics = (parsed.FavoriteColonyMetrics ?? new List<ColonyMetricFavorite>())
                .Where(f => f is not null && !string.IsNullOrWhiteSpace(f.ColonyKey) && !string.IsNullOrWhiteSpace(f.MetricKey)
                    && seenPairs.Add((f.ColonyKey, f.MetricKey)))
                .Take(MaxFavoriteCount)
                .Select(f => new ColonyMetricFavorite { ColonyKey = f.ColonyKey, MetricKey = f.MetricKey })
                .ToList();

            // View-state fields are additive and optional: unknown enum values, oversized strings and
            // over-cap lists fall back to exactly the defaults of a fresh preferences object.
            var defaults = new ViewPreferences();
            parsed.DetailsViewMode = ValidEnum<DetailsViewModePreference>(parsed.DetailsViewMode, defaults.DetailsViewMode);
            parsed.DetailsColonyKey = NormalizeStateText(parsed.DetailsColonyKey);
            parsed.DetailsCategory = ValidCategory(parsed.DetailsCategory);
            parsed.DetailsSearch = NormalizeStateText(parsed.DetailsSearch) ?? "";
            parsed.DetailsStatusFilter = ValidEnum<DetailsStatusPreference>(parsed.DetailsStatusFilter, defaults.DetailsStatusFilter);
            parsed.DetailsSourceFilter = ValidEnum<MetricSourceFilter>(parsed.DetailsSourceFilter, defaults.DetailsSourceFilter);
            parsed.DetailsExpandedKeys = (parsed.DetailsExpandedKeys ?? new List<string>())
                .Select(NormalizeStateText)
                .Where(k => k is not null)
                .Distinct(StringComparer.Ordinal)
                .Take(MaxExpandedKeys)
                .Select(k => k!)
                .ToList();

            parsed.RankingsSearch = NormalizeStateText(parsed.RankingsSearch) ?? "";
            parsed.RankingsCategory = ValidCategory(parsed.RankingsCategory);
            parsed.RankingsSelectedKey = NormalizeStateText(parsed.RankingsSelectedKey);
            parsed.DayFormat = ValidEnum<DisplayDayFormat>(parsed.DayFormat, defaults.DayFormat);

            parsed.Version = 1;
            return parsed;
        }
        catch (JsonException)
        {
            return new ViewPreferences();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

/// <summary>Serialized values of DetailsTab's private view-mode enum; the order is part of the JSON format.</summary>
public enum DetailsViewModePreference
{
    Lifetime = 0,
    Other = 1,
    All = 2,
}

/// <summary>Serialized values of DetailsTab's private status filter; the order is part of the JSON format.</summary>
public enum DetailsStatusPreference
{
    All = 0,
    Measured = 1,
    Partial = 2,
    Missing = 3,
    Experimental = 4,
}

/// <summary>Where a metric comes from; MOD wins over DLC when both apply.</summary>
public enum MetricSourceFilter
{
    All = 0,
    Vanilla = 1,
    Dlc = 2,
    Mod = 3,
}

public sealed class ColonyGroup
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> ColonyKeys { get; set; } = new();
}

public sealed class ColonyMetricFavorite
{
    public string ColonyKey { get; set; } = "";
    public string MetricKey { get; set; } = "";
}
