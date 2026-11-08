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
    private readonly object _lock = new();
    private readonly object _ioLock = new();
    private readonly Dictionary<(bool IncludeDevMode, string Visible), StatsView> _views = new();

    private long _changeSeq;     // bumped by every change (under _lock)
    private long _writtenSeq;    // newest change seq that reached the file (under _ioLock)
    private bool _backgroundRunning;
    private bool _backgroundPending;
    private Task? _backgroundTask;
    private volatile Exception? _lastSaveError;

    public ProfileSession(ProfileStore store, Func<DateTime> utcNow)
    {
        _store = store;
        LoadResult = store.Load(utcNow());
        _profile = LoadResult.Profile;
    }

    public ProfileLoadResult LoadResult { get; }

    public Exception? LastSaveError => _lastSaveError;

    public void Mutate(Action<Profile> change, SaveMode mode)
    {
        lock (_lock)
        {
            try
            {
                change(_profile);
            }
            finally
            {
                _views.Clear();
                _changeSeq++;
            }
        }
        Save(mode);
    }

    public T Read<T>(Func<Profile, T> read)
    {
        lock (_lock)
            return read(_profile);
    }

    public StatsView? TryGetCachedView(StatsOptions options, IReadOnlyCollection<string>? visibleUnitIds = null)
    {
        if (!Monitor.TryEnter(_lock))
            return null;
        try
        {
            return _views.GetValueOrDefault((options.IncludeDevMode, VisibilityKey(visibleUnitIds)));
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    public bool TryRead<T>(Func<Profile, T> read, out T result)
    {
        if (!Monitor.TryEnter(_lock))
        {
            result = default!;
            return false;
        }
        try
        {
            result = read(_profile);
            return true;
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    /// <summary>Colonies and statistics, cached per option set until the next change.</summary>
    public StatsView GetView(StatsOptions options, IReadOnlyCollection<string>? visibleUnitIds = null)
    {
        bool ledgerChanged;
        StatsView view;
        lock (_lock)
        {
            var cacheKey = (options.IncludeDevMode, VisibilityKey(visibleUnitIds));
            if (_views.TryGetValue(cacheKey, out var cached))
                return cached;
            var before = LedgerSignature();
            var colonies = CampaignMatcher.BuildColonies(_profile);
            var stats = StatsCalculator.Calculate(colonies, options);
            view = new StatsView
            {
                Colonies = colonies,
                Stats = stats,
                MissingObservations = _profile.SaveObservations.Count(o => !o.Superseded && o.Status != ObservationStatus.Ok),
                Numeric = MetricCalculator.BuildView(_profile, colonies, stats,
                    MetricDisplayPolicy.SelectDefinitions(MetricRegistry.Catalog,
                        visibleUnitIds is null ? new HashSet<string>(StringComparer.Ordinal)
                            : new HashSet<string>(visibleUnitIds, StringComparer.Ordinal))),
            };
            ledgerChanged = LedgerSignature() != before;
            if (ledgerChanged)
            {
                _views.Clear();   // other option sets were built before the ledger changed; keep them consistent
                _changeSeq++;
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

    /// <summary>Serializes the current profile and writes it, unless the file already holds this or a newer snapshot.</summary>
    private void WriteLatest()
    {
        lock (_ioLock)
        {
            string json;
            long seq;
            lock (_lock)
            {
                if (_changeSeq <= _writtenSeq)
                    return;
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
