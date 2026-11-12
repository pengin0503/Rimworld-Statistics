namespace RimworldStatistics.Core;

/// <summary>One playthrough, rebuilt from the profile's records every time (spec §6.2, §7.4). Never stored.</summary>
public sealed class Colony
{
    /// <summary>"fp:&lt;hash&gt;", "fp:&lt;hash&gt;#n" or "id:&lt;campaignId&gt;". User decisions refer to this key.</summary>
    public string Key { get; set; } = "";

    /// <summary>Other keys that also name this colony (several ledger entries matched, or a manual merge).</summary>
    public List<string> AliasKeys { get; } = new();

    public List<SaveObservation> Observations { get; } = new();
    public List<LiveCampaign> LiveCampaigns { get; } = new();
    public List<EndingEvent> Endings { get; } = new();
    public List<LineageEntry> RecoveredEndings { get; } = new();

    /// <summary>Union of the observations' ending traces.</summary>
    public HashSet<EndingType> Traces { get; } = new();

    public bool Excluded { get; set; }
    /// <summary>False only for members of the active view scope. Attribution still sees all colonies.</summary>
    public bool OutOfScope { get; set; }
    public bool Counted => !Excluded && !OutOfScope;
    public string? FingerprintHash { get; set; }
    public HashSet<string> StartingPawnUnion { get; } = new(StringComparer.Ordinal);

    /// <summary>Campaign ids of the live records and id-stamped saves in this colony.</summary>
    public IEnumerable<string> CampaignIds()
        => LiveCampaigns.Select(l => l.CampaignId)
            .Concat(Observations.Select(o => o.Identity.ModCampaignId).OfType<string>())
            .Distinct(StringComparer.Ordinal);
}
