using System.Collections.ObjectModel;

namespace RimworldStatistics.Core;

/// <summary>
/// The game-recorder chart series parsed from saves, kept so that opening the same chart of the same save again does not
/// parse its XML again (#305).
/// <para>
/// An entry is keyed by the SHA-256 of the save's bytes it was parsed from, the recorder, the running-gain mode and the
/// reader version, and is stored only after a parse that read every byte and matched that hash. A hit still reads the
/// file and checks its hash: a file can change without a new name, size or time, so only bytes with the same hash may
/// get the stored series (which a parse of them would give again: it depends on nothing else). A file that no longer
/// matches gets nothing, exactly as before. Nothing needs invalidating when a save is imported or changed, or a profile is
/// switched or restored: a series belongs to bytes, not to a profile.
/// </para>
/// <para>
/// The cache is bounded by its number of entries and of points, the least recently used entry going first; it lives as
/// long as the game process. Requests for the same entry at the same time share one parse. A failed, cut-short or
/// mismatching read is never stored. A request whose dialog closed meanwhile still finishes and stores its series; the
/// dialog decides, as before, whether to show it.
/// </para>
/// <para>
/// Measured on 55 real saves (34 colonies, 122 recorder charts, 19,076 points): a series takes about 190 bytes per point
/// kept, so the shared cache holds at most about 6 MB. Opening a chart again costs one read and hash of the save instead
/// of an XML parse (about 22 ms instead of 95 ms for a 34 MB save).
/// </para>
/// </summary>
public sealed class RecorderChartCache
{
    /// <summary>
    /// The cache every profile session uses unless it is given another. Its limits follow the memory-use setting
    /// (<see cref="SetLimits"/>); the standard ones are <see cref="ResourcePolicy.StandardCaches"/>.
    /// </summary>
    public static RecorderChartCache Shared { get; } =
        new(ResourcePolicy.StandardCaches.ChartSeries, ResourcePolicy.StandardCaches.ChartPoints);

    private readonly record struct Key(string Hash, string Recorder, bool RunningGain, int ReaderVersion);

    private int _maxEntries;   // under _gate
    private int _maxPoints;    // under _gate
    private readonly object _gate = new();
    // Most recently used first. A series is shared by every request for it, read only (its points are immutable records).
    private readonly LinkedList<(Key Key, ReadOnlyCollection<MetricHistoryPoint> Points)> _order = new();
    private readonly Dictionary<Key, LinkedListNode<(Key Key, ReadOnlyCollection<MetricHistoryPoint> Points)>> _entries = new();
    private readonly Dictionary<Key, TaskCompletionSource> _parsing = new();
    private long _points;
    private int _parses;

    public RecorderChartCache(int maxEntries, int maxPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPoints);
        _maxEntries = maxEntries;
        _maxPoints = maxPoints;
    }

    public int MaxEntries
    {
        get
        {
            lock (_gate)
                return _maxEntries;
        }
    }

    public int MaxPoints
    {
        get
        {
            lock (_gate)
                return _maxPoints;
        }
    }

    /// <summary>
    /// Changes the bounds (the memory-use setting). Lower bounds drop the least recently used entries at once; what is kept
    /// is unchanged, so a chart shows the same series whatever the bounds, only parsed again more or less often.
    /// </summary>
    public void SetLimits(int maxEntries, int maxPoints)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPoints);
        lock (_gate)
        {
            _maxEntries = maxEntries;
            _maxPoints = maxPoints;
            TrimLocked();
        }
    }

    /// <summary>XML parses this cache has run (a hit runs none).</summary>
    public int Parses => Volatile.Read(ref _parses);

    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    public long PointCount
    {
        get
        {
            lock (_gate)
                return _points;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _order.Clear();
            _entries.Clear();
            _points = 0;
        }
    }

    /// <summary>
    /// The series of <paramref name="recorder"/> in the save at <paramref name="path"/>, which must be the file
    /// <paramref name="file"/> with the bytes of <paramref name="expectedHash"/>; empty otherwise.
    /// </summary>
    public IReadOnlyList<MetricHistoryPoint> Read(string path, MetricFileAlias file, string expectedHash, string recorder, bool runningGain)
    {
        var key = new Key(expectedHash, recorder, runningGain, SaveReader.Version);
        while (true)
        {
            ReadOnlyCollection<MetricHistoryPoint>? cached = null;
            TaskCompletionSource? running = null;
            TaskCompletionSource? mine = null;
            lock (_gate)
            {
                if (_entries.TryGetValue(key, out var node))
                {
                    _order.Remove(node);
                    _order.AddFirst(node);
                    cached = node.Value.Points;
                }
                else if (!_parsing.TryGetValue(key, out running))
                {
                    mine = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _parsing[key] = mine;
                }
            }
            if (cached is not null)
                return SaveReader.HasBytes(path, file, expectedHash) ? cached : [];
            if (running is not null)
            {
                // The same chart is being parsed for another request: wait, then use what it stored (or parse if it stored
                // nothing, for example because its read failed).
                running.Task.Wait();
                continue;
            }

            try
            {
                Interlocked.Increment(ref _parses);
                var (verified, points) = SaveReader.ReadRecorderChartVerified(path, file, expectedHash, recorder, runningGain);
                if (!verified)
                    return points;
                var shared = Array.AsReadOnly(points as MetricHistoryPoint[] ?? points.ToArray());
                Store(key, shared);
                return shared;
            }
            finally
            {
                lock (_gate)
                    _parsing.Remove(key);
                mine!.SetResult();
            }
        }
    }

    private void Store(Key key, ReadOnlyCollection<MetricHistoryPoint> points)
    {
        lock (_gate)
        {
            if (points.Count > _maxPoints || _maxEntries == 0 || _entries.ContainsKey(key))
                return;
            _entries[key] = _order.AddFirst((key, points));
            _points += points.Count;
            TrimLocked();
        }
    }

    // Under _gate.
    private void TrimLocked()
    {
        while (_entries.Count > _maxEntries || _points > _maxPoints)
        {
            var last = _order.Last!;
            _order.RemoveLast();
            _entries.Remove(last.Value.Key);
            _points -= last.Value.Points.Count;
        }
    }
}
