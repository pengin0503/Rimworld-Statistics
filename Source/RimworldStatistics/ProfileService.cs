using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// The single <see cref="ProfileSession"/> of this game session, created on first use from any thread: the game runs
/// LoadedGame/StartedNewGame/FinalizeInit on its long-event worker thread (Root_Play, doAsynchronously: true, 1.7.4985).
/// </summary>
public static class ProfileService
{
    private static readonly RetryableLoad<ProfileSession> Loader = new(() =>
    {
        var path = DirectoryPath;
        return Task.Run(() => OpenSession(path));
    });
    private static readonly RestoredPreferences PendingPreferences = new();
    internal static readonly object OperationGate = new();
    private static int dataOperationRunning;
    private static long resourceSettingsVersion;
    private static ProfileSession? activeOpeningSession;
    public static bool DataOperationRunning => Volatile.Read(ref dataOperationRunning) != 0;

    public static string DirectoryPath => Path.Combine(GenFilePaths.SaveDataFolderPath, "RimworldStatistics");

    /// <summary>The session, waiting for it; a load that failed is tried again (the caller reports a failure).</summary>
    public static ProfileSession Session => Loader.Start(retryFailed: true).GetAwaiter().GetResult();

    /// <summary>Opening the window starts the load, or tries a failed one again.</summary>
    public static void PrepareForDialog() => Loader.Start(retryFailed: true);

    /// <summary>The window's retry button (#326): tries a failed load again.</summary>
    public static void RetryLoading() => Loader.Start(retryFailed: true);

    /// <summary>
    /// Every frame of the window: the session once loaded, or null while it loads or after it failed, with the failure. A
    /// failed load stays failed until <see cref="RetryLoading"/> or <see cref="PrepareForDialog"/> (#326).
    /// </summary>
    public static ProfileSession? TryGetSession(out Exception? failure) => Loader.TryGet(out failure);

    /// <summary>Restored presentation settings are waiting for the main thread (#325).</summary>
    internal static bool RestoredPreferencesPending => PendingPreferences.Pending;

    /// <summary>
    /// Main thread: applies and writes the presentation settings a successful restore brought back, once (#325). Called every
    /// frame by <see cref="Patches.RestoredPreferencesPatch"/> and by the statistics window, so it does not depend on which
    /// window or tab is open when the restore finishes.
    /// </summary>
    internal static void ApplyRestoredPreferences()
    {
        PendingPreferences.ApplyTo(json =>
        {
            RimworldStatisticsMod.Settings.viewPreferencesJson = json;
            try
            {
                RimworldStatisticsMod.Instance?.WriteSettings();
            }
            catch (Exception e)
            {
                Log.Error($"[RimworldStatistics] The restored view settings are in use but could not be written to the settings file.\n{e}");
            }
        });
    }

    private static ProfileSession OpenSession(string path)
    {
        var session = new ProfileSession(new ProfileStore(path), () => DateTime.UtcNow,
            new FileCheckpointArchive(Path.Combine(path, "checkpoint-archive")));
        Volatile.Write(ref activeOpeningSession, session);
        long version;
        do
        {
            version = Volatile.Read(ref resourceSettingsVersion);
            session.ApplyCacheLimits(RimworldStatisticsMod.Settings.CacheLimits);
        }
        while (version != Volatile.Read(ref resourceSettingsVersion));
        ReportLoadProblems(session.LoadResult, path);
        return session;
    }

    /// <summary>
    /// Main thread, after the processing-resource settings changed: the loaded session (if any) and the chart cache keep
    /// what the new settings allow. A session opened later gets them when it opens. Never waits for the profile.
    /// </summary>
    internal static void ApplyResourceSettings()
    {
        Interlocked.Increment(ref resourceSettingsVersion);
        var limits = RimworldStatisticsMod.Settings.CacheLimits;
        if ((Loader.TryPeek() ?? Volatile.Read(ref activeOpeningSession)) is { } session)
            session.ApplyCacheLimits(limits);
        else
            RecorderChartCache.Shared.SetLimits(limits.ChartSeries, limits.ChartPoints);
    }

    internal static Task<BackupInfo> CreateBackup(ProfileSession session, BackupMetadata metadata, string? preferences = null)
    {
        var directory = BeginDataOperation();
        return Task.Run(() =>
        {
            try
            {
                return session.WithWritesPaused(() =>
                {
                    System.IO.Directory.CreateDirectory(directory);
                    using var writeLock = File.Open(Path.Combine(directory, "profile.json.lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    var created = WriteBackup(session, directory, metadata, preferences);
                    PruneOldBackups(directory, created.Path);
                    return created;
                });
            }
            finally
            {
                Volatile.Write(ref dataOperationRunning, 0);
            }
        });
    }

    /// <param name="preferences">The settings in use, kept in the backup written before the restore.</param>
    /// <param name="restorePreferences">Apply the backup's settings once the restore succeeded (#325).</param>
    internal static Task<RestoreResult> RestoreBackup(ProfileSession current, string zip, BackupMetadata metadata, string? preferences,
        bool restorePreferences)
    {
        // A newer-schema profile must never be replaced by an older backup. A session that is read-only because an
        // existing primary could not be read is different: restoring the newest valid backup is exactly the recovery
        // offered on the Data Management tab, and Restore moves the unreadable primary aside first.
        if (current.LoadResult.ReadOnly && current.LoadResult.Source == ProfileLoadSource.NewerSchema)
            throw new InvalidOperationException("The profile was written by a newer version of this mod and is read-only.");
        var directory = BeginDataOperation();
        var replacement = new TaskCompletionSource<ProfileSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        Loader.Replace(replacement.Task);

        return Task.Run(() =>
        {
            var closed = false;
            try
            {
                current.Close();
                closed = true;
                ProfileSession? restored = null;
                var result = ProfileBackup.Restore(zip, directory, () =>
                {
                    var candidate = OpenSession(directory);
                    if (candidate.LoadResult.Source != ProfileLoadSource.Primary || candidate.LoadResult.ReadOnly)
                        throw new IOException("The restored profile could not be loaded as the primary profile.");
                    restored = candidate;
                }, () =>
                {
                    // A recovery backup is best effort: the still-present primary can be exactly the unreadable or
                    // oversized file this restore is meant to recover from. Never let it abort the confirmed restore.
                    try
                    {
                        var recovery = WriteBackup(current, directory, metadata with { Kind = BackupKinds.PreRestore }, preferences);
                        if (!ProfileBackup.Validate(recovery.Path, Profile.CurrentSchemaVersion).Ok)
                            throw new IOException("The pre-restore backup cannot be restored by this version.");
                    }
                    catch (Exception e)
                    {
                        Log.Warning($"[RimworldStatistics] A backup could not be taken before restoring; the restore continues.\n{e}");
                    }
                });
                // The main thread applies them whatever window is open by then (#325); nothing is applied on failure.
                PendingPreferences.Offer(result, restorePreferences);
                replacement.SetResult(restored!);
                return result;
            }
            catch
            {
                try { replacement.SetResult(closed ? OpenSession(directory) : current); }
                catch (Exception error) { replacement.TrySetException(error); }
                throw;
            }
            finally
            {
                Volatile.Write(ref dataOperationRunning, 0);
            }
        });
    }

    private static string BeginDataOperation()
    {
        lock (OperationGate)
        {
            if (Current.ProgramState != ProgramState.Entry || ImportRunner.Running)
                throw new InvalidOperationException("Profile operations require the title screen and an idle import.");
            var directory = DirectoryPath;
            if (Interlocked.CompareExchange(ref dataOperationRunning, 1, 0) != 0)
                throw new InvalidOperationException("A profile operation is in progress.");
            return directory;
        }
    }

    private static BackupInfo WriteBackup(ProfileSession session, string directory, BackupMetadata metadata, string? preferences)
    {
        var archive = Path.Combine(directory, "checkpoint-archive");
        var backups = Path.Combine(directory, "backups");
        byte[] onDisk;
        try
        {
            // The normal path backs up the exact on-disk bytes (a read-only newer-schema profile must not be
            // downgraded by re-serializing it with this version's model).
            onDisk = File.ReadAllBytes(Path.Combine(directory, "profile.json"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            onDisk = [];
        }

        if (onDisk.Length != 0)
        {
            try
            {
                return ProfileBackup.Create(new BackupSource(onDisk, archive, preferences), backups, metadata);
            }
            catch (InvalidDataException)
            {
                // The primary exists but is damaged/oversized junk: guided recovery must still be able to take a
                // backup, so fall back to the session's in-memory profile (loaded from the newest valid backup).
            }
        }
        return ProfileBackup.Create(new BackupSource(session.Read(ProfileJson.SerializeToUtf8Bytes), archive, preferences),
            backups, metadata);
    }

    /// <summary>Retention is best-effort: the backup just written is already valid and a pruning failure must not fail it.</summary>
    private static void PruneOldBackups(string directory, string created)
    {
        try
        {
            BackupRetention.Apply(Path.Combine(directory, "backups"), protectedPaths: [created]);
        }
        catch (Exception e)
        {
            Log.Warning($"[RimworldStatistics] Old backups could not be pruned.\n{e}");
        }
    }

    public static StatsOptions Options => new() { IncludeDevMode = RimworldStatisticsMod.Settings.includeDevMode };

    private static void ReportLoadProblems(ProfileLoadResult r, string path)
    {
        var file = Path.Combine(path, "profile.json");
        var renamed = r.CorruptFileRenamedTo is null ? "" : $" The damaged file was kept as {r.CorruptFileRenamedTo}.";
        switch (r.Source)
        {
            case ProfileLoadSource.Backup1 or ProfileLoadSource.Backup2 or ProfileLoadSource.Backup3:
                Log.Warning($"[RimworldStatistics] {file} could not be read; restored from {r.Source}.{renamed}");
                break;
            case ProfileLoadSource.Empty when r.CorruptFileRenamedTo != null:
                Log.Warning($"[RimworldStatistics] {file} and its backups could not be read; starting with an empty record.{renamed}");
                break;
            case ProfileLoadSource.NewerSchema:
                Log.Warning($"[RimworldStatistics] {file} was written by a newer version of this mod. It is shown read-only and nothing is recorded this session.");
                return;
        }
        if (r.ReadOnly)
            Log.Warning($"[RimworldStatistics] {file} exists but could not be read or set aside. Nothing is recorded this session, so it is not overwritten.");
    }
}
