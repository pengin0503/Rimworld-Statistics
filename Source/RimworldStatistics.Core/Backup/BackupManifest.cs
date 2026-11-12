namespace RimworldStatistics.Core;

public sealed record BackupFileEntry(string Path, long Size, string Sha256);

public sealed record BackupManifest(
    int Format, DateTime CreatedUtc, string ModVersion, int ProfileSchemaVersion,
    string Label, int Colonies, int Observations, List<BackupFileEntry> Files);

public sealed record BackupSource(byte[] Profile, string? ArchiveDirectory, string? Preferences);

public sealed record BackupMetadata(DateTime CreatedUtc, string ModVersion, string Label);

public enum BackupProblem { None, Unreadable, NoManifest, UnknownFormat, HashMismatch, ProfileUnreadable, NewerSchema }

public sealed record BackupValidation(BackupProblem Problem, BackupManifest? Manifest = null)
{
    public bool Ok => Problem == BackupProblem.None;
}

public sealed record BackupInfo(string Path, long Size, BackupValidation Validation);
