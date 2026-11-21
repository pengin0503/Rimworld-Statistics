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
    private int idle;   // 1 when no start/resume is pending under normal auto-import operation

    /// <summary>Lock-free per-frame fast check on the title screen.</summary>
    public bool MayNeedStart(bool autoImportEnabled) => autoImportEnabled && Volatile.Read(ref idle) == 0;

    /// <summary>Call on every title-menu draw; true means "start a scan now".</summary>
    public bool ShouldStart(bool autoImportEnabled, bool running)
    {
        if (!autoImportEnabled)
            return false;
        lock (gate)
        {
            if (userCancelled)
            {
                Volatile.Write(ref idle, 1);
                return false;
            }
            if (running)
                return false;
            if (started && !resumeRequested)
            {
                Volatile.Write(ref idle, 1);
                return false;
            }
            started = true;
            resumeRequested = false;
            Volatile.Write(ref idle, 1);
            return true;
        }
    }

    public void CancelledByUser()
    {
        lock (gate)
        {
            userCancelled = true;
            resumeRequested = false;
            Volatile.Write(ref idle, 1);
        }
    }

    public void StartedManually()
    {
        lock (gate)
        {
            userCancelled = false;
            started = true;
            resumeRequested = false;
            Volatile.Write(ref idle, 1);
        }
    }

    public void RefreshAfterSave() => RequestRefresh();

    /// <summary>A running scan was stopped because a game started or loaded.</summary>
    public void InterruptedByGame() => RequestRefresh();

    private void RequestRefresh()
    {
        lock (gate)
        {
            if (!userCancelled)
            {
                resumeRequested = true;
                Volatile.Write(ref idle, 0);
            }
        }
    }
}
