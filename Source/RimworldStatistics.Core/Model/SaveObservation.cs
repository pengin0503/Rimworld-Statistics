namespace RimworldStatistics.Core;

/// <summary>Result of reading one save file (record ① in spec §6.2).</summary>
public sealed class SaveObservation
{
    public string FileName { get; set; } = "";
    public long FileSize { get; set; }
    public DateTime LastWriteUtc { get; set; }
    public int ReaderVersion { get; set; }

    /// <summary>The file with this name has since changed; this observation is kept as history (spec §7.1).</summary>
    public bool Superseded { get; set; }

    public ObservationStatus Status { get; set; }
    public string? ErrorReason { get; set; }
    public string? GameVersion { get; set; }
    public int ModCount { get; set; }
    public ColonyIdentity Identity { get; set; } = new();
    public ObservedValues Values { get; set; } = new();
    public List<EndingType> EndingTraces { get; set; } = new();

    /// <summary>
    /// Traces the save's own lineageEndings explains (spec §4.3). They count only when the profile has no recorded
    /// ending of the same group for this colony, e.g. after profile.json was lost or restored from an older backup.
    /// </summary>
    public List<EndingType> ExplainedTraces { get; set; } = new();
    public List<LineageEntry> Lineage { get; set; } = new();

    public DisplayInfo Display { get; set; } = new();

    /// <summary>
    /// SHA-256 (lowercase hex) of every byte of the file this record was read from, the same as the key of the save's
    /// numeric observation; set by every complete read (#310). Null for a failed read and for records read before it was
    /// added (no reader version bump: it changes no statistic, so those files are not read again for it).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? ContentHash { get; set; }

    /// <summary>
    /// Auxiliary permanent RimWorld tales (<c>TileSettled</c>, <c>Marriage</c>, <c>MajorThreat</c>) converted from saved
    /// <c>TicksAbs</c> to colony-relative <c>TicksGame</c>. Strictly for branch-scoped chronicle display; never summed into
    /// numeric statistics, and devoid of pawn/settlement names.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<PermanentTaleFact>? PermanentTales { get; set; }

    /// <summary>
    /// Set when the save held more permanent tales than <see cref="SaveReader.MaxPermanentTales"/>: only the newest were kept, and
    /// every tale of the save after this colony-relative tick is in <see cref="PermanentTales"/> (#393). Null when none was left out.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? PermanentTalesDroppedThroughTick { get; set; }

    /// <summary>Fields of a newer minor version of the same schema, kept and written back unchanged.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? ExtensionData { get; set; }

    public bool SameFileContent(long size, DateTime lastWriteUtc) => FileSize == size && LastWriteUtc == lastWriteUtc;

    /// <summary>
    /// The same file name, size and time, and no proof of other bytes: the content hashes are equal or one is unknown.
    /// Name, size and time are the identity a scan skips by; only a forced re-check reads such a file again (#310).
    /// </summary>
    public bool SameFileContent(SaveObservation other)
        => FileName == other.FileName && SameFileContent(other.FileSize, other.LastWriteUtc)
            && (ContentHash is null || other.ContentHash is null || ContentHash == other.ContentHash);
}

/// <summary>Audited permanent RimWorld <c>TaleDef</c>s (<c>expireDays == -1</c>) surfaced as auxiliary chronicle entries.</summary>
public enum PermanentTaleKind
{
    TileSettled,
    Marriage,
    MajorThreat,
}

/// <summary>Detached, name-free permanent tale timestamp converted to colony-relative <c>TicksGame</c>.</summary>
public sealed record PermanentTaleFact(PermanentTaleKind Kind, int Id, int TicksGame);
