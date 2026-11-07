using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Runs <see cref="ImportEngine"/> on a pool thread (spec §7.5). Game API is touched only before the task starts; the
/// task itself only publishes progress and results through volatile fields.
/// </summary>
public static class ImportRunner
{
    private static readonly object Gate = new();
    private static CancellationTokenSource? cts;
    private static Task? task;
    private static readonly AutoImportGate AutoGate = new();

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
    public static void StartIfIdle() => Start(manual: true);

    private static void Start(bool manual)
    {
        lock (Gate)
        {
            if (task is { IsCompleted: false })
                return;
            if (manual)
                AutoGate.StartedManually();
            cts?.Dispose();
            cts = null;
            progress = null;
            lastError = null;
            try
            {
                var savesDir = GenFilePaths.SavedGamesFolderPath;
                var source = new CancellationTokenSource();
                cts = source;
                task = Task.Run(() => Run(savesDir, source.Token));
            }
            catch (Exception e)
            {
                lastError = e;
            }
        }
    }

    public static void Cancel()
    {
        lock (Gate)
        {
            AutoGate.CancelledByUser();
            cts?.Cancel();
        }
    }

    public static void CancelForGameStart()
    {
        lock (Gate)
        {
            if (task is not { IsCompleted: false } || cts is not { IsCancellationRequested: false })
                return;
            AutoGate.InterruptedByGame();
            cts.Cancel();
        }
    }

    public static void GameSaveCompleted() => AutoGate.RefreshAfterSave();

    /// <summary>
    /// Main thread, title screen (called on every menu draw). Starts the first scan of this session when auto-import is
    /// on, and continues a scan that a game start interrupted; never restarts a scan the user cancelled.
    /// </summary>
    public static void AutoStartOnce()
    {
        lock (Gate)
            if (AutoGate.ShouldStart(RimworldStatisticsMod.Settings.autoImport, task is { IsCompleted: false }))
                Start(manual: false);
    }

    private static void Run(string savesDir, CancellationToken ct)
    {
        try
        {
            lastResult = new ImportEngine(new SaveReader()).Run(savesDir, ProfileService.Session, new ProgressSink(), ct);
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
