using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Runs <see cref="ImportEngine"/> on a pool thread (spec §7.5). Game API is touched only before the task starts; the
/// task itself only publishes progress and results through volatile fields.
/// </summary>
public static class ImportRunner
{
    private static CancellationTokenSource? cts;
    private static Task? task;
    private static readonly AutoImportGate AutoGate = new();

    private static readonly ImportRunStatus Status = new();

    public static bool Running
    {
        get
        {
            lock (ProfileService.OperationGate)
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

    /// <summary>
    /// Main thread, title screen, after the player chose one failed save: re-reads only that save with forced re-check
    /// semantics (S18). Gated exactly like <see cref="StartForcedRecheckIfIdle"/>.
    /// </summary>
    public static void StartSingleSave(string fileName) => Start(manual: true, ImportMode.ForcedRecheck, fileName);

    private static void Start(bool manual, ImportMode mode = ImportMode.Incremental, string? singleFile = null)
    {
        if (Current.ProgramState != ProgramState.Entry)
            return;
        lock (ProfileService.OperationGate)
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
                // Read on this thread: the processing-resource and exclusion settings as they are when the scan starts.
                var settings = RimworldStatisticsMod.Settings;
                var threadMode = settings.ImportThreads;
                var threads = ResourcePolicy.ImportThreads(threadMode, settings.importThreadsManual, Environment.ProcessorCount);
                var priority = ResourcePolicy.ImportPriority(threadMode);
                var exclusions = settings.ImportExclusionPatterns;
                var source = new CancellationTokenSource();
                cts = source;
                task = Task.Run(() => Run(run, savesDir, mode, threads, priority, source.Token, exclusions, singleFile));
            }
            catch (Exception e)
            {
                Status.Fail(run, e);
            }
        }
    }

    public static void Cancel()
    {
        lock (ProfileService.OperationGate)
        {
            AutoGate.CancelledByUser();
            cts?.Cancel();
        }
    }

    public static void CancelForGameStart()
    {
        lock (ProfileService.OperationGate)
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
        if (ProfileService.DataOperationRunning || !AutoGate.MayNeedStart(RimworldStatisticsMod.Settings.autoImport))
            return;
        lock (ProfileService.OperationGate)
            if (!ProfileService.DataOperationRunning && AutoGate.ShouldStart(RimworldStatisticsMod.Settings.autoImport, task is { IsCompleted: false }))
                Start(manual: false);
    }

    private static void Run(long run, string savesDir, ImportMode mode, int threads, ThreadPriority priority, CancellationToken ct,
        IReadOnlyList<string> exclusions, string? singleFile)
    {
        try
        {
            var engine = new ImportEngine(new SaveReader(), threads, TimeProvider.System, priority);
            var result = singleFile is null
                ? engine.Run(savesDir, ProfileService.Session, new ProgressSink(run), ct, mode, exclusions)
                : engine.RunSingle(savesDir, singleFile, ProfileService.Session, new ProgressSink(run), ct, exclusions);
            Status.Complete(run, result);
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
