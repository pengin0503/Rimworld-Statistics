namespace RimworldStatistics.Core;

/// <summary>What the mod recorded during play for one campaign id (record ② in spec §6.2).</summary>
public sealed class LiveCampaign
{
    public string CampaignId { get; set; } = "";
    public ColonyIdentity Identity { get; set; } = new();
    public ObservedValues Max { get; set; } = new();
    public ObservedValues Latest { get; set; } = new();
    public DisplayInfo Display { get; set; } = new();
    public DateTime LastSavedUtc { get; set; }
}
