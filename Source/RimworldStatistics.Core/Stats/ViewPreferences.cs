using System.Text.Json;

namespace RimworldStatistics.Core;

/// <summary>Presentation-only preferences: never stored in the statistics profile.</summary>
public sealed class ViewPreferences
{
    public const int MaxGroupCount = 64;
    public const int MaxNameLength = 64;
    public const int MaxColonyKeys = 10000;
    public const int MaxFavoriteCount = 500;

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
