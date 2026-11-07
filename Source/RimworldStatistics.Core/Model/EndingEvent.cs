namespace RimworldStatistics.Core;

/// <summary>One recorded ending (record ③ in spec §6.2). Deduplicated by (CampaignId, Type, Ordinal, DevMode).</summary>
public sealed class EndingEvent
{
    public string CampaignId { get; set; } = "";
    public EndingType Type { get; set; }

    /// <summary>0-based count of earlier endings of the same type in the save's lineage.</summary>
    public int Ordinal { get; set; }

    public EndingSource Source { get; set; }
    public int TicksGame { get; set; }
    public DateTime RealTimeUtc { get; set; }
    public int Departed { get; set; }

    /// <summary>statsRecord.colonistsLaunched right after the ending (spec §8.4).</summary>
    public int LaunchedTotalAfter { get; set; }

    public bool DevMode { get; set; }
    public string? GameVersion { get; set; }
    public string? CreditsExcerpt { get; set; }

    /// <summary>
    /// statsRecord.colonistsKilled when the ending was recorded (ledger R-008, N1-F-003); null for endings recorded before
    /// this field existed, for traces, or when the colony state could not be read. Never taken from a later record.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? ColonistsKilledAtEnding { get; set; }

    /// <summary>Fields of a newer minor version of the same schema, kept and written back unchanged.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }
}
