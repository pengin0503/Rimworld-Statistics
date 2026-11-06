using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>The single <see cref="ProfileSession"/> of this game session, created on first use on the main thread.</summary>
public static class ProfileService
{
    private static readonly object Gate = new();
    private static ProfileSession? session;
    private static bool reported;

    public static string DirectoryPath => Path.Combine(GenFilePaths.SaveDataFolderPath, "RimworldStatistics");

    public static ProfileSession Session
    {
        get
        {
            lock (Gate)
            {
                if (session != null)
                    return session;
            }
            // GenFilePaths reads Unity's persistent data path, which is main-thread only.
            if (!UnityData.IsInMainThread)
                throw new InvalidOperationException("ProfileService must first be used on the main thread.");
            var created = new ProfileSession(new ProfileStore(DirectoryPath), () => DateTime.UtcNow);
            lock (Gate)
                session ??= created;
            ReportLoadProblemsOnce();
            return session;
        }
    }

    public static StatsOptions Options => new() { IncludeDevMode = RimworldStatisticsMod.Settings.includeDevMode };

    public static StatsView View => Session.GetView(Options);

    /// <summary>Warns once if the record file had to be restored, was reset, or cannot be written this session.</summary>
    public static void ReportLoadProblemsOnce()
    {
        ProfileLoadResult r;
        lock (Gate)
        {
            if (reported || session is null)
                return;
            reported = true;
            r = session.LoadResult;
        }
        var file = Path.Combine(DirectoryPath, "profile.json");
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
