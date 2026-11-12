using System.Text.Json;

namespace RimworldStatistics.Core;

public enum SaveMode
{
    /// <summary>Change in memory only; written by the next save or <see cref="ProfileSession.Flush"/>.</summary>
    None,

    /// <summary>Write on a pool thread; requests made while a write runs are coalesced into one more write.</summary>
    Background,

    /// <summary>Write before returning (endings, spec §8.4).</summary>
    Immediate,
}

public sealed class StatsView
{
    public required IReadOnlyList<Colony> Colonies { get; init; }
    public required StatsResult Stats { get; init; }
    public int MissingObservations { get; init; }

    /// <summary>The numeric statistics over the same colonies (Details tab).</summary>
    public NumericView Numeric { get; init; } = NumericView.Empty;
}

/// <summary>
/// The one in-memory profile, shared by the UI, the import thread and the game hooks (spec §6.3).
/// Changes and reads run under one lock; JSON is produced under the lock, and the file is written outside it.
/// Writes are ordered: a write never replaces a newer snapshot with an older one. Save errors are kept, never thrown.
/// </summary>
public sealed class ProfileSession
{
    private readonly ProfileStore _store;
    private readonly Profile _profile;
    private readonly ICheckpointArchive? _archive;
    private readonly object _lock = new();
    private readonly object _ioLock = new();
    private readonly Dictionary<(bool IncludeDevMode, string Visible, string Scope), StatsView> _views = new();

    private long _changeSeq;     // bumped by every change (under _lock)
    private long _writtenSeq;    // newest change seq that reached the file (under _ioLock)
    private bool _closed;
    private bool _backgroundRunning;
    private bool _backgroundPending;
    private Task? _backgroundTask;
    private volatile Exception? _lastSaveError;
    private volatile Exception? _lastArchiveError;

    public ProfileSession(ProfileStore store, Func<DateTime> utcNow, ICheckpointArchive? archive = null)
    {
        _store = store;
        LoadResult = store.Load(utcNow());
        _profile = LoadResult.Profile;
        _archive = LoadResult.ReadOnly ? null : archive;
        RestoreCheckpointMetrics();
    }

    public ProfileLoadResult LoadResult { get; }

    public Exception? LastSaveError => _lastSaveError;
    public Exception? LastArchiveError => _lastArchiveError;

    public void Mutate(Action<Profile> change, SaveMode mode)
    {
        MutateWithRevision(change);
        Save(mode);
    }

    internal (long Before, long After) MutateWithRevision(Action<Profile> change)
    {
        lock (_lock)
        {
            var before = _changeSeq;
            if (_closed)
                return (before, before);
            try
            {
                change(_profile);
                RestoreCheckpointMetrics();
            }
            finally
            {
                _views.Clear();
                _changeSeq++;
            }
            return (before, _changeSeq);
        }
    }

    internal void QueueSave() => Save(SaveMode.Background);

    private void RestoreCheckpointMetrics()
    {
        // Re-decoding the same checkpoint can add visible evidence, even if
        // the ancestor is still compressed. Merge before saving the profile,
        // so an unavailable archive cannot lose newly imported metrics.
        foreach (var incoming in _profile.PendingCheckpointArchive)
        {
            var target = _profile.MetricObservations.FirstOrDefault(o => !o.IsQuarantined
                && o.Source == MetricObservationSource.Checkpoint && o.Key == incoming.Key);
            if (target is null || target.Conflicted)
                continue;
            if (target.ArchivedMetrics)
                RestoreCheckpointMetrics(target);
            ProfileMutations.RestoreArchivedMetrics(target, incoming);
        }
        _profile.PendingCheckpointArchive.Clear();

        if (_archive is not null)
            foreach (var observation in _profile.MetricObservations)
                if (!observation.IsQuarantined && observation.ArchivedMetrics && !observation.Compressed)
                    RestoreCheckpointMetrics(observation);
    }

    private void RestoreCheckpointMetrics(MetricObservation target)
    {
        if (_archive is null || target.Identity.ModCampaignId is not { Length: > 0 } campaign)
            return;

        try
        {
            var restored = false;
            foreach (var observation in _archive.Read(campaign, target.Key))
            {
                if (observation.Source != MetricObservationSource.Checkpoint || observation.IsQuarantined
                    || observation.Metrics is not { Count: > 0 }
                    || observation.Identity?.ModCampaignId != campaign || observation.Key != target.Key
                    || observation.PayloadHash != target.PayloadHash || observation.ParentKey != target.ParentKey
                    || observation.SaveTick != target.SaveTick)
                    continue;
                ProfileMutations.RestoreArchivedMetrics(target, observation);
                restored = true;
            }
            if (restored)
                target.ArchivedMetrics = false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _lastArchiveError = e;
        }
    }

    public T Read<T>(Func<Profile, T> read)
    {
        lock (_lock)
            return read(_profile);
    }

    /// <summary>
    /// A profile read with the exact mutation revision under the same lock.
    /// A scan may cache an index while the revision is stable and use mutation
    /// receipts to identify its own edits without missing concurrent changes.
    /// </summary>
    internal T ReadWithRevision<T>(Func<Profile, long, T> read)
    {
        lock (_lock)
            return read(_profile, _changeSeq);
    }

    public StatsView? TryGetCachedView(StatsOptions options, IReadOnlyCollection<string>? visibleUnitIds = null, ViewScope? scope = null)
    {
        if (!Monitor.TryEnter(_lock))
            return null;
        try
        {
            return _views.GetValueOrDefault((options.IncludeDevMode, VisibilityKey(visibleUnitIds), scope?.CacheKey ?? ""));
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    public bool TryRead<T>(Func<Profile, T> read, out T result) =>
        TryReadWithRevision((profile, _) => read(profile), out result);

    public bool TryReadWithRevision<T>(Func<Profile, long, T> read, out T result)
    {
        if (!Monitor.TryEnter(_lock))
        {
            result = default!;
            return false;
        }
        try
        {
            result = read(_profile, _changeSeq);
            return true;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>Colonies and statistics, cached per option set until the next change.</summary>
    public StatsView GetView(StatsOptions options, IReadOnlyCollection<string>? visibleUnitIds = null, ViewScope? scope = null)
    {
        bool ledgerChanged;
        StatsView view;
        lock (_lock)
        {
            var cacheKey = (options.IncludeDevMode, VisibilityKey(visibleUnitIds), scope?.CacheKey ?? "");
            if (_views.TryGetValue(cacheKey, out var cached))
                return cached;
            var before = LedgerSignature();
            var colonies = CampaignMatcher.BuildColonies(_profile);
            scope?.Apply(colonies);
            var stats = StatsCalculator.Calculate(colonies, options);
            view = new StatsView
            {
                Colonies = colonies,
                Stats = stats,
                MissingObservations = _profile.SaveObservations.Count(o => !o.Superseded && o.Status != ObservationStatus.Ok),
                Numeric = MetricCalculator.BuildView(_profile, colonies, stats,
                    MetricRegistry.Compose(_profile.ModMetricDescriptors,
                        visibleUnitIds is null ? new HashSet<string>(StringComparer.Ordinal)
                            : new HashSet<string>(visibleUnitIds, StringComparer.Ordinal))),
            };
            ledgerChanged = LedgerSignature() != before;
            if (ledgerChanged)
            {
                _views.Clear();   // other option sets were built before the ledger changed; keep them consistent
                _changeSeq++;
            }
            // Keep the active scope plus a small number of recently requested scopes.
            if (_views.Count >= 4)
            {
                var victim = _views.Keys.FirstOrDefault(k => k.Scope.Length > 0);
                if (victim.Scope is { Length: > 0 })
                    _views.Remove(victim);
                else
                    _views.Clear();
            }
            _views[cacheKey] = view;
        }
        if (ledgerChanged)
            Save(SaveMode.Background);   // new colony keys must survive a restart (spec §6.2)
        return view;
    }

    private static string VisibilityKey(IReadOnlyCollection<string>? unitIds)
        => unitIds is null || unitIds.Count == 0 ? "" :
            string.Join("\u001f", unitIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    /// <summary>Entries are only appended and their pawn unions only grow, so this changes exactly when the ledger does.</summary>
    private (int, long) LedgerSignature() => (_profile.ColonyKeys.Count, _profile.ColonyKeys.Sum(k => (long)k.StartingPawnIds.Count));

    /// <summary>Waits for a running background write, then writes anything not yet on disk.</summary>
    public void Flush()
    {
        Task? running;
        lock (_lock)
            running = _backgroundTask;
        try
        {
            running?.Wait();
        }
        catch (AggregateException)
        {
            // WriteLatest records its own errors; nothing else can fault the task
        }
        WriteLatest();
    }

    public T WithWritesPaused<T>(Func<T> action)
    {
        lock (_ioLock)
        {
            WriteLatest();
            EnsureFlushed();
            return action();
        }
    }

    public void Close()
    {
        lock (_ioLock)
        lock (_lock)
        {
            if (_closed)
                return;
            WriteLatest();
            EnsureFlushed();
            _closed = true;
        }
    }

    private void EnsureFlushed()
    {
        if (_lastSaveError is { } saveError)
            throw new IOException("Could not flush the profile.", saveError);
        if (_lastArchiveError is { } archiveError)
            throw new IOException("Could not flush the checkpoint archive.", archiveError);
    }

    private void Save(SaveMode mode)
    {
        switch (mode)
        {
            case SaveMode.Immediate:
                WriteLatest();
                break;
            case SaveMode.Background:
                lock (_lock)
                {
                    if (_closed)
                        return;
                    if (_backgroundRunning)
                    {
                        _backgroundPending = true;
                        return;
                    }
                    _backgroundRunning = true;
                    _backgroundTask = Task.Run(BackgroundLoop);
                }
                break;
        }
    }

    private void BackgroundLoop()
    {
        while (true)
        {
            WriteLatest();
            lock (_lock)
            {
                if (!_backgroundPending)
                {
                    _backgroundRunning = false;
                    _backgroundTask = null;
                    return;
                }
                _backgroundPending = false;
            }
        }
    }

    private void ArchiveCheckpoints()
    {
        if (_archive is null)
            return;

        try
        {
            IReadOnlyList<ArchiveStep> steps;
            long plannedRevision;
            var batches = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            lock (_lock)
            {
                plannedRevision = _changeSeq;
                steps = CheckpointArchivePlanner.Plan(_profile);
                foreach (var step in steps)
                {
                    var campaign = step.Observation.Identity.ModCampaignId!;
                    if (!batches.TryGetValue(campaign, out var lines))
                        batches.Add(campaign, lines = new List<string>());
                    lines.Add(JsonSerializer.Serialize(step.Observation, ProfileJson.CompactOptions));
                }
            }

            foreach (var (campaign, lines) in batches)
                _archive.Append(campaign, lines);

            lock (_lock)
            {
                // A mutation can replace a breakdown in place without changing its
                // entry reference or size. Only trim the revision written to disk.
                if (_changeSeq == plannedRevision)
                    foreach (var step in steps)
                        CheckpointArchivePlanner.Apply(step);
            }

            if (batches.Count > 0)
                _lastArchiveError = null;
        }
        catch (Exception e)
        {
            _lastArchiveError = e;
        }
    }

    /// <summary>Serializes the current profile and writes it, unless the file already holds this or a newer snapshot.</summary>
    private void WriteLatest()
    {
        lock (_ioLock)
        {
            lock (_lock)
                if (_closed || _changeSeq <= _writtenSeq)
                    return;

            ArchiveCheckpoints();
            string json;
            long seq;
            lock (_lock)
            {
                seq = _changeSeq;
                try
                {
                    json = ProfileJson.Serialize(_profile);
                }
                catch (Exception e)
                {
                    _lastSaveError = e;
                    return;
                }
            }
            try
            {
                _store.SaveSerialized(json);
                _writtenSeq = seq;
                _lastSaveError = null;
            }
            catch (Exception e)
            {
                _lastSaveError = e;
            }
        }
    }
}
