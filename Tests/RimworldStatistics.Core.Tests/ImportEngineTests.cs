using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ImportEngineTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Stamp = new(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    /// <summary>Synchronous progress sink (Progress&lt;T&gt; would post callbacks asynchronously).</summary>
    private sealed class SyncProgress(Action<ImportProgress> onReport) : IProgress<ImportProgress>
    {
        public List<ImportProgress> Reports { get; } = new();
        public void Report(ImportProgress value)
        {
            Reports.Add(value);
            onReport(value);
        }
    }

    private static byte[] Save(string seed) => new SaveXmlBuilder().Minimal().World(seed: seed, prv: 1).Build();

    private static void Write(TempDir dir, string name, byte[] bytes, DateTime? time = null)
    {
        File.WriteAllBytes(dir.File(name), bytes);
        File.SetLastWriteTimeUtc(dir.File(name), time ?? Stamp);
    }

    private static ProfileSession NewSession(TempDir store) => new(new ProfileStore(store.Path), () => Now);

    private static ImportResult Run(TempDir saves, ProfileSession s, IProgress<ImportProgress>? progress = null, CancellationToken? ct = null)
        => new ImportEngine(new SaveReader()).Run(saves.Path, s, progress, ct ?? TestContext.Current.CancellationToken);

    [Fact]
    public void Run_ReadsRwsAndOld_IgnoresOthers()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("a"));
        Write(saves, "b.rws.old", Save("b"));
        Write(saves, "c.rsc", Save("c"));
        Write(saves, "d.rws.bak", Save("d"));
        Directory.CreateDirectory(saves.File("sub.rws"));   // a directory, not a save
        var s = NewSession(store);
        var r = Run(saves, s);
        Assert.Equal((2, 0, 0, false), (r.Read, r.Skipped, r.Failed, r.Cancelled));
        Assert.Equal(new[] { "a.rws", "b.rws.old" }, r.PresentFiles);
        Assert.Equal(new[] { "a.rws", "b.rws.old" }, s.Read(p => p.SaveObservations.Select(o => o.FileName).Order(StringComparer.Ordinal).ToArray()));
        Assert.Equal(DateTime.UtcNow.Date, s.Read(p => p.ImportState.LastScanUtc)!.Value.Date);
        s.Flush();
        Assert.Equal(2, new ProfileStore(store.Path).Load(Now).Profile.SaveObservations.Count);   // completion is saved
    }

    [Theory]
    [InlineData("x.rws", true)]
    [InlineData("X.RWS", true)]
    [InlineData("y.Rws.Old", true)]
    [InlineData("z.rws.bak", false)]
    [InlineData("z.rwsx", false)]
    [InlineData("rws", false)]
    public void IsSaveFile_MatchesRwsAndOldCaseInsensitively(string name, bool expected)
        => Assert.Equal(expected, ImportEngine.IsSaveFile(name));

    [Fact]
    public void Run_SecondRun_SkipsUnchanged()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("a"));
        Write(saves, "b.rws", Save("b"));
        var s = NewSession(store);
        Run(saves, s);
        var r = Run(saves, s);
        Assert.Equal((0, 2, 0), (r.Read, r.Skipped, r.Failed));
        Assert.Equal(2, s.Read(p => p.SaveObservations.Count));
    }

    [Fact]
    public void Run_ChangedFile_AppendsObservation()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "Autosave-1.rws", Save("first colony"));
        var s = NewSession(store);
        Run(saves, s);
        Write(saves, "Autosave-1.rws", Save("second colony"), Stamp.AddHours(1));   // slot reused by another colony
        var r = Run(saves, s);
        Assert.Equal((1, 0), (r.Read, r.Skipped));
        Assert.Equal(2, s.Read(p => p.SaveObservations.Count));
        Assert.Equal(1, s.Read(p => p.SaveObservations.Count(o => o.Superseded)));
        Assert.Equal(2, s.GetView(new StatsOptions()).Stats.ColonyCount);
    }

    [Fact]
    public void Run_ErrorFile_IsRetriedNextRun()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var good = Save("a");
        Write(saves, "a.rws", good[..(good.Length / 2)]);   // being written / torn
        var s = NewSession(store);
        var first = Run(saves, s);
        Assert.Equal((1, 1), (first.Read, first.Failed));
        Assert.Equal(ObservationStatus.Error, s.Read(p => p.SaveObservations.Single().Status));

        Write(saves, "a.rws", good);   // fixed; the time is left unchanged
        var second = Run(saves, s);
        Assert.Equal((1, 0), (second.Read, second.Failed));
        Assert.Equal(ObservationStatus.Ok, s.Read(p => p.SaveObservations.Single().Status));
    }

    [Fact]
    public void Run_Cancelled_KeepsDoneAndResumes()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 10; i++)
            Write(saves, $"s{i:00}.rws", Save("seed" + i));
        var s = NewSession(store);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress = new SyncProgress(p => { if (p.Done == 3) cts.Cancel(); });
        var r = Run(saves, s, progress, cts.Token);
        Assert.True(r.Cancelled);
        Assert.Equal(3, r.Read);
        Assert.Null(s.Read(p => p.ImportState.LastScanUtc));
        Assert.Equal(10, progress.Reports[0].Total);
        Assert.Equal("s00.rws", progress.Reports[0].CurrentFile);

        s.Flush();
        Assert.Equal(3, new ProfileStore(store.Path).Load(Now).Profile.SaveObservations.Count);   // the finished part is kept

        var resumed = Run(saves, NewSession(store));   // e.g. back at the title screen, next session
        Assert.Equal((7, 3, false), (resumed.Read, resumed.Skipped, resumed.Cancelled));
    }

    [Fact]
    public void Run_MalformedPayload_FailsThatFileOnly()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("a"));
        Write(saves, "b.rws", new SaveXmlBuilder().Minimal().WealthTotalRaw("records", "!!not-base64!!").Build());
        Write(saves, "c.rws", Save("c"));
        var s = NewSession(store);
        var r = Run(saves, s);
        Assert.Equal((3, 1), (r.Read, r.Failed));
        var status = s.Read(p => p.SaveObservations.ToDictionary(o => o.FileName, o => o.Status));
        Assert.Equal((ObservationStatus.Ok, ObservationStatus.Error, ObservationStatus.Ok), (status["a.rws"], status["b.rws"], status["c.rws"]));
    }

    [Fact]
    public void Run_MissingDirectory_ReturnsEmpty()
    {
        using var store = new TempDir();
        var s = NewSession(store);
        var r = new ImportEngine(new SaveReader()).Run(Path.Combine(store.Path, "no-such-dir"), s, null, TestContext.Current.CancellationToken);
        Assert.Equal((0, 0, 0, false), (r.Read, r.Skipped, r.Failed, r.Cancelled));
        Assert.Empty(r.PresentFiles);
    }

    [Fact]
    public void Run_FiveChangedThenSkippedFiles_DoesNotInvalidateCachedViewOnEachSkip()
    {
        // A batch of five changed files is followed by unchanged files. The
        // checkpoint at the fifth read is legitimate, but skipped files must
        // not trigger another no-op mutation and invalidate the cached view.
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 11; i++)
            Write(saves, $"s{i:00}.rws", Save("initial-" + i));
        var session = NewSession(store);
        Assert.Equal(11, Run(saves, session).Read);
        session.Flush();

        for (var i = 0; i < 5; i++)
            Write(saves, $"s{i:00}.rws", Save("updated-" + i), Stamp.AddHours(1));

        StatsView? duringSkippedFiles = null;
        var checkedSkips = 0;
        var progress = new SyncProgress(p =>
        {
            if (p.CurrentFile is null || string.CompareOrdinal(p.CurrentFile, "s05.rws") < 0)
                return;
            var current = session.GetView(new StatsOptions());
            if (duringSkippedFiles is null)
                duringSkippedFiles = current;
            else
            {
                Assert.Same(duringSkippedFiles, current);
                checkedSkips++;
            }
        });

        var result = Run(saves, session, progress);
        Assert.Equal((5, 6, 0), (result.Read, result.Skipped, result.Failed));
        Assert.Equal(5, checkedSkips);
        session.Flush();
        Assert.Null(session.LastSaveError);
    }

    [Fact]
    public void Run_MetricMutationFails_RecordsOneReadAndRetriesAfterRepair()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("seed"));
        var session = NewSession(store);
        // Deliberately corrupt the in-memory metrics collection after loading.
        // The v1 save observation must not be accepted if the numeric mutation fails.
        session.Mutate(p => p.MetricObservations = null!, SaveMode.None);
        var first = Run(saves, session);
        Assert.Equal((1, 1), (first.Read, first.Failed));
        Assert.Equal(ObservationStatus.Error, session.Read(p => p.SaveObservations.Single().Status));

        session.Mutate(p => p.MetricObservations = new List<MetricObservation>(), SaveMode.None);
        var retry = Run(saves, session);
        Assert.Equal((1, 0), (retry.Read, retry.Failed));
        Assert.Equal(ObservationStatus.Ok, session.Read(p => p.SaveObservations.Single().Status));
    }

}
