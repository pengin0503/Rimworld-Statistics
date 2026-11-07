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

    public bool SameFileContent(long size, DateTime lastWriteUtc) => FileSize == size && LastWriteUtc == lastWriteUtc;
}
