namespace RimworldStatistics.Core;

/// <summary>
/// Decides when the title screen starts an import by itself (spec §7.1, §7.5): once per session, and once more after a
/// scan was interrupted because a game started. A scan the user cancelled is not restarted. Thread-safe: the title menu
/// asks on the main thread, while the game reports an interruption from its long-event thread.
/// </summary>
public sealed class AutoImportGate
{
    private readonly object gate = new();
    private bool started;
    private bool resumeRequested;
    private bool userCancelled;

    /// <summary>Call on every title-menu draw; true means "start a scan now".</summary>
    public bool ShouldStart(bool autoImportEnabled, bool running)
    {
        lock (gate)
        {
            if (!autoImportEnabled || running || userCancelled)
                return false;
            if (started && !resumeRequested)
                return false;
            started = true;
            resumeRequested = false;
            return true;
        }
    }

    public void CancelledByUser()
    {
        lock (gate)
        {
            userCancelled = true;
            resumeRequested = false;
        }
    }

    public void StartedManually()
    {
        lock (gate)
        {
            userCancelled = false;
            started = true;
            resumeRequested = false;
        }
    }

    public void RefreshAfterSave() => RequestRefresh();

    /// <summary>A running scan was stopped because a game started or loaded.</summary>
    public void InterruptedByGame() => RequestRefresh();

    private void RequestRefresh()
    {
        lock (gate)
            if (!userCancelled)
                resumeRequested = true;
    }
}
