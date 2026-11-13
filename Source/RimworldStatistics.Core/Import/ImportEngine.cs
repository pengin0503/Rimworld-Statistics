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
public sealed class ImportEngine
{
    private readonly SaveReader _reader;
    private readonly TimeProvider _timeProvider;
    private readonly int _readAhead;

    public ImportEngine(SaveReader reader, int readAhead = -1)
        : this(reader, readAhead, TimeProvider.System) { }

    public ImportEngine(SaveReader reader, int readAhead, TimeProvider timeProvider)
    {
        _reader = reader;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _readAhead = readAhead < 0 ? Math.Clamp(Environment.ProcessorCount / 2, 1, 3) : readAhead;
    }

    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

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
        var lastSaveRequest = _timeProvider.GetTimestamp();
        var present = new List<string>(files.Count);

        // Warm scans used to search the entire profile for every unchanged
        // .rws (O(files * observations)). Cache only the matching read
        // identities; mutations from another thread invalidate the cache.
        var indexedRevision = -1L;
        var skippable = new HashSet<(string FileName, long FileSize, DateTime LastWriteUtc)>();
        var pending = new Dictionary<int, Task<SaveReadResult?>>();
        List<Task<SaveReadResult?>>? discarded = null;
        var nextToSchedule = 0;

        bool AlreadyRead(string name, long size, DateTime time) => session.ReadWithRevision((p, revision) =>
        {
            if (revision != indexedRevision)
            {
                skippable = p.SaveObservations
                    .Where(o => ProfileMutations.IsCurrentRead(o, SaveReader.Version))
                    .Select(o => (o.FileName, o.FileSize, o.LastWriteUtc))
                    .ToHashSet();
                indexedRevision = revision;
            }
            return skippable.Contains((name, size, time));
        });

        void CommitCurrentFile(Action<Profile> change)
        {
            var (before, after) = session.MutateWithRevision(change);
            // A directory snapshot visits each filename once. This mutation can
            // only change that file's skip identities, which are no longer queried.
            if (indexedRevision == before)
                indexedRevision = after;
        }

        void DiscardReadAhead(int index)
        {
            if (pending.Remove(index, out var task) && !task.IsCompleted)
                (discarded ??= new List<Task<SaveReadResult?>>()).Add(task);
        }

        void ScheduleReadAhead(int current)
        {
            if (_readAhead <= 1)
                return;
            discarded?.RemoveAll(static task => task.IsCompleted);
            nextToSchedule = Math.Max(nextToSchedule, current + 1);
            while (pending.Count + (discarded?.Count ?? 0) < _readAhead
                && nextToSchedule < files.Count && !ct.IsCancellationRequested)
            {
                var index = nextToSchedule++;
                var (aheadPath, aheadName) = files[index];
                try
                {
                    var aheadInfo = new FileInfo(aheadPath);
                    if (!aheadInfo.Exists || AlreadyRead(aheadName, aheadInfo.Length, aheadInfo.LastWriteTimeUtc))
                        continue;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    continue;   // decided again, and reported, at its turn
                }
                pending[index] = Task.Run(() =>
                {
                    try { return ct.IsCancellationRequested ? null : _reader.ReadDetailed(aheadPath); }
                    catch (Exception) { return null; }
                });
            }
        }

        try
        {
            for (var index = 0; index < files.Count; index++)
            {
                var (path, name) = files[index];
                progress?.Report(new ImportProgress(files.Count, done, failed, name));
                if (ct.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                ScheduleReadAhead(index);
                done++;
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    DiscardReadAhead(index);
                    continue;   // deleted since the folder was listed
                }
                present.Add(name);
                var countedRead = false;
                try
                {
                    long size = info.Length;
                    var time = info.LastWriteTimeUtc;
                    if (AlreadyRead(name, size, time))
                    {
                        skipped++;
                        DiscardReadAhead(index);
                        continue;
                    }
                    SaveReadResult result;
                    if (pending.Remove(index, out var ahead) && ahead.GetAwaiter().GetResult() is { } prefetched
                        && prefetched.Observation.FileName == name && prefetched.Observation.FileSize == size
                        && prefetched.Observation.LastWriteUtc == time)
                        result = prefetched;
                    else
                        result = _reader.ReadDetailed(path);
                    var (obs, metrics) = result;
                    read++;
                    countedRead = true;
                    if (obs.Status == ObservationStatus.Error)
                        failed++;
                    CommitCurrentFile(p =>
                    {
                        // A successful v1 read must not be committed before the numeric observations:
                        // if their mutation fails, leave an Error observation so the file is retried.
                        // Record the embedded checkpoint before its matching
                        // SaveFile counters. Compaction then knows this .rws is an
                        // ancestry witness even when files are scanned out of order.
                        if (result.Checkpoint is not null)
                            ProfileMutations.UpsertMetricObservation(p, result.Checkpoint);
                        if (metrics is not null)
                            ProfileMutations.UpsertMetricObservation(p, metrics);
                        ProfileMutations.AddSaveObservation(p, obs);
                    });
                }
                catch (Exception e)
                {
                    DiscardReadAhead(index);
                    // SaveReader does not throw by contract; if anything else does, record this file as failed and go on.
                    if (!countedRead)
                        read++;
                    failed++;
                    var error = new SaveObservation
                    {
                        FileName = name, ReaderVersion = SaveReader.Version, Status = ObservationStatus.Error,
                        ErrorReason = $"{e.GetType().Name}: {e.Message}",
                    };
                    CommitCurrentFile(p => ProfileMutations.AddSaveObservation(p, error));
                }
                // Bound how often the background writer serializes a large profile.
                // Completion and cancellation both still request a final save.
                var now = _timeProvider.GetTimestamp();
                if (_timeProvider.GetElapsedTime(lastSaveRequest, now) >= SaveInterval)
                {
                    lastSaveRequest = now;
                    session.QueueSave();
                }
            }
        }
        finally
        {
            foreach (var task in pending.Values)
                task.GetAwaiter().GetResult();
            if (discarded is not null)
                foreach (var task in discarded)
                    task.GetAwaiter().GetResult();
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
