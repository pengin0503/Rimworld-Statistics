using System.Text.Json;

namespace RimworldStatistics.Core;

/// <summary>Presentation-only preferences: never stored in the statistics profile.</summary>
public sealed class ViewPreferences
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public int Version { get; set; } = 1;
    public List<ColonyGroup> Groups { get; set; } = new();
    public List<string> FavoriteMetricKeys { get; set; } = new();

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
                .Take(64).Select(g => new ColonyGroup
                {
                    Id = g.Id,
                    Name = string.IsNullOrWhiteSpace(g.Name) ? g.Id : g.Name.Trim()[..Math.Min(64, g.Name.Trim().Length)],
                    ColonyKeys = (g.ColonyKeys ?? new List<string>())
                        .Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).Take(10000).ToList(),
                }).ToList();
            parsed.FavoriteMetricKeys = (parsed.FavoriteMetricKeys ?? new List<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).Take(500).ToList();
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
