using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>Issue #305: a chart read from a save is parsed once, and only bytes with the same hash get it again.</summary>
public sealed class RecorderChartCacheTests
{
    private const string Wealth = HistoryMetricReader.WealthTotal;

    /// <summary>A save whose wealth recorder holds <paramref name="points"/> readings (one per 30,000 ticks), the last one <paramref name="last"/>.</summary>
    private static SaveReadResult WriteSave(TempDir dir, string name, int points, float last = 200f)
    {
        var wealth = Enumerable.Repeat(100f, points).ToArray();
        wealth[^1] = last;
        var path = dir.File(name);
        File.WriteAllBytes(path, new SaveXmlBuilder().Minimal().Tick(ticksGame: (points - 1) * HistoryMetricReader.TicksPerPoint + 1, start: 0)
            .WealthTotalDeflate(wealth).Build());
        var save = new SaveReader().ReadDetailed(path);
        Assert.Equal(ObservationStatus.Ok, save.Observation.Status);
        return save;
    }

    private static MetricFileAlias Alias(SaveReadResult save) => new()
    {
        FileName = save.Observation.FileName, FileSize = save.Observation.FileSize, LastWriteUtc = save.Observation.LastWriteUtc,
    };

    private static IReadOnlyList<MetricHistoryPoint> Read(RecorderChartCache cache, TempDir dir, SaveReadResult save,
        string recorder = Wealth, string? hash = null)
        => cache.Read(dir.File(save.Observation.FileName), Alias(save), hash ?? save.Observation.ContentHash!, recorder, runningGain: false);

    private static ProfileSession Session(TempDir store, SaveReadResult save, RecorderChartCache cache)
    {
        var session = new ProfileSession(new ProfileStore(store.Path), () => DateTime.UtcNow, recorderCharts: cache);
        session.Mutate(profile =>
        {
            profile.SaveObservations.Add(save.Observation);
            profile.MetricObservations.Add(save.Metrics!);
        }, SaveMode.None);
        return session;
    }

    private static MetricHistory Chart(ProfileSession session, string key, string saves)
    {
        var view = session.GetView(new StatsOptions());
        return session.ReadChartHistory(view, Assert.Single(view.Colonies).Key, MetricRegistry.Find(key)!, saves);
    }

    /// <summary>What a chart shows, compared by value (the points hold values of a class without value equality).</summary>
    private static string Shown(MetricHistory history) => history.Kind + ":" + string.Join(";",
        history.Points.Select(p => $"{p.Tick},{p.Value?.Number},{p.Value?.Coverage},{p.ConnectToPrevious},{p.ContinuesFrom}"));

    [Fact]
    public void OpeningTheSameChartAgain_ParsesTheSaveOnceAndShowsWhatAParseShows()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        using var plainStore = new TempDir();
        var save = WriteSave(saves, "a.rws", 34);
        var cache = new RecorderChartCache(maxEntries: 16, maxPoints: 1000);
        var session = Session(store, save, cache);
        var uncached = Session(plainStore, save, new RecorderChartCache(maxEntries: 0, maxPoints: 0));

        foreach (var (key, parses) in new[] { (MetricKeys.WealthTotalMean, 1), (MetricKeys.WealthTotalMaxGain15D, 2) })
        {
            var first = Chart(session, key, saves.Path);
            var again = Chart(session, key, saves.Path);
            Assert.Equal(parses, cache.Parses);   // the running gain is another series of the same recorder
            Assert.Contains(first.Kind, new[] { MetricHistoryKind.GameRecorder, MetricHistoryKind.WealthRecorder });
            Assert.Equal(Shown(Chart(uncached, key, saves.Path)), Shown(first));
            Assert.Equal(Shown(first), Shown(again));
            Assert.True(((ICollection<MetricHistoryPoint>)again.Points).IsReadOnly);   // shared, so nobody may change it
        }
        Assert.Equal((2, 34L + 4), (cache.Count, cache.PointCount));
    }

    [Fact]
    public void AChangedMissingOrCutSave_NeverGetsTheStoredSeries()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var save = WriteSave(saves, "a.rws", 34);
        var path = saves.File("a.rws");
        var original = File.ReadAllBytes(path);
        var cache = new RecorderChartCache(maxEntries: 16, maxPoints: 1000);
        var session = Session(store, save, cache);
        var recorder = Shown(Chart(session, MetricKeys.WealthTotalMean, saves.Path));
        Assert.StartsWith(nameof(MetricHistoryKind.GameRecorder), recorder, StringComparison.Ordinal);

        void Put(byte[] bytes)
        {
            File.WriteAllBytes(path, bytes);
            File.SetLastWriteTimeUtc(path, save.Observation.LastWriteUtc);
        }

        // Other bytes under the same name, size and time; a save cut short; no save: each falls back to the profile's points.
        var changed = (byte[])original.Clone();
        changed[^1] = (byte)' ';
        foreach (var replace in new Action[] { () => Put(changed), () => Put(original[..^1]), () => File.Delete(path) })
        {
            replace();
            Assert.Equal(MetricHistoryKind.SaveObservations, Chart(session, MetricKeys.WealthTotalMean, saves.Path).Kind);
            Put(original);
            Assert.Equal(recorder, Shown(Chart(session, MetricKeys.WealthTotalMean, saves.Path)));
        }
        Assert.Equal(1, cache.Parses);   // the stored series served the original bytes each time; nothing was parsed again
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void AnUnprovenReadIsNotStored_ButAnEmptyOrOnePointSeriesOfProvenBytesIs()
    {
        using var saves = new TempDir();
        var many = WriteSave(saves, "many.rws", 34);
        var one = WriteSave(saves, "one.rws", 1);
        var cache = new RecorderChartCache(maxEntries: 16, maxPoints: 1000);

        var wrongHash = new string('0', 64);
        Assert.Empty(Read(cache, saves, many, hash: wrongHash));
        Assert.Empty(Read(cache, saves, many, hash: wrongHash));
        Assert.Equal((2, 0), (cache.Parses, cache.Count));   // a mismatch is parsed again next time: it may be a torn read

        Assert.Empty(Read(cache, saves, many, HistoryMetricReader.ColonistMood));   // this save has no mood recorder
        Assert.Empty(Read(cache, saves, many, HistoryMetricReader.ColonistMood));
        Assert.Single(Read(cache, saves, one));
        Assert.Single(Read(cache, saves, one));
        Assert.Equal(34, Read(cache, saves, many).Count);
        Assert.Equal(34, Read(cache, saves, many).Count);
        Assert.Equal((5, 3, 35L), (cache.Parses, cache.Count, cache.PointCount));
    }

    [Fact]
    public void TheCacheDropsTheLeastRecentlyUsedSeriesToStayWithinItsBounds()
    {
        using var saves = new TempDir();
        var a = WriteSave(saves, "a.rws", 34);
        var b = WriteSave(saves, "b.rws", 34, last: 300f);
        var c = WriteSave(saves, "c.rws", 34, last: 400f);
        var large = WriteSave(saves, "large.rws", 67);

        var byEntries = new RecorderChartCache(maxEntries: 2, maxPoints: 1000);
        Read(byEntries, saves, a);
        Read(byEntries, saves, b);
        Read(byEntries, saves, a);   // a is now the most recently used
        Read(byEntries, saves, c);   // b goes
        Assert.Equal((3, 2), (byEntries.Parses, byEntries.Count));
        Read(byEntries, saves, a);
        Assert.Equal(3, byEntries.Parses);
        Assert.Equal(300d, Read(byEntries, saves, b)[^1].Value!.Number);
        Assert.Equal((4, 2, 68L), (byEntries.Parses, byEntries.Count, byEntries.PointCount));

        var byPoints = new RecorderChartCache(maxEntries: 16, maxPoints: 50);
        Read(byPoints, saves, a);
        Read(byPoints, saves, b);    // 68 points would exceed 50: a goes
        Assert.Equal((1, 34L), (byPoints.Count, byPoints.PointCount));
        Assert.Equal(67, Read(byPoints, saves, large).Count);   // larger than the whole cache: shown, never stored
        Assert.Equal(67, Read(byPoints, saves, large).Count);
        Assert.Equal((4, 1, 34L), (byPoints.Parses, byPoints.Count, byPoints.PointCount));

        byPoints.Clear();
        Assert.Equal((0, 0L), (byPoints.Count, byPoints.PointCount));
    }

    [Fact]
    public void RequestsForOneChartAtTheSameTime_ShareOneParse()
    {
        using var saves = new TempDir();
        var save = WriteSave(saves, "a.rws", 513);
        var cache = new RecorderChartCache(maxEntries: 16, maxPoints: 10_000);
        using var start = new Barrier(8);
        var results = new IReadOnlyList<MetricHistoryPoint>[8];
        var errors = new Exception?[8];
        var threads = Enumerable.Range(0, 8).Select(i => new Thread(() =>
        {
            try
            {
                start.SignalAndWait();
                results[i] = Read(cache, saves, save);
            }
            catch (Exception ex)
            {
                errors[i] = ex;
            }
        }) { IsBackground = true }).ToArray();
        foreach (var thread in threads) thread.Start();
        foreach (var thread in threads) thread.Join();

        Assert.All(errors, Assert.Null);
        Assert.Equal(1, cache.Parses);
        Assert.All(results, r => Assert.Equal(513, r.Count));
        Assert.All(results, r => Assert.Same(results[0], r));
    }
}
