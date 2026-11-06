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

    public DisplayInfo Clone() => (DisplayInfo)MemberwiseClone();
}
