using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ImportEngineReadAheadTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Stamp = new(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    private sealed class SyncProgress(Action<ImportProgress> onReport) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => onReport(value);
    }

    private static byte[] Save(string seed, int ticks)
        => new SaveXmlBuilder().Minimal().World(seed: seed, prv: 1).Tick(ticksGame: ticks).Maps(64 * 1024).Build();

    private static void Write(TempDir dir, string name, byte[] bytes, DateTime? time = null)
    {
        File.WriteAllBytes(dir.File(name), bytes);
        File.SetLastWriteTimeUtc(dir.File(name), time ?? Stamp);
    }

    private static string Imported(TempDir saves, int readAhead)
    {
        using var store = new TempDir();
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var result = new ImportEngine(new SaveReader(), readAhead).Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Assert.False(result.Cancelled);
        session.Mutate(p => p.ImportState.LastScanUtc = null, SaveMode.None);   // the only wall-clock value
        session.Flush();
        return session.Read(ProfileJson.Serialize);
    }

    [Fact]
    public void ReadAhead_ProducesExactlyTheSequentialProfile()
    {
        using var saves = new TempDir();
        for (var i = 0; i < 24; i++)   // 8 colonies, 3 saves each: grouping, superseding and pruning all take part
            Write(saves, $"c{i % 8}-s{i / 8}.rws", Save("seed" + (i % 8), 60000 * (i / 8 + 1)), Stamp.AddMinutes(i));
        File.WriteAllText(saves.File("broken.rws"), "<savegame><meta>");
        var sequential = Imported(saves, readAhead: 1);
        Assert.Equal(sequential, Imported(saves, readAhead: 3));
        Assert.Equal(sequential, Imported(saves, readAhead: 8));
    }

    [Fact]
    public void FileReplacedAfterItWasReadAhead_IsReadAgainAtItsTurn()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 6; i++)
            Write(saves, $"s{i}.rws", Save("seed" + i, 60000));
        var replacement = Save("replaced", 120000);
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var progress = new SyncProgress(p =>
        {
            if (p.CurrentFile == "s1.rws")   // s3 is within the read-ahead window by now, or will be read at its turn
                Write(saves, "s3.rws", replacement, Stamp.AddHours(1));
        });
        new ImportEngine(new SaveReader(), readAhead: 3).Run(saves.Path, session, progress, TestContext.Current.CancellationToken);
        var s3 = session.Read(p => p.SaveObservations.Single(o => o.FileName == "s3.rws" && !o.Superseded));
        Assert.Equal((long)replacement.Length, s3.FileSize);
        Assert.Equal(Stamp.AddHours(1), s3.LastWriteUtc);
        Assert.Equal(120000, s3.Values.TicksGame);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ExternalProfileMutationInvalidatesSkipIndexDuringMixedImport(int readAhead)
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 4; i++)
            Write(saves, $"s{i}.rws", Save("seed" + i, 60000));
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var engine = new ImportEngine(new SaveReader(), readAhead);
        engine.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Write(saves, "s0.rws", Save("seed0", 120000), Stamp.AddMinutes(1));
        var progress = new SyncProgress(p =>
        {
            if (p.CurrentFile == "s2.rws")
                session.Mutate(profile => profile.SaveObservations.RemoveAll(o => o.FileName == "s3.rws"), SaveMode.None);
        });
        var mixed = engine.Run(saves.Path, session, progress, TestContext.Current.CancellationToken);
        Assert.Equal((2, 2, 0), (mixed.Read, mixed.Skipped, mixed.Failed));
        Assert.Contains(session.Read(p => p.SaveObservations.ToArray()), o => o.FileName == "s3.rws");
    }

    [Fact]
    public void UnchangedFilesAreNotReadAhead_OnAWarmScan()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 6; i++)
            Write(saves, $"s{i}.rws", Save("seed" + i, 60000));
        var session = new ProfileSession(new ProfileStore(store.Path), () => Now);
        var engine = new ImportEngine(new SaveReader(), readAhead: 3);
        engine.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        var warm = engine.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Assert.Equal((0, 6, 0), (warm.Read, warm.Skipped, warm.Failed));
    }
}
