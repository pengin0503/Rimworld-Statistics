using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// The single <see cref="ProfileSession"/> of this game session, created on first use from any thread: the game runs
/// LoadedGame/StartedNewGame/FinalizeInit on its long-event worker thread (Root_Play, doAsynchronously: true, 1.7.4985).
/// </summary>
public static class ProfileService
{
    private static readonly object Gate = new();
    private static Task<ProfileSession>? sessionTask;
    private static int dataOperationRunning;
    public static bool DataOperationRunning => Volatile.Read(ref dataOperationRunning) != 0;

    public static string DirectoryPath => Path.Combine(GenFilePaths.SaveDataFolderPath, "RimworldStatistics");

    private static Task<ProfileSession> StartLoading(bool retry = false)
    {
        lock (Gate)
        {
            if (sessionTask == null || (retry && sessionTask.IsFaulted))
            {
                var path = DirectoryPath;
                sessionTask = Task.Run(() => OpenSession(path));
            }
            return sessionTask;
        }
    }

    public static ProfileSession Session => StartLoading(retry: true).GetAwaiter().GetResult();

    public static void PrepareForDialog() => StartLoading(retry: true);

    public static ProfileSession? TryGetSession()
    {
        var task = StartLoading();
        return task.IsCompleted ? task.GetAwaiter().GetResult() : null;
    }

    private static ProfileSession OpenSession(string path)
    {
        var session = new ProfileSession(new ProfileStore(path), () => DateTime.UtcNow,
            new FileCheckpointArchive(Path.Combine(path, "checkpoint-archive")));
        ReportLoadProblems(session.LoadResult, path);
        return session;
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
                    return WriteBackup(session, directory, metadata, preferences);
                });
            }
            finally
            {
                Volatile.Write(ref dataOperationRunning, 0);
            }
        });
    }

    internal static Task<RestoreResult> RestoreBackup(ProfileSession current, string zip, BackupMetadata metadata, string? preferences = null)
    {
        if (current.LoadResult.ReadOnly)
            throw new InvalidOperationException("The current profile is read-only.");
        var directory = BeginDataOperation();
        var replacement = new TaskCompletionSource<ProfileSession>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (Gate)
            sessionTask = replacement.Task;

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
                    var recovery = WriteBackup(current, directory, metadata, preferences);
                    if (!ProfileBackup.Validate(recovery.Path, Profile.CurrentSchemaVersion).Ok)
                        throw new IOException("The pre-restore backup cannot be restored by this version.");
                });
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
        if (Current.ProgramState != ProgramState.Entry || ImportRunner.Running)
            throw new InvalidOperationException("Profile operations require the title screen and an idle import.");
        var directory = DirectoryPath;
        if (Interlocked.CompareExchange(ref dataOperationRunning, 1, 0) != 0)
            throw new InvalidOperationException("A profile operation is in progress.");
        return directory;
    }

    private static BackupInfo WriteBackup(ProfileSession session, string directory, BackupMetadata metadata, string? preferences)
    {
        var primary = Path.Combine(directory, "profile.json");
        var bytes = File.Exists(primary)
            ? File.ReadAllBytes(primary)
            : session.Read(profile => System.Text.Encoding.UTF8.GetBytes(ProfileJson.Serialize(profile)));
        return ProfileBackup.Create(new BackupSource(bytes, Path.Combine(directory, "checkpoint-archive"), preferences),
            Path.Combine(directory, "backups"), metadata);
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
