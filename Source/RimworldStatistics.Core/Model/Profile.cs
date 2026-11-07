using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimworldStatistics.Core;

/// <summary>User decisions (record ④ in spec §6.2), referenced by colony key.</summary>
public sealed class UserDecisions
{
    public List<string> ExcludedCampaignKeys { get; set; } = new();
    public List<List<string>> ManualMerges { get; set; } = new();

    /// <summary>Stored but not applied in v1 (spec §7.4, §13).</summary>
    public List<List<string>> ManualSplits { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>Ledger entry for a colony key whose fingerprint collides with another colony's (spec §6.2).</summary>
public sealed class ColonyKeyEntry
{
    public string Key { get; set; } = "";
    public string FingerprintHash { get; set; } = "";
    public List<string> StartingPawnIds { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

public sealed class ImportState
{
    public DateTime? LastScanUtc { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>The whole record file (profile.json).</summary>
public sealed class Profile
{
    /// <summary>2: numeric observations (numeric expansion design §2). A file without the field is schema 1.</summary>
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<SaveObservation> SaveObservations { get; set; } = new();
    public List<LiveCampaign> LiveCampaigns { get; set; } = new();
    public List<EndingEvent> EndingEvents { get; set; } = new();
    public UserDecisions UserDecisions { get; set; } = new();
    public List<ColonyKeyEntry> ColonyKeys { get; set; } = new();
    public ImportState ImportState { get; set; } = new();

    /// <summary>Numeric summaries, kept apart from <see cref="SaveObservations"/> and their pruning.</summary>
    [JsonConverter(typeof(MetricObservationsConverter))]
    public List<MetricObservation> MetricObservations { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
