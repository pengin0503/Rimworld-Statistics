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

    private static readonly ImportRunStatus Status = new();

    public static bool Running
    {
        get
        {
            lock (Gate)
                return task is { IsCompleted: false };
        }
    }

    /// <summary>The latest scan of this session: running, its outcome, or why it failed (#337).</summary>
    public static ImportRunState State => Status.Current;

    /// <summary>The outcome of the latest scan once it finished; null while one runs, after a failure, or before the first.</summary>
    public static ImportResult? LastResult => Status.Current.Result;

    /// <summary>Main thread. Starts a scan unless one is running.</summary>
    public static void StartIfIdle() => Start(manual: true, ImportMode.Incremental);

    /// <summary>
    /// Main thread, after the player confirmed it. Reads every save again and compares it with its record (#310). A game
    /// start interrupts it like any scan; what automatic import then continues on the title screen is an ordinary scan.
    /// </summary>
    public static void StartForcedRecheckIfIdle() => Start(manual: true, ImportMode.ForcedRecheck);

    private static void Start(bool manual, ImportMode mode = ImportMode.Incremental)
    {
        if (Current.ProgramState != ProgramState.Entry)
            return;
        lock (Gate)
        {
            if (ProfileService.DataOperationRunning || task is { IsCompleted: false })
                return;
            if (manual)
                AutoGate.StartedManually();
            cts?.Dispose();
            cts = null;
            // Forget the previous scan's outcome: its counts are not this scan's.
            var run = Status.Begin(mode);
            try
            {
                var savesDir = GenFilePaths.SavedGamesFolderPath;
                var source = new CancellationTokenSource();
                cts = source;
                task = Task.Run(() => Run(run, savesDir, mode, source.Token));
            }
            catch (Exception e)
            {
                Status.Fail(run, e);
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
            if (!ProfileService.DataOperationRunning && AutoGate.ShouldStart(RimworldStatisticsMod.Settings.autoImport, task is { IsCompleted: false }))
                Start(manual: false);
    }

    private static void Run(long run, string savesDir, ImportMode mode, CancellationToken ct)
    {
        try
        {
            Status.Complete(run, new ImportEngine(new SaveReader()).Run(savesDir, ProfileService.Session, new ProgressSink(run), ct, mode));
        }
        catch (Exception e)
        {
            Status.Fail(run, e);   // shown by the Import tab on the main thread
        }
    }

    private sealed class ProgressSink(long run) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => Status.Report(run, value);
    }
}
