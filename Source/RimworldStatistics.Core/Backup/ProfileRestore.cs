using System.IO.Compression;

namespace RimworldStatistics.Core;

public sealed record RestoreResult(string? Preferences);

public static partial class ProfileBackup
{
    private static readonly string[] RestorePaths =
        [ProfileFile, "profile.json.bak1", "profile.json.bak2", "profile.json.bak3", "profile.json.previous", ArchiveDirectory];

    public static RestoreResult Restore(string zipPath, string dataDirectory, Action? loadRestoredProfile = null, Action? beforeSwap = null)
    {
        Directory.CreateDirectory(dataDirectory);
        var work = Path.Combine(dataDirectory, ".restore-" + Guid.NewGuid().ToString("N"));
        var staged = Path.Combine(work, "incoming");
        var previous = Path.Combine(work, "previous");
        var keepRecovery = false;

        try
        {
            string? preferences;
            try
            {
                using var zip = ZipFile.OpenRead(zipPath);
                var validation = Inspect(zip, Profile.CurrentSchemaVersion, staged);
                if (!validation.Ok)
                    throw new InvalidDataException($"Restore verification failed: {validation.Problem}");
                preferences = zip.GetEntry(PreferencesFile) is { } entry ? ReadText(entry.Open()) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new InvalidDataException("Cannot stage backup for restoration.", ex);
            }

            Directory.CreateDirectory(previous);
            using var writeLock = new FileStream(Path.Combine(dataDirectory, ProfileFile + ".lock"),
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            beforeSwap?.Invoke();
            var movedOut = new List<string>();
            var movedIn = new List<string>();
            try
            {
                foreach (var name in RestorePaths)
                    if (Move(dataDirectory, previous, name))
                        movedOut.Add(name);
                foreach (var name in RestorePaths)
                    if (Move(staged, dataDirectory, name))
                        movedIn.Add(name);
                loadRestoredProfile?.Invoke();
            }
            catch (Exception installError)
            {
                try
                {
                    for (var i = movedIn.Count - 1; i >= 0; i--)
                        Move(dataDirectory, staged, movedIn[i]);
                    for (var i = movedOut.Count - 1; i >= 0; i--)
                        Move(previous, dataDirectory, movedOut[i]);
                }
                catch (Exception rollbackError)
                {
                    keepRecovery = true;
                    throw new AggregateException($"The original files are retained in {previous}.", installError, rollbackError);
                }
                throw;
            }
            return new RestoreResult(preferences);
        }
        finally
        {
            if (!keepRecovery && Directory.Exists(work))
            {
                try { Directory.Delete(work, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static bool Move(string from, string to, string name)
    {
        var source = Path.Combine(from, name);
        var destination = Path.Combine(to, name);
        if (Directory.Exists(source))
            Directory.Move(source, destination);
        else if (File.Exists(source))
            File.Move(source, destination);
        else
            return false;
        return true;
    }
}
