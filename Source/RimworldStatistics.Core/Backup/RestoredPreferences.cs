namespace RimworldStatistics.Core;

/// <summary>
/// The presentation settings a backup restore brought back, on their way from the restore's pool thread to the main thread
/// (#325). The restore offers them only after it succeeded, and only when the player asked for them; the settings in use are
/// not touched before that. The main thread takes them once, from a hook that runs every frame whatever window is open, so
/// closing the statistics window or switching tabs while the restore runs no longer loses them. The newest offer wins.
/// </summary>
public sealed class RestoredPreferences
{
    private string? _pending;

    /// <summary>A restore finished successfully with <paramref name="result"/>; <paramref name="requested"/>: the player asked for its settings.</summary>
    public void Offer(RestoreResult result, bool requested)
    {
        if (requested && result.Preferences is { } json)
            Volatile.Write(ref _pending, json);
    }

    /// <summary>Settings are waiting to be applied (cheap enough to ask every frame).</summary>
    public bool Pending => Volatile.Read(ref _pending) is not null;

    /// <summary>
    /// Main thread. Hands the waiting settings, normalised as the window reads them, to <paramref name="apply"/> once.
    /// Returns false when nothing was waiting.
    /// </summary>
    public bool ApplyTo(Action<string> apply)
    {
        var json = Interlocked.Exchange(ref _pending, null);
        if (json is null)
            return false;
        apply(ViewPreferences.Parse(json).ToJson());
        return true;
    }
}
