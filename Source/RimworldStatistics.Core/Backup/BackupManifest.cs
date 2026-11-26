namespace RimworldStatistics.Core;

public sealed record BackupFileEntry(string Path, long Size, string Sha256);

/// <summary>Kinds of backup. Old manifests (written before the field existed) have no kind and mean <see cref="Manual"/>.</summary>
public static class BackupKinds
{
    public const string Manual = "manual";
    public const string PreRestore = "pre-restore";
}

/// <summary>
/// <paramref name="Kind"/> is additive: v2.0 manifests without it deserialize as <see cref="BackupKinds.Manual"/>, and
/// older readers ignore the extra JSON property.
/// </summary>
public sealed record BackupManifest(
    int Format, DateTime CreatedUtc, string ModVersion, int ProfileSchemaVersion,
    string Label, int Colonies, int Observations, List<BackupFileEntry> Files,
    string Kind = BackupKinds.Manual);

public sealed record BackupSource(byte[] Profile, string? ArchiveDirectory, string? Preferences);

public sealed record BackupMetadata(
    DateTime CreatedUtc, string ModVersion, string Label, string Kind = BackupKinds.Manual);

public enum BackupProblem { None, Unreadable, NoManifest, UnknownFormat, HashMismatch, ProfileUnreadable, NewerSchema, TooLarge }

public sealed record BackupValidation(BackupProblem Problem, BackupManifest? Manifest = null)
{
    public bool Ok => Problem == BackupProblem.None;
}

public sealed record BackupInfo(string Path, long Size, BackupValidation Validation);

/// <summary>
/// A read-only preview of a backup: validation, manifest data, backup and current counts, and the uncompressed payload
/// size. <see cref="ProfileBackup.Preview"/> produces it without writing anything.
/// </summary>
public sealed record RestorePreview(
    BackupProblem Problem,
    BackupManifest? Manifest,
    int FileCount,
    long TotalUncompressedBytes,
    int? CurrentColonies = null,
    int? CurrentObservations = null)
{
    public bool Ok => Problem == BackupProblem.None;
    public int? BackupColonies => Manifest?.Colonies;
    public int? BackupObservations => Manifest?.Observations;
    public string Kind => Manifest?.Kind ?? BackupKinds.Manual;
}
