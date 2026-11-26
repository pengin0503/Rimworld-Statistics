namespace RimworldStatistics.Core;

/// <summary>
/// Opens the single-writer lock file, retrying briefly while another session still holds it. A lock is normally released
/// in milliseconds, but a just-closed game or a backup/restore running in another process may keep it a little longer.
/// The last attempt lets the original <see cref="IOException"/> through unchanged.
/// </summary>
internal static class FileLockRetry
{
    internal const int DefaultAttempts = 6;
    internal static readonly TimeSpan DefaultInitialDelay = TimeSpan.FromMilliseconds(50);

    internal static FileStream Acquire(string path, int attempts = DefaultAttempts,
        TimeSpan? initialDelay = null, Action<TimeSpan>? pause = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);
        var wait = initialDelay ?? DefaultInitialDelay;
        var sleep = pause ?? Thread.Sleep;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (attempt < attempts)
            {
                sleep(wait);
                if (wait < TimeSpan.FromDays(1))
                    wait += wait;
            }
        }
    }
}
