using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
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

    /// <summary>Starting biome defNames by colony key resolved from <see cref="ColonyStartSnapshot.Biome"/> observations.</summary>
    public IReadOnlyDictionary<string, string> StartingBiomes { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
/// The one in-memory profile, shared by the UI, the import thread and the game hooks (spec §6.3).
/// Changes and reads run under one lock; JSON is produced under the lock, and the file is written outside it.
/// The game thread records saves with <see cref="MutateWithoutBlocking"/>, which never waits for that lock: a change that
/// finds it held is queued and applied, in order, before any later read, change or write.
/// Writes are ordered: a write never replaces a newer snapshot with an older one. Save errors are kept, never thrown.
/// </summary>
public sealed class ProfileSession
{
    private readonly ProfileStore _store;
    private readonly Profile _profile;
    private readonly ICheckpointArchive? _archive;
    private readonly RecorderChartCache _recorderCharts;
    private readonly object _lock = new();
    private readonly object _ioLock = new();
    private readonly object _preemptGate = new();
    private CancellationTokenSource _preemptCts = new();
    private int _cancellableViews;

    private readonly record struct ViewCacheKey(bool IncludeDevMode, string Visible, string Scope);
    private readonly record struct ColonyReportKey(long Revision, long ArchiveGeneration, StatsView View, string ColonyKey, bool IncludeDevMode);

    // The active scope and up to four comparison columns must coexist with the all-colony view.
    // LRU eviction also avoids repeatedly rebuilding recently requested groups when users switch scopes.
    private int _maxCachedViews = ResourcePolicy.StandardCaches.Views;   // set without _lock (volatile), applied under it
    private readonly LinkedList<(ViewCacheKey Key, StatsView View)> _viewOrder = new();
    private readonly Dictionary<ViewCacheKey, LinkedListNode<(ViewCacheKey Key, StatsView View)>> _views = new();
    private readonly ConditionalWeakTable<FrozenSet<string>, string> _visibilityKeys = new();

    // Colony summaries and chronicles already built for a view (the memory-use and precomputation settings; none kept by
    // default). An entry is used only for the same view object, colony and option while neither the profile nor the
    // archived checkpoint data changed since it was built from them, so it is exactly what building it again would give.
    private int _maxColonyReports;                        // set without _lock (volatile), applied under it
    private int _trimPending;                             // 1: a bound was lowered and not yet applied under _lock
    private readonly LinkedList<ColonyReportEntry> _colonyReports = new();   // under _lock, most recently used last
    private readonly Dictionary<ColonyReportKey, LinkedListNode<ColonyReportEntry>> _colonyReportsByKey = new();
    private readonly Dictionary<StatsView, List<LinkedListNode<ColonyReportEntry>>> _colonyReportsByView = new(ReferenceEqualityComparer.Instance);
    // Under _lock: bumped before the archive files change and again when archived data replaces inline data.
    private long _archiveGeneration;

    private sealed record ColonyReportEntry(long Revision, long ArchiveGeneration, StatsView View, string ColonyKey,
        bool IncludeDevMode, ColonySummaryReport? Report, ColonyChronicle? Chronicle);

    private long _changeSeq;     // bumped by every change (under _lock)
    private long _writtenSeq;    // newest change seq that reached the file (under _ioLock)
    private bool _closed;
    private bool _backgroundRunning;
    private bool _backgroundPending;
    private Task? _backgroundTask;
    private volatile Exception? _lastSaveError;
    private volatile Exception? _lastArchiveError;       // the last append to the archive failed: inline data not yet moved
    private volatile Exception? _lastArchiveReadError;   // the last read of archived metrics failed: the files are unchanged
    private volatile Exception? _lastQueuedError;

    // Changes from MutateWithoutBlocking that found the profile busy, in order. Applied under _lock before any other
    // use of the profile, so nothing ever sees a later change without an earlier one.
    private readonly ConcurrentQueue<Action<Profile>> _queued = new();
    private int _mutationDepth;   // changes running (under _lock, nested on one thread): queued changes wait for them

    /// <param name="recorderCharts">Where chart series parsed from saves are kept (#305); <see cref="RecorderChartCache.Shared"/> by default.</param>
    public ProfileSession(ProfileStore store, Func<DateTime> utcNow, ICheckpointArchive? archive = null,
        RecorderChartCache? recorderCharts = null)
    {
        _store = store;
        _recorderCharts = recorderCharts ?? RecorderChartCache.Shared;
        LoadResult = store.Load(utcNow());
        _profile = LoadResult.Profile;
        _archive = LoadResult.ReadOnly ? null : archive;
        RestoreCheckpointMetrics(initialLoad: true);
    }

    public ProfileLoadResult LoadResult { get; }

    /// <summary>
    /// The number of changes applied so far, read without the profile lock (a window polls it every frame). Changes queued by
    /// <see cref="MutateWithoutBlocking"/> count once they are applied, which every read of the profile does first.
    /// </summary>
    public long Revision => Interlocked.Read(ref _changeSeq);

    /// <summary>
    /// The most statistics views kept (one per option set and scope), at least one. Lowering it drops the least recently
    /// used views; a dropped view is only built again when asked for.
    /// </summary>
    public int ViewCacheCapacity
    {
        get => Volatile.Read(ref _maxCachedViews);
        set
        {
            Volatile.Write(ref _maxCachedViews, Math.Max(1, value));
            TrimCachesIfFree();
        }
    }

    /// <summary>The most colony summaries (with their chronicles) kept for reuse; 0, the default, keeps none.</summary>
    public int ColonyReportCacheCapacity
    {
        get => Volatile.Read(ref _maxColonyReports);
        set
        {
            Volatile.Write(ref _maxColonyReports, Math.Max(0, value));
            TrimCachesIfFree();
        }
    }

    /// <summary>
    /// Applies the processing-resource settings: the views and colony reports this session keeps and the bounds of its chart
    /// cache. Never waits for the profile (the settings window calls it on the main thread while a view may be building):
    /// if the profile is busy, the next use of it trims to the new bounds. Only what is kept changes, never what is shown.
    /// </summary>
    public void ApplyCacheLimits(CacheLimits limits)
    {
        Volatile.Write(ref _maxCachedViews, Math.Max(1, limits.Views));
        Volatile.Write(ref _maxColonyReports, Math.Max(0, limits.ColonyReports));
        _recorderCharts.SetLimits(Math.Max(0, limits.ChartSeries), Math.Max(0, limits.ChartPoints));
        TrimCachesIfFree();
    }

    /// <summary>Colony summaries kept for reuse now, within the current bound (tests and diagnostics).</summary>
    internal int CachedColonyReports
    {
        get
        {
            lock (_lock)
            {
                TrimIfPending();
                return _colonyReports.Count;
            }
        }
    }

    /// <summary>Statistics views kept now, within the current bound (tests and diagnostics).</summary>
    internal int CachedViews
    {
        get
        {
            lock (_lock)
            {
                TrimIfPending();
                return _views.Count;
            }
        }
    }

    // Lowered bounds are applied at once when the profile is free, else by whoever uses it next (ApplyQueuedAndRequestSave).
    private void TrimCachesIfFree()
    {
        Volatile.Write(ref _trimPending, 1);
        if (!Monitor.TryEnter(_lock))
            return;
        try
        {
            TrimIfPending();
        }
        finally
        {
            Monitor.Exit(_lock);
        }
    }

    // Under _lock.
    private void TrimIfPending()
    {
        if (Interlocked.Exchange(ref _trimPending, 0) == 0)
            return;
        TrimViews(0);
        TrimColonyReports();
    }

    // Under _lock. Drops the least recently used views until <paramref name="room"/> more fit, and the colony reports kept
    // for them, which would otherwise keep a dropped view in memory (a window still holding that view builds its report
    // again, as without the cache).
    private void TrimViews(int room)
    {
        var max = Math.Max(1, _maxCachedViews);
        while (_views.Count > 0 && _views.Count + room > max)
        {
            var oldest = _viewOrder.First!;
            _viewOrder.RemoveFirst();
            _views.Remove(oldest.Value.Key);
            DropColonyReportsForViewLocked(oldest.Value.View);
        }
    }

    // Under _lock.
    private void TrimColonyReports()
    {
        var max = Math.Max(0, _maxColonyReports);
        while (_colonyReports.Count > max)
            RemoveColonyReportNodeLocked(_colonyReports.First!);
    }

    // Under _lock.
    private void DropColonyReportsForViewLocked(StatsView view)
    {
        if (!_colonyReportsByView.Remove(view, out var nodes))
            return;
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            _colonyReports.Remove(node);
            var e = node.Value;
            _colonyReportsByKey.Remove(new ColonyReportKey(e.Revision, e.ArchiveGeneration, e.View, e.ColonyKey, e.IncludeDevMode));
        }
    }

    // Under _lock.
    private void RemoveColonyReportNodeLocked(LinkedListNode<ColonyReportEntry> node)
    {
        _colonyReports.Remove(node);
        var e = node.Value;
        _colonyReportsByKey.Remove(new ColonyReportKey(e.Revision, e.ArchiveGeneration, e.View, e.ColonyKey, e.IncludeDevMode));
        if (_colonyReportsByView.TryGetValue(e.View, out var nodes))
        {
            nodes.Remove(node);
            if (nodes.Count == 0)
                _colonyReportsByView.Remove(e.View);
        }
    }

    // Under _lock.
    private void ClearViewsLocked()
    {
        _views.Clear();
        _viewOrder.Clear();
        ClearColonyReportsLocked();
    }

    // Under _lock.
    private void ClearColonyReportsLocked()
    {
        _colonyReports.Clear();
        _colonyReportsByKey.Clear();
        _colonyReportsByView.Clear();
    }

    public Exception? LastSaveError => _lastSaveError;
    public Exception? LastArchiveError => _lastArchiveError ?? _lastArchiveReadError;

    /// <summary>The latest exception thrown by a change given to <see cref="MutateWithoutBlocking"/>.</summary>
    public Exception? LastQueuedMutationError => _lastQueuedError;

    public void ClearQueuedMutationError() => _lastQueuedError = null;

    public void Mutate(Action<Profile> change, SaveMode mode)
    {
        MutateWithRevision(change);
        Save(mode);
    }

    /// <summary>
    /// For a caller that must not wait for the profile (the game thread recording a save, Issue #195 / F-14): applies
    /// <paramref name="change"/> now and requests a background write when the profile is free. When another thread holds
    /// it (a background write serializing it, an import, a view being built), the change is queued and returns at once;
    /// it is applied, in order with every other change, before anything else reads or writes the profile, and a worker
    /// applies it as soon as the profile is released. The change must only use the profile and data it captured; an
    /// exception it throws is kept in <see cref="LastQueuedMutationError"/> and does not affect other changes.
    /// </summary>
    public void MutateWithoutBlocking(Action<Profile> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        _queued.Enqueue(change);
        if (Monitor.TryEnter(_lock))
        {
            var insideChange = false;
            try
            {
                insideChange = _mutationDepth > 0;   // called from a running change: it goes after that change
                if (!insideChange)
                    ApplyQueuedAndRequestSave();
            }
            finally
            {
                Monitor.Exit(_lock);
            }
            if (!insideChange)
                return;
        }
        ThreadPool.UnsafeQueueUserWorkItem(static session =>
        {
            lock (session._lock)
                session.ApplyQueuedAndRequestSave();
        }, this, preferLocal: false);
    }

    internal (long Before, long After) MutateWithRevision(Action<Profile> change)
    {
        PreemptCancellableViews();
        lock (_lock)
        {
            ApplyQueuedAndRequestSave();
            return MutateLocked(change);
        }
    }

    /// <summary>Under _lock, at every use of the profile: queued changes first, then a background write for them.</summary>
    private void ApplyQueuedAndRequestSave()
    {
        TrimIfPending();
        if (ApplyQueued())
            Save(SaveMode.Background);   // re-entrant lock: starts or flags the writer, never waits
    }

    // Under _lock.
    private (long Before, long After) MutateLocked(Action<Profile> change)
    {
        var before = _changeSeq;
        if (_closed)
            return (before, before);
        _mutationDepth++;
        try
        {
            change(_profile);
            RestoreCheckpointMetrics(initialLoad: false);
        }
        finally
        {
            _mutationDepth--;
            ClearViewsLocked();
            _changeSeq++;
        }
        return (before, _changeSeq);
    }

    /// <summary>Under _lock: applies queued changes in their order. False when there were none (or a change is running).</summary>
    private bool ApplyQueued()
    {
        if (_mutationDepth > 0 || _queued.IsEmpty)
            return false;
        var applied = false;
        while (_queued.TryDequeue(out var change))
        {
            applied = true;
            try
            {
                MutateLocked(change);
            }
            catch (Exception e)
            {
                _lastQueuedError = e;
            }
        }
        return applied;
    }

    internal void QueueSave() => Save(SaveMode.Background);

    private void RestoreCheckpointMetrics(bool initialLoad)
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

        if (_archive is null)
        {
            _profile.PendingCheckpointRestorations.Clear();
            return;
        }

        if (_profile.PendingCheckpointRestorations.Count > 0)
        {
            foreach (var target in _profile.PendingCheckpointRestorations)
                if (!target.IsQuarantined && target.ArchivedMetrics)
                    RestoreCheckpointMetrics(target);
            _profile.PendingCheckpointRestorations.Clear();
        }

        foreach (var observation in _profile.MetricObservations)
            if (!observation.IsQuarantined && observation.ArchivedMetrics && !observation.Compressed)
                RestoreCheckpointMetrics(observation);

        if (initialLoad)
            RestoreBranchFallbackParents();
    }

    private void RestoreBranchFallbackParents()
    {
        var hasCandidate = _profile.MetricObservations.Any(o => !o.IsQuarantined
            && o.Source == MetricObservationSource.Checkpoint && o.ArchivedMetrics && o.Compressed
            && !o.Conflicted && o.Identity.ModCampaignId is { Length: > 0 });
        if (!hasCandidate)
            return;

        var childrenByParent = _profile.MetricObservations
            .Where(o => !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint
                && o.ParentKey is not null && o.Identity.ModCampaignId is { Length: > 0 })
            .ToLookup(o => (o.Identity.ModCampaignId!, o.ParentKey!));
        HashSet<string>? lastCaptureKeys = null;

        foreach (var observation in _profile.MetricObservations)
        {
            if (observation.IsQuarantined || observation.Source != MetricObservationSource.Checkpoint
                || !observation.ArchivedMetrics || !observation.Compressed || observation.Conflicted
                || observation.Identity.ModCampaignId is not { Length: > 0 } campaign)
                continue;

            var hasConflictedChild = false;
            List<MetricObservation>? validChildren = null;
            foreach (var child in childrenByParent[(campaign, observation.Key)])
            {
                if (child.SaveTick <= observation.SaveTick)
                    continue;
                if (child.Conflicted)
                {
                    hasConflictedChild = true;
                    break;
                }
                (validChildren ??= new List<MetricObservation>()).Add(child);
            }

            if (hasConflictedChild)
            {
                RestoreCheckpointMetrics(observation);
                continue;
            }

            if (validChildren is not { Count: > 1 })
                continue;

            lastCaptureKeys ??= CheckpointArchivePlanner.LastCaptureCheckpointKeys(_profile);
            if (lastCaptureKeys.Count == 0)
                continue;

            var first = validChildren[0];
            var needsRestore = false;
            foreach (var key in lastCaptureKeys)
            {
                if (observation.Metrics.TryGetValue(key, out var parentEntry) && !parentEntry.IsQuarantined)
                    continue;
                var firstHas = first.Metrics.TryGetValue(key, out var fe) && !fe.IsQuarantined;
                for (var i = 1; i < validChildren.Count; i++)
                {
                    var otherHas = validChildren[i].Metrics.TryGetValue(key, out var oe) && !oe.IsQuarantined;
                    if (firstHas != otherHas || (firstHas && fe!.Revision != oe!.Revision))
                    {
                        needsRestore = true;
                        break;
                    }
                }
                if (needsRestore)
                    break;
            }

            if (needsRestore)
                RestoreCheckpointMetrics(observation);
        }
    }

    private void RestoreCheckpointMetrics(MetricObservation target)
    {
        if (!target.ArchivedMetrics || _archive is null || target.Identity.ModCampaignId is not { Length: > 0 } campaign)
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
            _lastArchiveReadError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            _lastArchiveReadError = e;
        }
    }

    public T Read<T>(Func<Profile, T> read)
    {
        PreemptCancellableViews();
        lock (_lock)
        {
            ApplyQueuedAndRequestSave();
            return read(_profile);
        }
    }

    public IReadOnlyList<HistoricalCounterInterval> ReadHistoricalCounterIntervals()
        => HistoricalCounterIntervals.ForDiagnostics(Read(HistoricalCounterIntervals.Capture));

    public ColonySaveLineage ReadColonySaveLineage(StatsView view, string colonyKey)
    {
        var captured = Read(profile => ColonySaveLineage.Capture(profile, view.Colonies, colonyKey));
        return captured is null ? ColonySaveLineage.Empty(colonyKey) : ColonySaveLineage.BuildFromCaptured(captured, _archive);
    }

    public ColonySummaryReport? ReadColonySummaryReport(StatsView view, string colonyKey)
    {
        var lineage = ReadColonySaveLineage(view, colonyKey);
        return ColonySummaryReport.Build(view, colonyKey, lineage);
    }

    public (ColonySummaryReport? Report, ColonyChronicle? Chronicle) ReadColonySummaryAndChronicle(
        StatsView view,
        string colonyKey,
        bool includeDevMode = false)
    {
        ColonySaveLineage.CapturedInputs? captured;
        ColonyChronicle.Evidence? evidence;
        long revision, archiveGeneration;
        lock (_lock)
        {
            ApplyQueuedAndRequestSave();
            revision = _changeSeq;
            archiveGeneration = _archiveGeneration;
            if (FindColonyReport(revision, archiveGeneration, view, colonyKey, includeDevMode) is { } kept)
                return (kept.Report, kept.Chronicle);
            captured = ColonySaveLineage.Capture(_profile, view.Colonies, colonyKey);
            evidence = ColonyChronicle.Capture(view, colonyKey);
        }
        var lineage = captured is null ? ColonySaveLineage.Empty(colonyKey) : ColonySaveLineage.BuildFromCaptured(captured, _archive);
        var report = ColonySummaryReport.Build(view, colonyKey, lineage);
        var chronicle = evidence is null ? null : ColonyChronicle.Build(view, evidence, lineage, null, includeDevMode);
        lock (_lock)
        {
            // Kept only if nothing it was built from changed meanwhile: a later call would build exactly this again.
            if (_maxColonyReports > 0 && _changeSeq == revision && _archiveGeneration == archiveGeneration
                && FindColonyReport(revision, archiveGeneration, view, colonyKey, includeDevMode) is null)
            {
                var entry = new ColonyReportEntry(revision, archiveGeneration, view, colonyKey, includeDevMode, report, chronicle);
                var node = _colonyReports.AddLast(entry);
                _colonyReportsByKey[new ColonyReportKey(revision, archiveGeneration, view, colonyKey, includeDevMode)] = node;
                if (!_colonyReportsByView.TryGetValue(view, out var nodes))
                {
                    nodes = new List<LinkedListNode<ColonyReportEntry>>();
                    _colonyReportsByView[view] = nodes;
                }
                nodes.Add(node);
                TrimColonyReports();
            }
        }
        return (report, chronicle);
    }

    // Under _lock. A hit becomes the most recently used entry.
    private ColonyReportEntry? FindColonyReport(long revision, long archiveGeneration, StatsView view, string colonyKey,
        bool includeDevMode)
    {
        var key = new ColonyReportKey(revision, archiveGeneration, view, colonyKey, includeDevMode);
        if (!_colonyReportsByKey.TryGetValue(key, out var node))
            return null;
        _colonyReports.Remove(node);
        _colonyReports.AddLast(node);
        return node.Value;
    }

    /// <summary>
    /// Builds all recorded per-colony time-series histories for in-scope non-excluded colonies in <paramref name="view"/>,
    /// restoring archived checkpoint points in a single pass per campaign outside the profile lock.
    /// </summary>
    public IReadOnlyList<ColonyMetricHistorySeries> ReadExportHistories(StatsView view)
    {
        var series = Read(profile => MetricCalculator.BuildColonyChartHistoriesCore(profile, view));
        return MetricCalculator.RestoreExportHistories(series, _archive);
    }

    /// <summary>
    /// The evidence-based chronicle of one colony (V2-A) on <paramref name="branch"/> (the representative line when null or
    /// not found). The lineage inputs and the colony's ending evidence are copied together under the lock.
    /// </summary>
    public ColonyChronicle? ReadColonyChronicle(
        StatsView view,
        string colonyKey,
        ChronicleBranchKey? branch = null,
        bool includeDevMode = false)
    {
        var (captured, evidence) = Read(profile =>
            (ColonySaveLineage.Capture(profile, view.Colonies, colonyKey), ColonyChronicle.Capture(view, colonyKey)));
        if (evidence is null)
            return null;
        var lineage = captured is null ? ColonySaveLineage.Empty(colonyKey) : ColonySaveLineage.BuildFromCaptured(captured, _archive);
        return ColonyChronicle.Build(view, evidence, lineage, branch, includeDevMode);
    }

    public MetricHistory ReadChartHistory(StatsView view, string colonyKey, MetricDefinition definition, string savesDirectory)
    {
        var history = Read(profile => MetricCalculator.BuildChartHistory(profile, view.Colonies, colonyKey, definition));
        if (_archive is not null)
            history = MetricCalculator.RestoreChartHistory(history, definition, _archive);
        if (history.RecorderHash is not { } hash || MetricCalculator.ChartRecorder(definition.Key) is not { } recorder)
            return history;

        foreach (var file in history.RecorderFiles)
        {
            if (Path.GetFileName(file.FileName) != file.FileName || !ImportEngine.IsSaveFile(file.FileName))
                continue;
            var runningGain = definition.Key == MetricKeys.WealthTotalMaxGain15D;
            var points = _recorderCharts.Read(Path.Combine(savesDirectory, file.FileName), file, hash, recorder, runningGain);
            if (points.Count > 0 && (!runningGain
                || points.LastOrDefault(p => p.Value is not null)?.Value?.Number == history.RecorderMaximum))
                return new MetricHistory(runningGain ? MetricHistoryKind.WealthRecorder : MetricHistoryKind.GameRecorder, points);
        }
        return history;
    }

    /// <summary>
    /// A profile read with the exact mutation revision under the same lock.
    /// A scan may cache an index while the revision is stable and use mutation
    /// receipts to identify its own edits without missing concurrent changes.
    /// </summary>
    internal T ReadWithRevision<T>(Func<Profile, long, T> read)
    {
        PreemptCancellableViews();
        lock (_lock)
        {
            ApplyQueuedAndRequestSave();
            return read(_profile, _changeSeq);
        }
    }

    public StatsView? TryGetCachedView(StatsOptions options, IReadOnlyCollection<string>? visibleUnitIds = null, ViewScope? scope = null)
    {
        if (!_queued.IsEmpty)
            return null;
        if (!Monitor.TryEnter(_lock))
            return null;
        try
        {
            if (!_queued.IsEmpty)
                return null;
            TrimIfPending();
            var key = new ViewCacheKey(options.IncludeDevMode, VisibilityKeyFor(visibleUnitIds), scope?.CacheKey ?? "");
            if (!_views.TryGetValue(key, out var node))
                return null;
            _viewOrder.Remove(node);
            _viewOrder.AddLast(node);
            return node.Value.View;
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
            ApplyQueuedAndRequestSave();
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
        => GetViewCancellable(options, visibleUnitIds, scope, CancellationToken.None);

    /// <summary>Colonies and statistics, cached per option set until the next change.</summary>
    internal StatsView GetViewCancellable(StatsOptions options, IReadOnlyCollection<string>? visibleUnitIds, ViewScope? scope, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CancellationTokenSource? linkedCts = null;
        var registeredCancellable = false;
        if (ct.CanBeCanceled)
        {
            lock (_preemptGate)
            {
                _cancellableViews++;
                registeredCancellable = true;
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _preemptCts.Token);
            }
        }
        else
        {
            PreemptCancellableViews();
        }

        var token = linkedCts?.Token ?? ct;
        var ledgerChanged = false;
        StatsView view;
        try
        {
            try
            {
                lock (_lock)
                {
                    token.ThrowIfCancellationRequested();
                    ApplyQueuedAndRequestSave();
                    var cacheKey = new ViewCacheKey(options.IncludeDevMode, VisibilityKeyFor(visibleUnitIds), scope?.CacheKey ?? "");
                    if (_views.TryGetValue(cacheKey, out var cached))
                    {
                        _viewOrder.Remove(cached);
                        _viewOrder.AddLast(cached);
                        return cached.Value.View;
                    }
                    token.ThrowIfCancellationRequested();
                    var before = LedgerSignature();
                    try
                    {
                        var colonies = CampaignMatcher.BuildColoniesCancellable(_profile, token);
                        var attached = MetricCalculator.Attach(_profile, colonies, out var unattributed);
                        var startingBiomes = SmartGroupEvaluator.ResolveStartingBiomesByKey(attached, colonies);
                        token.ThrowIfCancellationRequested();
                        scope?.Apply(_profile, colonies, options, startingBiomes);
                        var stats = StatsCalculator.CalculateCancellable(colonies, options, token);
                        var missing = 0;
                        foreach (var o in _profile.SaveObservations)
                        {
                            if (!o.Superseded && o.Status != ObservationStatus.Ok)
                                missing++;
                        }
                        view = new StatsView
                        {
                            Colonies = colonies,
                            Stats = stats,
                            MissingObservations = missing,
                            Numeric = MetricCalculator.BuildViewWithAttached(_profile, colonies, stats,
                                MetricRegistry.Compose(_profile.ModMetricDescriptors,
                                    visibleUnitIds is null ? new HashSet<string>(StringComparer.Ordinal)
                                        : new HashSet<string>(visibleUnitIds, StringComparer.Ordinal)),
                                attached, unattributed, token),
                            StartingBiomes = startingBiomes,
                        };
                    }
                    finally
                    {
                        if (LedgerSignature() != before)
                        {
                            ledgerChanged = true;
                            ClearViewsLocked();   // other option sets were built before the ledger changed; keep them consistent
                            _changeSeq++;
                        }
                    }
                    // Keep a complete comparison working set; evict only the least recently accessed views.
                    TrimViews(1);
                    _views[cacheKey] = _viewOrder.AddLast((cacheKey, view));
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested && token.IsCancellationRequested)
            {
                // Preempted by a foreground caller waiting on _lock: restart cleanly outside if needed, or retry below if normal thread
                throw new OperationCanceledException(token);
            }
            finally
            {
                if (ledgerChanged)
                    Save(SaveMode.Background);   // new colony keys must survive a restart (spec §6.2)
            }
            return view;
        }
        finally
        {
            linkedCts?.Dispose();
            if (registeredCancellable)
            {
                lock (_preemptGate)
                    _cancellableViews--;
            }
        }
    }

    private void PreemptCancellableViews()
    {
        if (Volatile.Read(ref _cancellableViews) == 0)
            return;
        CancellationTokenSource toCancel;
        lock (_preemptGate)
        {
            if (_cancellableViews == 0)
                return;
            toCancel = _preemptCts;
            _preemptCts = new CancellationTokenSource();
        }
        toCancel.Cancel();
    }

    /// <summary>
    /// Under _lock. <see cref="VisibilityKey"/>, remembered per immutable <see cref="FrozenSet{T}"/> so concurrent
    /// precomputation and UI scopes never evict each other's cached key. Any other collection is keyed afresh.
    /// </summary>
    private string VisibilityKeyFor(IReadOnlyCollection<string>? unitIds)
    {
        if (unitIds is null || unitIds.Count == 0)
            return "";
        if (unitIds is FrozenSet<string> frozen)
            return _visibilityKeys.GetValue(frozen, static s => VisibilityKey(s));
        return VisibilityKey(unitIds);
    }

    internal static string VisibilityKey(IReadOnlyCollection<string>? unitIds)
        => unitIds is null || unitIds.Count == 0 ? "" :
            string.Join("\u001f", unitIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    /// <summary>Entries are only appended and their pawn unions only grow, so this changes exactly when the ledger does.</summary>
    private (int, long) LedgerSignature()
    {
        long pawns = 0;
        foreach (var k in _profile.ColonyKeys)
            pawns += k.StartingPawnIds.Count;
        return (_profile.ColonyKeys.Count, pawns);
    }

    /// <summary>Waits for any currently scheduled background write without starting a new write.</summary>
    internal void WaitForBackgroundWrite()
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
    }

    /// <summary>Waits for a running background write, then writes anything not yet on disk.</summary>
    public void Flush()
    {
        WaitForBackgroundWrite();
        WriteLatest();
    }

    /// <summary>
    /// Runs <paramref name="action"/> (a backup) with everything on disk. A checkpoint archive append that failed earlier is
    /// tried again first, even when the profile has not changed since (#315), so fixing the archive folder is enough.
    /// </summary>
    public T WithWritesPaused<T>(Func<T> action)
    {
        lock (_ioLock)
        {
            WriteLatest(retryArchive: true);
            EnsureFlushed();
            return action();
        }
    }

    public void Close()
    {
        PreemptCancellableViews();
        lock (_ioLock)
        lock (_lock)
        {
            if (_closed)
                return;
            WriteLatest(retryArchive: true);   // a restore closes the session first (#315)
            EnsureFlushed();
            _closed = true;
        }
    }

    /// <summary>
    /// The profile and the archive append it depends on reached the disk. A failed read of archived metrics leaves both files
    /// as they were, so it does not block a backup (it stays in <see cref="LastArchiveError"/>).
    /// </summary>
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
            var serialized = new List<(ArchiveStep Step, string Json)>();
            lock (_lock)
            {
                ApplyQueuedAndRequestSave();
                plannedRevision = _changeSeq;
                steps = CheckpointArchivePlanner.Plan(_profile);
                foreach (var step in steps)
                {
                    var campaign = step.Observation.Identity.ModCampaignId!;
                    if (!batches.TryGetValue(campaign, out var lines))
                        batches.Add(campaign, lines = new List<string>());
                    var json = JsonSerializer.Serialize(step.Observation, ProfileJson.CompactOptions);
                    lines.Add(json);
                    serialized.Add((step, json));
                }
                // The archive files change from here on: a colony report built before or while they do is not kept.
                if (steps.Count > 0)
                    _archiveGeneration++;
            }

            foreach (var (campaign, lines) in batches)
                _archive.Append(campaign, lines);

            lock (_lock)
            {
                // A mutation can replace a breakdown in place without changing its
                // entry reference or size. Only trim the revision written to disk.
                if (_changeSeq == plannedRevision)
                {
                    foreach (var step in steps)
                        CheckpointArchivePlanner.Apply(step);
                    if (steps.Count > 0)
                        _archiveGeneration++;
                }
                else
                {
                    // An unrelated import mutation must not leave the same ancestor
                    // untrimmed and repeatedly appended. Recheck BOTH the archived
                    // parent bytes and current child proof before applying any drop.
                    // Changed parent payloads and invalidated child proofs stay intact.
                    var current = CheckpointArchivePlanner.Plan(_profile);
                    foreach (var (step, json) in serialized)
                    {
                        var stillPlanned = current.FirstOrDefault(candidate =>
                            ReferenceEquals(candidate.Observation, step.Observation));
                        if (stillPlanned is null || !step.Drops.All(drop => stillPlanned.Drops.Any(now =>
                                now.Key == drop.Key && ReferenceEquals(now.Entry, drop.Entry)
                                && now.ValueCount == drop.ValueCount)))
                            continue;
                        if (step.DropEvidenceUnits.Any(u => !stillPlanned.DropEvidenceUnits.Contains(u, StringComparer.Ordinal))
                            || step.DropProviderIds.Any(p => !stillPlanned.DropProviderIds.Contains(p, StringComparer.Ordinal)))
                            continue;
                        if (!string.Equals(JsonSerializer.Serialize(step.Observation, ProfileJson.CompactOptions),
                                json, StringComparison.Ordinal))
                            continue;
                        CheckpointArchivePlanner.Apply(step);
                        _archiveGeneration++;
                    }
                }
            }

            // Every planned append succeeded, or nothing is left to move: no inline data waits for the archive.
            _lastArchiveError = null;
        }
        catch (Exception e)
        {
            _lastArchiveError = e;
        }
    }

    /// <summary>
    /// Serializes the current profile and writes it, unless the file already holds this or a newer snapshot. With
    /// <paramref name="retryArchive"/> (a backup or a restore) an archive append that failed is retried and the trimmed
    /// profile written even when nothing changed since: the failure was the archive's, not the profile's (#315).
    /// </summary>
    private void WriteLatest(bool retryArchive = false)
    {
        lock (_ioLock)
        {
            lock (_lock)
            {
                ApplyQueuedAndRequestSave();
                if (_closed || _changeSeq <= _writtenSeq && !(retryArchive && _archive is not null && _lastArchiveError is not null))
                    return;
            }

            long seq = 0;
            try
            {
                _store.SaveCanonical(output =>
                {
                    ArchiveCheckpoints();
                    lock (_lock)
                    {
                        ApplyQueuedAndRequestSave();
                        seq = _changeSeq;
                        ProfileJson.SerializeTo(output, _profile);   // into the store's reused buffer (Issue #195)
                    }
                });
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
