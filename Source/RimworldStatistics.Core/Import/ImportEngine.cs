namespace RimworldStatistics.Core;

public sealed record ImportProgress(int Total, int Done, int Failed, string? CurrentFile);

public sealed class ImportResult
{
    public ImportMode Mode { get; init; }

    /// <summary>Saves read in this scan, failed reads included.</summary>
    public int Read { get; init; }

    /// <summary>Saves not read (already read), or, in a forced re-check, read but recorded by a newer version of this MOD.</summary>
    public int Skipped { get; init; }

    /// <summary>Reads that failed. In a forced re-check, a good earlier record of the same file is kept (<see cref="KeptAfterFailure"/>).</summary>
    public int Failed { get; init; }

    /// <summary>Forced re-check: saves that hold what their record was read from; nothing was written for them.</summary>
    public int Unchanged { get; init; }

    /// <summary>Forced re-check: saves whose bytes differ from their record although name, size and time are the same.</summary>
    public int Replaced { get; init; }

    /// <summary>Forced re-check: failed reads of saves with a good record, which was kept.</summary>
    public int KeptAfterFailure { get; init; }

    /// <summary>Saves read and committed as new: not failed, not unchanged, not replaced.</summary>
    public int Added => Math.Max(0, Read - Failed - Unchanged - Replaced);

    public bool Cancelled { get; init; }

    /// <summary>Save file names present in the folder at scan time (for "元ファイルの有無", spec §7.5).</summary>
    public required IReadOnlyList<string> PresentFiles { get; init; }
}

/// <summary>
/// Reads new or changed saves in one folder into the profile (spec §7.1, §7.5), or, in a forced re-check, every save
/// (#310). Runs on a background thread and touches no game API and no save file (read only). Cancellation stops the reads
/// in progress within one block; a save whose read was stopped is not recorded at all, so what was read is kept and the
/// next run continues with the rest.
/// </summary>
public sealed class ImportEngine
{
    private readonly SaveReader _reader;
    private readonly TimeProvider _timeProvider;
    private readonly int _readAhead;
    private readonly ThreadPriority _priority;

    public ImportEngine(SaveReader reader, int readAhead = -1)
        : this(reader, readAhead, TimeProvider.System) { }

    public ImportEngine(SaveReader reader, int readAhead, TimeProvider timeProvider)
        : this(reader, readAhead, timeProvider, ThreadPriority.Normal) { }

    /// <param name="readAhead">Saves read at once; negative for <see cref="ResourcePolicy.StandardImportThreads"/>.</param>
    /// <param name="priority">
    /// The priority the reading threads run at (the calling thread's is restored when the run ends). Normal leaves every
    /// thread as it is.
    /// </param>
    public ImportEngine(SaveReader reader, int readAhead, TimeProvider timeProvider, ThreadPriority priority)
    {
        _reader = reader;
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _readAhead = readAhead < 0 ? ResourcePolicy.StandardImportThreads(Environment.ProcessorCount)
            : Math.Min(readAhead, ResourcePolicy.MaxImportThreads);
        _priority = priority;
    }

    /// <summary>Saves read at once.</summary>
    public int ReadAhead => _readAhead;

    private static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(5);

    private static void Commit(Profile p, SaveReadResult result, bool otherContent)
    {
        // A successful v1 read must not be committed before the numeric observations:
        // if their mutation fails, leave an Error observation so the file is retried.
        // Record the embedded checkpoint before its matching
        // SaveFile counters. Compaction then knows this .rws is an
        // ancestry witness even when files are scanned out of order.
        if (result.Checkpoint is not null)
            ProfileMutations.UpsertMetricObservation(p, result.Checkpoint);
        if (result.Metrics is not null)
            ProfileMutations.UpsertMetricObservation(p, result.Metrics);
        ProfileMutations.AddSaveObservation(p, result.Observation, otherContent);
    }

    private static bool HasGoodRecord(Profile p, string name, long size, DateTime time)
    {
        foreach (var o in p.SaveObservations)
            if (!o.Superseded && o.Status != ObservationStatus.Error && o.FileName == name && o.SameFileContent(size, time))
                return true;
        return false;
    }

    public static bool IsSaveFile(string fileName)
        => fileName.EndsWith(".rws", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".rws.old", StringComparison.OrdinalIgnoreCase);

    public ImportResult Run(string savesDir, ProfileSession session, IProgress<ImportProgress>? progress, CancellationToken ct)
        => Run(savesDir, session, progress, ct, ImportMode.Incremental);

    public ImportResult Run(string savesDir, ProfileSession session, IProgress<ImportProgress>? progress, CancellationToken ct,
        ImportMode mode)
        => ThreadPriorities.Run(_priority, () => RunAtPriority(savesDir, session, progress, ct, mode));

    private ImportResult RunAtPriority(string savesDir, ProfileSession session, IProgress<ImportProgress>? progress,
        CancellationToken ct, ImportMode mode)
    {
        if (!Directory.Exists(savesDir))
            return new ImportResult { Mode = mode, PresentFiles = Array.Empty<string>() };
        var forced = mode == ImportMode.ForcedRecheck;

        var files = Directory.EnumerateFiles(savesDir)
            .Select(path => (Path: path, Name: Path.GetFileName(path)))
            .Where(f => IsSaveFile(f.Name))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();

        int read = 0, skipped = 0, failed = 0, done = 0, unchanged = 0, replaced = 0, keptAfterFailure = 0;
        var cancelled = false;
        var lastSaveRequest = _timeProvider.GetTimestamp();
        var present = new List<string>(files.Count);

        // Warm scans used to search the entire profile for every unchanged
        // .rws (O(files * observations)). Cache only the matching read
        // identities; mutations from another thread invalidate the cache.
        var indexedRevision = -1L;
        var skippable = new HashSet<(string FileName, long FileSize, DateTime LastWriteUtc)>();
        var pending = new Dictionary<int, Task<(SaveReadResult? Result, Exception? Error)>>();
        List<Task<(SaveReadResult? Result, Exception? Error)>>? discarded = null;
        var nextToSchedule = 0;

        // A forced re-check reads every save: whether one holds what its record was read from is decided after reading it.
        bool AlreadyRead(string name, long size, DateTime time) => !forced && session.ReadWithRevision((p, revision) =>
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
                (discarded ??= new List<Task<(SaveReadResult? Result, Exception? Error)>>()).Add(task);
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
                pending[index] = Task.Run<(SaveReadResult? Result, Exception? Error)>(() =>
                {
                    try
                    {
                        return ct.IsCancellationRequested
                            ? (null, null)
                            : (ThreadPriorities.Run(_priority, () => _reader.ReadUnlessCancelled(aheadPath, ct)), null);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        return (null, null);
                    }
                    catch (Exception e)
                    {
                        return (null, e);
                    }
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
                (long Size, DateTime Time)? identity = null;
                try
                {
                    long size = info.Length;
                    var time = info.LastWriteTimeUtc;
                    identity = (size, time);
                    if (AlreadyRead(name, size, time))
                    {
                        skipped++;
                        DiscardReadAhead(index);
                        continue;
                    }
                    SaveReadResult? result = null;
                    if (pending.Remove(index, out var ahead))
                    {
                        var (prefetched, error) = ahead.GetAwaiter().GetResult();
                        if (error is not null)
                        {
                            if (!ct.IsCancellationRequested)
                                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(error);
                        }
                        else if (prefetched is not null
                            && prefetched.Observation.FileName == name
                            && prefetched.Observation.FileSize == size
                            && prefetched.Observation.LastWriteUtc == time)
                        {
                            result = prefetched;
                        }
                    }
                    if (result is null && !ct.IsCancellationRequested)
                        result = _reader.ReadUnlessCancelled(path, ct);
                    if (result is null)
                    {
                        // Stopped before this save was read through (or its read-ahead was): record nothing for it.
                        cancelled = true;
                        break;
                    }
                    var obs = result.Observation;
                    read++;
                    countedRead = true;
                    if (obs.Status == ObservationStatus.Error)
                        failed++;
                    if (forced)
                    {
                        // A save that holds what its record was read from is not written at all, so a re-check of
                        // unchanged saves changes nothing. What is written is decided again under the lock.
                        switch (session.ReadWithRevision((p, _) => ForcedRecheck.Classify(p, result)))
                        {
                            case RecheckOutcome.Unchanged:
                                unchanged++;
                                continue;
                            case RecheckOutcome.FailedKept:
                                keptAfterFailure++;
                                continue;
                            case RecheckOutcome.NewerRecord:
                                read--;
                                skipped++;
                                continue;
                        }
                    }
                    var outcome = RecheckOutcome.New;
                    CommitCurrentFile(p =>
                    {
                        outcome = forced ? ForcedRecheck.Classify(p, result) : RecheckOutcome.New;
                        if (outcome is RecheckOutcome.New or RecheckOutcome.Changed)
                            Commit(p, result, otherContent: outcome == RecheckOutcome.Changed);
                    });
                    switch (outcome)
                    {
                        case RecheckOutcome.Changed: replaced++; break;
                        case RecheckOutcome.Unchanged: unchanged++; break;
                        case RecheckOutcome.FailedKept: keptAfterFailure++; break;
                        case RecheckOutcome.NewerRecord: read--; skipped++; break;
                    }
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
                    if (forced && identity is { } known && session.ReadWithRevision((p, _) =>
                            HasGoodRecord(p, name, known.Size, known.Time)))
                        keptAfterFailure++;   // a forced re-check never hides a good record behind a failure
                    else
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
            static void ObserveTask(Task<(SaveReadResult? Result, Exception? Error)> task, bool wait)
            {
                if (wait || task.IsCompleted)
                    _ = task.GetAwaiter().GetResult();
                else
                    _ = task.ContinueWith(static t => _ = t.Exception, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            var waitTasks = !cancelled && !ct.IsCancellationRequested;
            foreach (var task in pending.Values)
                ObserveTask(task, waitTasks);
            if (discarded is not null)
                foreach (var task in discarded)
                    ObserveTask(task, waitTasks);
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
        return new ImportResult
        {
            Mode = mode, Read = read, Skipped = skipped, Failed = failed, Unchanged = unchanged, Replaced = replaced,
            KeptAfterFailure = keptAfterFailure, Cancelled = cancelled, PresentFiles = present,
        };
    }
}
