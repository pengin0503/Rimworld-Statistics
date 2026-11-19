using System.Text.Json;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// The processing-resource settings: how many saves an import reads at once and at what priority, background
/// precomputation, and the cache sizes. Standard must be exactly the behaviour from before the settings existed, and no
/// setting may change what is read, recorded or shown.
/// </summary>
public sealed class ProcessingResourceTests
{
    private static readonly DateTime Now = new(2026, 11, 19, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Stamp = new(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    // ---- ResourcePolicy -------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(256)]
    public void StandardThreads_AreTheReadAheadUsedBeforeTheSetting(int processors)
    {
        // ImportEngine used Math.Clamp(Environment.ProcessorCount / 2, 1, 3) before this setting existed.
        var before = Math.Clamp(processors / 2, 1, 3);
        Assert.Equal(before, ResourcePolicy.StandardImportThreads(processors));
        Assert.Equal(before, ResourcePolicy.ImportThreads(ImportThreadMode.Standard, manualThreads: 40, processors));
        Assert.Equal(before, ResourcePolicy.ImportThreads((ImportThreadMode)99, manualThreads: 40, processors));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 3)]
    [InlineData(16, 15)]
    [InlineData(65, 64)]
    [InlineData(500, 64)]
    public void MoreThreads_AreEveryLogicalProcessorButOne(int processors, int expected)
        => Assert.Equal(expected, ResourcePolicy.ImportThreads(ImportThreadMode.More, manualThreads: 2, processors));

    [Theory]
    [InlineData(-5, 16, 1)]
    [InlineData(0, 16, 1)]
    [InlineData(7, 16, 7)]
    [InlineData(16, 16, 16)]
    [InlineData(40, 16, 16)]
    [InlineData(100, 128, 64)]
    [InlineData(3, 1, 1)]
    public void ManualThreads_AreClampedToOneAndTheProcessorCount(int manual, int processors, int expected)
    {
        Assert.Equal(expected, ResourcePolicy.ImportThreads(ImportThreadMode.Manual, manual, processors));
        Assert.Equal(Math.Clamp(processors, 1, 64), ResourcePolicy.MaxManualImportThreads(processors));
    }

    [Fact]
    public void OnlyStandardKeepsNormalPriority()
    {
        Assert.Equal(ThreadPriority.Normal, ResourcePolicy.ImportPriority(ImportThreadMode.Standard));
        Assert.Equal(ThreadPriority.BelowNormal, ResourcePolicy.ImportPriority(ImportThreadMode.More));
        Assert.Equal(ThreadPriority.BelowNormal, ResourcePolicy.ImportPriority(ImportThreadMode.Manual));
    }

    [Fact]
    public void StandardCaches_AreTheSizesUsedBeforeTheSetting()
    {
        // RecorderChartCache.Shared was new(256, 32_768) and ProfileSession kept 8 views; no colony report was kept.
        Assert.Equal(new CacheLimits(256, 32_768, 8, 0), ResourcePolicy.Caches(MemoryUseMode.Standard, precompute: false));
        Assert.Equal(ResourcePolicy.StandardCaches, ResourcePolicy.Caches((MemoryUseMode)7, precompute: false));
        using var store = new TempDir();
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now, recorderCharts: new RecorderChartCache(1, 1));
        Assert.Equal(8, session.ViewCacheCapacity);
        Assert.Equal(0, session.ColonyReportCacheCapacity);
    }

    [Fact]
    public void Caches_GrowWithMoreMemoryAndKeepAFewReportsForPrecomputation()
    {
        Assert.Equal(ResourcePolicy.MoreCaches, ResourcePolicy.Caches(MemoryUseMode.More, precompute: false));
        Assert.Equal(ResourcePolicy.MoreCaches, ResourcePolicy.Caches(MemoryUseMode.More, precompute: true));
        Assert.Equal(ResourcePolicy.StandardCaches with { ColonyReports = ResourcePolicy.PrecomputeColonyReports },
            ResourcePolicy.Caches(MemoryUseMode.Standard, precompute: true));
        var more = ResourcePolicy.MoreCaches;
        var standard = ResourcePolicy.StandardCaches;
        Assert.True(more.ChartSeries > standard.ChartSeries && more.ChartPoints > standard.ChartPoints
            && more.Views > standard.Views && more.ColonyReports > ResourcePolicy.PrecomputeColonyReports);
    }

    [Fact]
    public void ChartCacheMiB_IsTheMeasuredBytesPerPointTimesTheLimit()
    {
        Assert.Equal(6, ResourcePolicy.ChartCacheMiB(ResourcePolicy.StandardCaches));    // 32,768 × 190 B = 5.9 MiB
        Assert.Equal(48, ResourcePolicy.ChartCacheMiB(ResourcePolicy.MoreCaches));       // 262,144 × 190 B = 47.5 MiB
    }

    [Theory]
    [InlineData(0, ImportThreadMode.Standard)]
    [InlineData(1, ImportThreadMode.More)]
    [InlineData(2, ImportThreadMode.Manual)]
    [InlineData(3, ImportThreadMode.Standard)]
    [InlineData(-1, ImportThreadMode.Standard)]
    public void StoredThreadMode_UnknownValuesAreStandard(int stored, ImportThreadMode expected)
        => Assert.Equal(expected, ResourcePolicy.ParseImportThreadMode(stored));

    [Theory]
    [InlineData(0, MemoryUseMode.Standard)]
    [InlineData(1, MemoryUseMode.More)]
    [InlineData(2, MemoryUseMode.Standard)]
    [InlineData(int.MinValue, MemoryUseMode.Standard)]
    public void StoredMemoryMode_UnknownValuesAreStandard(int stored, MemoryUseMode expected)
        => Assert.Equal(expected, ResourcePolicy.ParseMemoryUseMode(stored));

    // ---- Thread priority ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task LowerPriority_AppliesDuringTheWorkAndIsRestoredAfterIt()
    {
        var seen = await Task.Run(() =>
        {
            var before = Thread.CurrentThread.Priority;
            var inside = ThreadPriorities.Run(ThreadPriority.BelowNormal, () => Thread.CurrentThread.Priority);
            Assert.Throws<InvalidOperationException>(() => ThreadPriorities.Run<int>(ThreadPriority.BelowNormal,
                () => throw new InvalidOperationException()));
            return (before, inside, after: Thread.CurrentThread.Priority);
        }, TestContext.Current.CancellationToken);
        Assert.Equal(ThreadPriority.BelowNormal, seen.inside);
        Assert.Equal(seen.before, seen.after);
    }

    [Fact]
    public async Task NormalPriority_LeavesTheThreadAsItIs()
    {
        var seen = await Task.Run(() =>
        {
            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
            try
            {
                return ThreadPriorities.Run(ThreadPriority.Normal, () => Thread.CurrentThread.Priority);
            }
            finally
            {
                Thread.CurrentThread.Priority = ThreadPriority.Normal;
            }
        }, TestContext.Current.CancellationToken);
        Assert.Equal(ThreadPriority.AboveNormal, seen);
    }

    // ---- Import: thread count, priority and stopping --------------------------------------------------------------------

    private static byte[] Save(string seed, int ticks, int mapBytes = 96 * 1024)
        => new SaveXmlBuilder().Minimal().World(seed: seed, prv: 1).Tick(ticksGame: ticks).Maps(mapBytes).Build();

    private static void Write(TempDir dir, string name, byte[] bytes, DateTime time)
    {
        File.WriteAllBytes(dir.File(name), bytes);
        File.SetLastWriteTimeUtc(dir.File(name), time);
    }

    private static string Imported(TempDir saves, ImportEngine engine)
    {
        using var store = new TempDir();
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var result = engine.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Assert.False(result.Cancelled);
        session.Mutate(p => p.ImportState.LastScanUtc = null, SaveMode.None);   // the only wall-clock value
        session.Flush();
        return session.Read(ProfileJson.Serialize);
    }

    [Fact]
    public void DefaultEngine_ReadsTheStandardNumberAtOnce()
    {
        Assert.Equal(ResourcePolicy.StandardImportThreads(Environment.ProcessorCount), new ImportEngine(new SaveReader()).ReadAhead);
        Assert.Equal(ResourcePolicy.MaxImportThreads, new ImportEngine(new SaveReader(), 1_000).ReadAhead);
    }

    [Fact]
    public void ManyThreadsAtLowerPriority_RecordExactlyWhatOneThreadRecords()
    {
        using var saves = new TempDir();
        for (var i = 0; i < 20; i++)
            Write(saves, $"c{i % 5}-s{i / 5}.rws", Save("seed" + (i % 5), 60000 * (i / 5 + 1)), Stamp.AddMinutes(i));
        File.WriteAllText(saves.File("broken.rws"), "<savegame><meta>");
        var sequential = Imported(saves, new ImportEngine(new SaveReader(), 1));
        Assert.Equal(sequential, Imported(saves, new ImportEngine(new SaveReader(), 16, TimeProvider.System, ThreadPriority.BelowNormal)));
        Assert.Equal(sequential, Imported(saves, new ImportEngine(new SaveReader(), 64, TimeProvider.System, ThreadPriority.BelowNormal)));
    }

    [Fact]
    public async Task ImportAtLowerPriority_RestoresTheCallingThread()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("seed", 60000), Stamp);
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var engine = new ImportEngine(new SaveReader(), 4, TimeProvider.System, ThreadPriority.BelowNormal);
        var ct = TestContext.Current.CancellationToken;
        var priorities = await Task.Run(() =>
        {
            var before = Thread.CurrentThread.Priority;
            engine.Run(saves.Path, session, null, ct);
            return (before, after: Thread.CurrentThread.Priority);
        }, ct);
        Assert.Equal(priorities.before, priorities.after);
        Assert.Equal(1, session.Read(p => p.SaveObservations.Count));
    }

    /// <summary>Serves a save and cancels the token once <paramref name="cancelAt"/> bytes were served.</summary>
    private sealed class CancellingStream(byte[] bytes, long cancelAt, CancellationTokenSource cts) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public long Served { get; private set; }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            Served += read;
            if (Served >= cancelAt)
                cts.Cancel();
            return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(1)]                  // before the XML has started
    [InlineData(256 * 1024)]         // in the middle of the maps
    public void StoppedRead_GivesUpWithinABlockAndTellsNothing(long cancelAt)
    {
        var bytes = Save("seed", 60000, mapBytes: 4 * 1024 * 1024);
        using var cts = new CancellationTokenSource();
        var stream = new CancellingStream(bytes, cancelAt, cts);
        var result = new SaveReader().ReadUnlessCancelled(stream, "a.rws", bytes.Length, Stamp, cts.Token);
        Assert.Null(result);
        // At most the block that saw the cancellation and the XML reader's own buffer after it.
        Assert.True(stream.Served < cancelAt + 4 * (1 << 16), $"served {stream.Served} of {bytes.Length}");
    }

    [Fact]
    public void UncancelledRead_IsTheSameWithOrWithoutAToken()
    {
        var bytes = Save("seed", 60000, mapBytes: 512 * 1024);
        var plain = new SaveReader().ReadDetailed(new MemoryStream(bytes), "a.rws", bytes.Length, Stamp);
        using var cts = new CancellationTokenSource();
        var withToken = new SaveReader().ReadUnlessCancelled(new MemoryStream(bytes), "a.rws", bytes.Length, Stamp, cts.Token)!;
        Assert.Equal(ObservationStatus.Ok, plain.Observation.Status);
        Assert.Equal(JsonSerializer.Serialize(plain.Observation, ProfileJson.CompactOptions),
            JsonSerializer.Serialize(withToken.Observation, ProfileJson.CompactOptions));
        Assert.Equal(JsonSerializer.Serialize(plain.Metrics, ProfileJson.CompactOptions),
            JsonSerializer.Serialize(withToken.Metrics, ProfileJson.CompactOptions));
    }

    [Fact]
    public void ReadFromDisk_CancelledBeforeItStartsIsNotRecorded()
    {
        using var saves = new TempDir();
        Write(saves, "a.rws", Save("seed", 60000), Stamp);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Null(new SaveReader().ReadUnlessCancelled(saves.File("a.rws"), cts.Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void ImportStoppedDuringARead_RecordsNoErrorAndTheNextScanReadsTheRest(int readAhead)
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        // Large enough that reading one takes far longer than the delay before the stop.
        for (var i = 0; i < 3; i++)
            Write(saves, $"s{i}.rws", Save("seed" + i, 60000, mapBytes: 24 * 1024 * 1024), Stamp.AddMinutes(i));
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            if (p.CurrentFile == "s0.rws")
                cts.CancelAfter(TimeSpan.FromMilliseconds(20));
        });
        var engine = new ImportEngine(new SaveReader(), readAhead);
        var stopped = engine.Run(saves.Path, session, progress, cts.Token);
        Assert.True(stopped.Cancelled);
        Assert.Equal(0, stopped.Failed);
        Assert.DoesNotContain(session.Read(p => p.SaveObservations.ToList()), o => o.Status == ObservationStatus.Error);

        var rest = engine.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Assert.False(rest.Cancelled);
        Assert.Equal(0, rest.Failed);
        Assert.Equal(3, session.Read(p => p.SaveObservations.Count(o => o.Status == ObservationStatus.Ok)));
    }

    private sealed class SyncProgress(Action<ImportProgress> onReport) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => onReport(value);
    }

    // ---- Cache limits ---------------------------------------------------------------------------------------------------

    private static ColonyIdentity Identity(string campaignId, string seed) => new()
    {
        ModCampaignId = campaignId,
        SeedString = seed,
        PersistentRandomValue = 7,
        StartingTile = "12",
        GameStartAbsTick = 0,
        FirstWealthBits = BitConverter.SingleToUInt32Bits(10_000f),
        StartingPawnIds = ["Thing_Human1"],
    };

    private static ObservedValues Values(int ticks) => new()
    {
        TicksGame = ticks,
        RealPlayTime = ticks / 60.0,
        NumRaidsEnemy = 2,
        NumThreatsQueued = 3,
        ColonistsKilled = 0,
        GreatestPopulation = 4,
        WealthLast = 20_000,
        WealthMax = 25_000,
        WealthPoints = 2,
    };

    private static DisplayInfo Display(string name) => new()
    {
        FactionName = name,
        WorldName = "Terra",
        Scenario = "Crashlanded",
        Storyteller = "Cassandra",
        Difficulty = "Rough",
    };

    private static void Live(ProfileSession session, string campaignId, int ticks)
        => session.Mutate(p => ProfileMutations.ApplyLiveObservation(p, campaignId, Identity(campaignId, "seed-" + campaignId),
            Values(ticks), Display("Colony " + campaignId), Now, "1.7.5011 rev24"), SaveMode.None);

    private static ProfileSession SessionWithColonies(TempDir store, params string[] campaigns)
    {
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now, recorderCharts: new RecorderChartCache(16, 1000));
        foreach (var campaign in campaigns)
            Live(session, campaign, 120_000);
        return session;
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value, ProfileJson.CompactOptions);

    [Fact]
    public void ApplyCacheLimits_SetsEveryBoundAndLowerBoundsDropTheOldestViews()
    {
        using var store = new TempDir();
        var charts = new RecorderChartCache(256, 32_768);
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now, recorderCharts: charts);
        Live(session, "a", 120_000);
        session.ApplyCacheLimits(ResourcePolicy.MoreCaches);
        Assert.Equal(32, session.ViewCacheCapacity);
        Assert.Equal(16, session.ColonyReportCacheCapacity);
        Assert.Equal(2_048, charts.MaxEntries);
        Assert.Equal(262_144, charts.MaxPoints);

        var visible = MetricDisplayPolicy.AllDetailsVisibleUnits;
        var newest = session.GetView(new StatsOptions(), visible, ViewScope.All);
        session.GetView(new StatsOptions { IncludeDevMode = true }, visible, ViewScope.All);
        session.GetView(new StatsOptions(), null, ViewScope.All);
        Assert.Same(newest, session.GetView(new StatsOptions(), visible, ViewScope.All));   // now the most recently used
        Assert.Equal(3, session.CachedViews);

        session.ApplyCacheLimits(ResourcePolicy.StandardCaches with { Views = 1 });
        // Trimmed at once, or (when a background save held the profile) at its next use, which this is.
        Assert.Same(newest, session.GetView(new StatsOptions(), visible, ViewScope.All));
        Assert.Equal(1, session.CachedViews);
        Assert.Equal(256, charts.MaxEntries);
        Assert.Equal(0, session.ColonyReportCacheCapacity);
    }

    [Fact]
    public async Task ApplyCacheLimits_DoesNotWaitForABusyProfile()
    {
        var ct = TestContext.Current.CancellationToken;
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        using var holding = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Task.Run(() => session.Read(_ =>
        {
            holding.Set();
            release.Wait(ct);
            return 0;
        }), ct);
        holding.Wait(ct);
        try
        {
            // Throws TimeoutException if it waits for the profile lock.
            await Task.Run(() => session.ApplyCacheLimits(ResourcePolicy.MoreCaches), ct).WaitAsync(TimeSpan.FromSeconds(10), ct);
            Assert.Equal(32, session.ViewCacheCapacity);
        }
        finally
        {
            release.Set();
            await holder;
        }
    }

    [Fact]
    public void ChartCacheSetLimits_TrimsAndRefusesNegativeBounds()
    {
        var cache = new RecorderChartCache(4, 100);
        cache.SetLimits(2, 50);
        Assert.Equal(2, cache.MaxEntries);
        Assert.Equal(50, cache.MaxPoints);
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetLimits(-1, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => cache.SetLimits(1, -10));
        Assert.Equal(2, cache.MaxEntries);
    }

    [Fact]
    public void ColonyReports_AreNotKeptByDefault()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        var view = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits, ViewScope.All);
        var key = Assert.Single(view.Colonies).Key;
        var first = session.ReadColonySummaryAndChronicle(view, key);
        var second = session.ReadColonySummaryAndChronicle(view, key);
        Assert.NotNull(first.Report);
        Assert.NotSame(first.Report, second.Report);
        Assert.Equal(0, session.CachedColonyReports);
    }

    [Fact]
    public void KeptColonyReport_IsWhatBuildingItAgainGives_UntilTheRecordChanges()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a", "b");
        session.ColonyReportCacheCapacity = 4;
        var options = new StatsOptions();
        var view = session.GetView(options, MetricDisplayPolicy.AllDetailsVisibleUnits, ViewScope.All);
        var key = view.Colonies.First(c => c.LiveCampaigns.Any(l => l.CampaignId == "a")).Key;

        var first = session.ReadColonySummaryAndChronicle(view, key);
        var kept = session.ReadColonySummaryAndChronicle(view, key);
        Assert.Same(first.Report, kept.Report);
        Assert.Same(first.Chronicle, kept.Chronicle);
        Assert.Equal(1, session.CachedColonyReports);

        // Another option, colony or view object is another entry, never the kept one.
        Assert.NotSame(first.Report, session.ReadColonySummaryAndChronicle(view, key, includeDevMode: true).Report);
        var otherView = session.GetView(options, null, ViewScope.All);
        Assert.NotSame(first.Report, session.ReadColonySummaryAndChronicle(otherView, key).Report);

        // What was kept is exactly what a session without the cache builds.
        using var plainStore = new TempDir();
        var plain = SessionWithColonies(plainStore, "a", "b");
        var plainView = plain.GetView(options, MetricDisplayPolicy.AllDetailsVisibleUnits, ViewScope.All);
        var plainBuilt = plain.ReadColonySummaryAndChronicle(plainView, key);
        Assert.Equal(Json(plainBuilt.Report), Json(kept.Report));
        Assert.Equal(Json(plainBuilt.Chronicle), Json(kept.Chronicle));

        // A change of the record drops everything kept: the next read builds from the new record.
        Live(session, "a", 240_000);
        Assert.Equal(0, session.CachedColonyReports);
        var newView = session.GetView(options, MetricDisplayPolicy.AllDetailsVisibleUnits, ViewScope.All);
        var rebuilt = session.ReadColonySummaryAndChronicle(newView, key);
        Assert.NotSame(first.Report, rebuilt.Report);
        Assert.NotEqual(Json(first.Report), Json(rebuilt.Report));
    }

    [Fact]
    public void KeptColonyReports_AreBoundedLeastRecentlyUsedFirst()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a", "b", "c");
        session.ColonyReportCacheCapacity = 2;
        var view = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits, ViewScope.All);
        var keys = view.Colonies.Select(c => c.Key).ToArray();
        Assert.Equal(3, keys.Length);
        var a = session.ReadColonySummaryAndChronicle(view, keys[0]).Report;
        session.ReadColonySummaryAndChronicle(view, keys[1]);
        Assert.Same(a, session.ReadColonySummaryAndChronicle(view, keys[0]).Report);   // a is now the most recent
        session.ReadColonySummaryAndChronicle(view, keys[2]);                          // drops keys[1]
        Assert.Equal(2, session.CachedColonyReports);
        Assert.Same(a, session.ReadColonySummaryAndChronicle(view, keys[0]).Report);
        session.ColonyReportCacheCapacity = 0;
        Assert.Equal(0, session.CachedColonyReports);
    }

    [Fact]
    public void DroppingAView_DropsTheReportsKeptForIt()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        session.ApplyCacheLimits(ResourcePolicy.MoreCaches);
        var visible = MetricDisplayPolicy.AllDetailsVisibleUnits;
        var first = session.GetView(new StatsOptions(), visible, ViewScope.All);
        var second = session.GetView(new StatsOptions { IncludeDevMode = true }, visible, ViewScope.All);
        var key = Assert.Single(first.Colonies).Key;
        session.ReadColonySummaryAndChronicle(first, key);
        var kept = session.ReadColonySummaryAndChronicle(second, key, includeDevMode: true).Report;
        Assert.Equal(2, session.CachedColonyReports);

        session.ViewCacheCapacity = 1;   // drops the older view, first
        Assert.Equal(1, session.CachedViews);
        Assert.Equal(1, session.CachedColonyReports);
        Assert.Same(kept, session.ReadColonySummaryAndChronicle(second, key, includeDevMode: true).Report);
    }

    // ---- Precomputation -------------------------------------------------------------------------------------------------

    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void Schedule_BuildsATargetOnceAfterItStayedQuiet()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        var schedule = new PrecomputeSchedule();
        var target = new PrecomputeTarget(session, 5, false, null);
        Assert.Null(schedule.Next(target, busy: false, S(0)));
        Assert.Null(schedule.Next(target, busy: false, S(1.9)));
        Assert.Equal(target, schedule.Next(target, busy: false, S(2.0)));
        Assert.Null(schedule.Next(target, busy: false, S(10)));   // built once, even if it failed

        var changed = target with { Revision = 6 };
        Assert.Null(schedule.Next(changed, busy: false, S(11)));
        Assert.Null(schedule.Next(changed, busy: false, S(12.5)));
        Assert.Equal(changed, schedule.Next(changed, busy: false, S(13)));

        Assert.Null(schedule.Next(changed with { IncludeDevMode = true }, busy: false, S(14)));
        Assert.NotNull(schedule.Next(changed with { IncludeDevMode = true }, busy: false, S(16)));
        Assert.Null(schedule.Next(changed with { CampaignId = "a" }, busy: false, S(17)));
        Assert.NotNull(schedule.Next(changed with { CampaignId = "a" }, busy: false, S(19)));
    }

    [Fact]
    public void Schedule_BusyTimeIsNotQuietAndResetBuildsAgain()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        var schedule = new PrecomputeSchedule();
        var target = new PrecomputeTarget(session, 1, false, null);
        Assert.Null(schedule.Next(target, busy: true, S(0)));
        Assert.Null(schedule.Next(target, busy: true, S(30)));
        Assert.Null(schedule.Next(target, busy: false, S(31)));   // the quiet period counts from the last busy check
        Assert.Null(schedule.Next(target, busy: false, S(31.9)));
        Assert.Equal(target, schedule.Next(target, busy: false, S(32)));

        schedule.Reset();
        Assert.Null(schedule.Next(target, busy: false, S(40)));
        Assert.Equal(target, schedule.Next(target, busy: false, S(42)));
    }

    [Fact]
    public void Precompute_FillsTheCachesTheWindowsReadWithWhatTheyWouldBuild()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a", "b");
        session.ApplyCacheLimits(ResourcePolicy.Caches(MemoryUseMode.Standard, precompute: true));
        var visible = MetricDisplayPolicy.AllDetailsVisibleUnits;
        Assert.Equal(0, session.CachedViews);

        StatisticsPrecompute.Run(session, new StatsOptions(), "a", CancellationToken.None);

        // TryGetCachedView, which the windows call, never waits for the profile; the counts here do.
        Assert.Equal(1, session.CachedViews);
        var view = session.GetView(new StatsOptions(), visible, ViewScope.All);
        Assert.Equal(1, session.CachedViews);   // the window's request was the precomputed view, not a new one
        Assert.Equal(1, session.CachedColonyReports);
        var key = InGameColonyAccess.Resolve(view, "a").ColonyKey!;
        var report = session.ReadColonySummaryAndChronicle(view, key);
        Assert.Equal(1, session.CachedColonyReports);   // the in-game window got the precomputed one

        using var plainStore = new TempDir();
        var plain = SessionWithColonies(plainStore, "a", "b");
        var plainView = plain.GetView(new StatsOptions(), visible, ViewScope.All);
        Assert.Equal(Json(plainView.Stats), Json(view.Stats));
        Assert.Equal(Json(plain.ReadColonySummaryAndChronicle(plainView, key).Report), Json(report.Report));
    }

    [Fact]
    public void Precompute_OnTheTitleScreenOrForAnUnknownColonyBuildsOnlyTheView()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        session.ColonyReportCacheCapacity = 4;
        StatisticsPrecompute.Run(session, new StatsOptions(), null, CancellationToken.None);
        StatisticsPrecompute.Run(session, new StatsOptions(), "not-recorded", CancellationToken.None);
        Assert.Equal(1, session.CachedViews);
        session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits, ViewScope.All);
        Assert.Equal(1, session.CachedViews);
        Assert.Equal(0, session.CachedColonyReports);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var otherStore = new TempDir();
        var other = SessionWithColonies(otherStore, "a");
        StatisticsPrecompute.Run(other, new StatsOptions(), "a", cancelled.Token);
        Assert.Equal(0, other.CachedViews);
    }

    [Fact]
    public void Precomputer_RunsOneBuildAtATimeAndReportsAFailureOnce()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        var builds = new List<(Action Work, TaskCompletionSource Done)>();
        var precomputer = new StatisticsPrecomputer(work =>
        {
            var done = new TaskCompletionSource();
            builds.Add((work, done));
            return done.Task;
        });
        var target = new PrecomputeTarget(session, session.Revision, false, null);
        Assert.Null(precomputer.Poll(target, busy: false, S(0)));
        Assert.Null(precomputer.Poll(target, busy: false, S(2)));
        Assert.Single(builds);
        Assert.True(precomputer.Running);

        // A newer target waits for the running build, then for its own quiet period.
        var newer = target with { Revision = target.Revision + 1 };
        Assert.Null(precomputer.Poll(newer, busy: false, S(5)));
        Assert.Single(builds);
        builds[0].Work();
        builds[0].Done.SetException(new InvalidOperationException("boom"));
        Assert.IsType<InvalidOperationException>(precomputer.Poll(newer, busy: false, S(6)));
        Assert.False(precomputer.Running);
        Assert.Null(precomputer.Poll(newer, busy: false, S(6.5)));   // reported once
        Assert.Single(builds);
        Assert.Null(precomputer.Poll(newer, busy: false, S(8)));     // two seconds after the build stopped being busy
        Assert.Equal(2, builds.Count);
        Assert.Equal(2, precomputer.Started);
        builds[1].Work();
        builds[1].Done.SetResult();
        Assert.Null(precomputer.Poll(newer, busy: false, S(9)));
        Assert.Null(precomputer.Poll(newer, busy: false, S(20)));    // the same target is not built again
        Assert.Equal(2, builds.Count);
        Assert.Equal(1, session.CachedViews);
    }

    [Fact]
    public void Precomputer_TurnedOffStopsAndBuildsAgainWhenTurnedOn()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        var started = 0;
        var precomputer = new StatisticsPrecomputer(work =>
        {
            started++;
            work();
            return Task.CompletedTask;
        });
        var target = new PrecomputeTarget(session, session.Revision, false, null);
        precomputer.Poll(target, busy: false, S(0));
        precomputer.Poll(target, busy: false, S(2));
        Assert.Equal(1, started);
        precomputer.Poll(null, busy: false, S(3));
        precomputer.Poll(target, busy: false, S(4));
        Assert.Equal(1, started);
        precomputer.Poll(target, busy: false, S(6));
        Assert.Equal(2, started);
    }

    [Fact]
    public void Precomputer_RunsTheBuildBelowNormalPriorityAndStopsItWhenTurnedOff()
    {
        using var store = new TempDir();
        var session = SessionWithColonies(store, "a");
        var seen = new List<(ThreadPriority Priority, bool Cancelled)>();
        using var building = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var precomputer = new StatisticsPrecomputer(work => Task.Run(work), (target, ct) =>
        {
            building.Set();
            release.Wait(TimeSpan.FromSeconds(30));
            lock (seen)
                seen.Add((Thread.CurrentThread.Priority, ct.IsCancellationRequested));
        });
        var target = new PrecomputeTarget(session, session.Revision, false, null);
        precomputer.Poll(target, busy: false, S(0));
        precomputer.Poll(target, busy: false, S(2));
        Assert.True(building.Wait(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
        precomputer.Poll(null, busy: false, S(3));   // turned off while it builds
        release.Set();
        Assert.True(SpinWait.SpinUntil(() => !precomputer.Running, TimeSpan.FromSeconds(30)));
        var (priority, cancelled) = Assert.Single(seen);
        Assert.Equal(ThreadPriority.BelowNormal, priority);
        Assert.True(cancelled);
    }
}
