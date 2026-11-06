using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Runs <see cref="ImportEngine"/> on a pool thread (spec §7.5). Game API is touched only on the main thread, before
/// the task starts; the task itself only publishes progress and results through volatile fields.
/// </summary>
public static class ImportRunner
{
    private static readonly object Gate = new();
    private static CancellationTokenSource? cts;
    private static Task? task;
    private static bool autoStarted;

    private static volatile ImportProgress? progress;
    private static volatile ImportResult? lastResult;
    private static volatile Exception? lastError;

    public static bool Running
    {
        get
        {
            lock (Gate)
                return task is { IsCompleted: false };
        }
    }

    public static ImportProgress? Progress => progress;
    public static ImportResult? LastResult => lastResult;
    public static Exception? LastError => lastError;

    /// <summary>Main thread. Starts a scan unless one is running.</summary>
    public static void StartIfIdle()
    {
        if (Running)
            return;
        var savesDir = GenFilePaths.SavedGamesFolderPath;   // main thread only
        var session = ProfileService.Session;               // created on the main thread
        lock (Gate)
        {
            if (task is { IsCompleted: false })
                return;
            cts?.Dispose();
            var source = new CancellationTokenSource();
            cts = source;
            progress = null;
            lastError = null;
            task = Task.Run(() => Run(savesDir, session, source.Token));
        }
    }

    public static void Cancel()
    {
        lock (Gate)
            cts?.Cancel();
    }

    /// <summary>
    /// Main thread, title screen. Starts the first scan of this session when auto-import is on, and resumes a scan that
    /// was cancelled because a game was started or loaded (spec §7.5: continue on the next return to the title screen).
    /// </summary>
    public static void AutoStartOnce()
    {
        if (!RimworldStatisticsMod.Settings.autoImport || Running)
            return;
        var resume = lastResult is { Cancelled: true };
        if (autoStarted && !resume)
            return;
        autoStarted = true;
        StartIfIdle();
    }

    private static void Run(string savesDir, ProfileSession session, CancellationToken ct)
    {
        try
        {
            lastResult = new ImportEngine(new SaveReader()).Run(savesDir, session, new ProgressSink(), ct);
        }
        catch (Exception e)
        {
            lastError = e;   // shown by the Import tab on the main thread
        }
    }

    private sealed class ProgressSink : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => progress = value;
    }
}
