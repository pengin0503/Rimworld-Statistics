namespace RimworldStatistics.Core;

/// <summary>
/// Keeps the backup folder bounded. Only valid backups are managed: unreadable or invalid zips are never deleted (and
/// do not count toward the caps), so a damaged file can be inspected and recovered by hand.
/// </summary>
public static class BackupRetention
{
    /// <summary>Keep at most this many valid backups by default.</summary>
    public const int DefaultMaxCount = 20;

    /// <summary>
    /// Keep at most 2 GiB of valid backups by default, the same bound <see cref="ProfileBackup.MaxTotalUncompressedBytes"/>
    /// uses for one backup: a retained set a few hundred manual backups deep is not useful, while 2 GiB is generous for a
    /// handful of checkpoint-archive backups.
    /// </summary>
    public const long DefaultMaxTotalBytes = ProfileBackup.MaxTotalUncompressedBytes;

    /// <summary>
    /// Deletes the oldest valid backups beyond <paramref name="maxCount"/> or <paramref name="maxTotalBytes"/>, oldest
    /// (<see cref="BackupManifest.CreatedUtc"/>, then path) first, and returns the deleted paths. Always kept: the newest
    /// valid backup, the newest pre-restore backup (even when it is older than other backups), and any path in
    /// <paramref name="protectedPaths"/> (for example the backup just created).
    /// </summary>
    public static IReadOnlyList<string> Apply(string backupDirectory, int maxCount = DefaultMaxCount,
        long maxTotalBytes = DefaultMaxTotalBytes, IEnumerable<string>? protectedPaths = null)
    {
        var valid = ProfileBackup.List(backupDirectory).Where(info => info.Validation.Ok).ToList();
        if (valid.Count == 0)
            return [];

        // ProfileBackup.List orders newest first (CreatedUtc descending, then path), so this is the newest valid backup.
        var newest = valid[0];
        var newestPreRestore = valid.FirstOrDefault(info =>
            string.Equals(info.Validation.Manifest?.Kind, BackupKinds.PreRestore, StringComparison.OrdinalIgnoreCase));

        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { newest.Path };
        if (newestPreRestore is not null)
            keep.Add(newestPreRestore.Path);
        if (protectedPaths is not null)
            foreach (var path in protectedPaths)
                keep.Add(path);

        var remaining = valid.Count;
        var totalBytes = valid.Sum(info => info.Size);
        var deleted = new List<string>();
        foreach (var info in valid
            .OrderBy(info => info.Validation.Manifest?.CreatedUtc ?? DateTime.MinValue)
            .ThenBy(info => info.Path, StringComparer.Ordinal))
        {
            if (remaining <= maxCount && totalBytes <= maxTotalBytes)
                break;
            if (keep.Contains(info.Path))
                continue;
            try
            {
                File.Delete(info.Path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;   // a locked/unavailable file is left for a later create to prune
            }
            remaining--;
            totalBytes -= info.Size;
            deleted.Add(info.Path);
        }
        return deleted;
    }
}
