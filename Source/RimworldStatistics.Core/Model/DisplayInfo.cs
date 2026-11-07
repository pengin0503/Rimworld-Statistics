namespace RimworldStatistics.Core;

/// <summary>Names shown in the UI only; never used for identification (spec §7.4).</summary>
public sealed class DisplayInfo
{
    public string? FactionName { get; set; }
    public string? WorldName { get; set; }
    public string? Scenario { get; set; }
    public string? Storyteller { get; set; }
    public string? Difficulty { get; set; }
    public bool Permadeath { get; set; }

    /// <summary>Fields of a newer minor version of the same schema, kept and written back unchanged.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }

    public DisplayInfo Clone() => (DisplayInfo)MemberwiseClone();
}
