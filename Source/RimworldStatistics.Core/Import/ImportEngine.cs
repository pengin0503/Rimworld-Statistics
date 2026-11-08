namespace RimworldStatistics.Core;

public sealed record ImportProgress(int Total, int Done, int Failed, string? CurrentFile);

public sealed class ImportResult
{
    public int Read { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public bool Cancelled { get; init; }

    /// <summary>Save file names present in the folder at scan time (for "元ファイルの有無", spec §7.5).</summary>
    public required IReadOnlyList<string> PresentFiles { get; init; }
}

/// <summary>
/// Reads new or changed saves in one folder into the profile (spec §7.1, §7.5). Runs on a background thread and
/// touches no game API. Cancellation is checked between files; what was read is kept and the next run continues.
/// </summary>
public sealed class ImportEngine(SaveReader reader)
{
    private const int SaveEvery = 5;

    public static bool IsSaveFile(string fileName)
        => fileName.EndsWith(".rws", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".rws.old", StringComparison.OrdinalIgnoreCase);

    public ImportResult Run(string savesDir, ProfileSession session, IProgress<ImportProgress>? progress, CancellationToken ct)
    {
        if (!Directory.Exists(savesDir))
            return new ImportResult { PresentFiles = Array.Empty<string>() };

        var files = Directory.EnumerateFiles(savesDir)
            .Select(path => (Path: path, Name: Path.GetFileName(path)))
            .Where(f => IsSaveFile(f.Name))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        int read = 0, skipped = 0, failed = 0, done = 0;
        var cancelled = false;
        var present = new List<string>(files.Count);
        foreach (var (path, name) in files)
        {
            progress?.Report(new ImportProgress(files.Count, done, failed, name));
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            done++;
            var info = new FileInfo(path);
            if (!info.Exists)
                continue;   // deleted since the folder was listed
            present.Add(name);
            try
            {
                long size = info.Length;
                var time = info.LastWriteTimeUtc;
                if (!session.Read(p => ProfileMutations.NeedsRead(p, name, size, time, SaveReader.Version)))
                {
                    skipped++;
                    continue;
                }
                var result = reader.ReadDetailed(path);
                var (obs, metrics) = result;
                read++;
                if (obs.Status == ObservationStatus.Error)
                    failed++;
                session.Mutate(p =>
                {
                    ProfileMutations.AddSaveObservation(p, obs);
                    if (metrics is not null)
                        ProfileMutations.UpsertMetricObservation(p, metrics);
                    if (result.Checkpoint is not null)
                        ProfileMutations.UpsertMetricObservation(p, result.Checkpoint);
                }, SaveMode.None);
            }
            catch (Exception e)
            {
                // SaveReader does not throw by contract; if anything else does, record this file as failed and go on.
                read++;
                failed++;
                var error = new SaveObservation
                {
                    FileName = name, ReaderVersion = SaveReader.Version, Status = ObservationStatus.Error,
                    ErrorReason = $"{e.GetType().Name}: {e.Message}",
                };
                session.Mutate(p => ProfileMutations.AddSaveObservation(p, error), SaveMode.None);
            }
            if (read > 0 && read % SaveEvery == 0)
                session.Mutate(static _ => { }, SaveMode.Background);
        }

        // Spec §6.3: write when the import completes; a cancelled run still keeps what it read.
        var completed = !cancelled;
        session.Mutate(p =>
        {
            if (completed)
                p.ImportState.LastScanUtc = DateTime.UtcNow;
        }, SaveMode.Background);
        if (completed)
            progress?.Report(new ImportProgress(files.Count, done, failed, null));

        if (cancelled)
            present.AddRange(files.Skip(done).Select(f => f.Name).Where(n => File.Exists(Path.Combine(savesDir, n))));
        return new ImportResult { Read = read, Skipped = skipped, Failed = failed, Cancelled = cancelled, PresentFiles = present };
    }
}
