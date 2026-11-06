namespace RimworldStatistics.Core;

/// <summary>
/// Decides when the title screen starts an import by itself (spec §7.1, §7.5): once per session, and once more after a
/// scan was interrupted because a game started. A scan the user cancelled is not restarted. Main thread only.
/// </summary>
public sealed class AutoImportGate
{
    private bool started;
    private bool resumeRequested;

    /// <summary>Call on every title-menu draw; true means "start a scan now".</summary>
    public bool ShouldStart(bool autoImportEnabled, bool running)
    {
        if (!autoImportEnabled || running)
            return false;
        if (started && !resumeRequested)
            return false;
        started = true;
        resumeRequested = false;
        return true;
    }

    /// <summary>A running scan was stopped because a game started or loaded.</summary>
    public void InterruptedByGame() => resumeRequested = true;
}
