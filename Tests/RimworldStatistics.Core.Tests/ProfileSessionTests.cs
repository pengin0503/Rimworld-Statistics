using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ProfileSessionTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);

    private static SaveObservation Obs(string file, string pawns = "Thing_Human1")
        => new()
        {
            FileName = file, Status = ObservationStatus.Ok, ReaderVersion = SaveReader.Version,
            Identity = new ColonyIdentity
            {
                SeedString = "s", StartingTile = "1,0", FirstWealthBits = 1, StartingPawnIds = pawns.Split(',').ToList(),
            },
        };

    private static Profile Reload(TempDir dir) => new ProfileStore(dir.Path).Load(Now).Profile;

    [Fact]
    public void Mutate_Immediate_WritesBeforeReturn()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        session.Mutate(p => p.SaveObservations.Add(Obs("a.rws")), SaveMode.Immediate);
        Assert.Equal("a.rws", Reload(dir).SaveObservations.Single().FileName);
        Assert.Null(session.LastSaveError);
    }

    [Fact]
    public void Mutate_Background_CoalescesAndFlushWrites()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
        {
            for (var i = 0; i < 100; i++)
                session.Mutate(p => ProfileMutations.AddSaveObservation(p, Obs($"t{t}-{i}.rws")), SaveMode.Background);
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        session.Flush();
        Assert.Equal(800, Reload(dir).SaveObservations.Count);
        Assert.Equal(800, session.Read(p => p.SaveObservations.Count));
        Assert.Null(session.LastSaveError);
        Assert.False(File.Exists(dir.File("profile.json.tmp")));
    }

    [Fact]
    public void Mutate_None_IsWrittenByFlushOrByTheNextSave()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        session.Mutate(p => p.SaveObservations.Add(Obs("a.rws")), SaveMode.None);
        Assert.False(File.Exists(dir.File("profile.json")));
        session.Flush();
        Assert.Single(Reload(dir).SaveObservations);
        session.Mutate(p => p.SaveObservations.Add(Obs("b.rws")), SaveMode.None);
        session.Mutate(p => p.SaveObservations.Add(Obs("c.rws")), SaveMode.Immediate);
        Assert.Equal(3, Reload(dir).SaveObservations.Count);
        session.Flush();   // nothing pending: no extra write, no backup churn
        Assert.Equal(1, Reload(dir).SaveObservations.Count(o => o.FileName == "c.rws"));
        Assert.False(File.Exists(dir.File("profile.json.bak2")));
    }

    [Fact]
    public void MixedModes_EndWithTheLatestState()
    {
        // Background writes run on the thread pool while Immediate writes run on the caller; the newest state must win.
        // (Ordering is guaranteed by construction — serialize inside the I/O lock, skip stale sequence numbers; this test
        // checks the end state, it cannot force the interleaving.)
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        for (var i = 0; i < 300; i++)
            session.Mutate(p => p.SaveObservations.Add(Obs($"{i}.rws")), i % 3 == 0 ? SaveMode.Immediate : SaveMode.Background);
        session.Mutate(p => p.SaveObservations.Add(Obs("last.rws")), SaveMode.Immediate);
        Thread.Sleep(200);   // let any queued background write finish
        Assert.Equal(301, Reload(dir).SaveObservations.Count);
        session.Flush();
        Assert.Equal(301, Reload(dir).SaveObservations.Count);
    }

    [Fact]
    public void GetView_IsCachedUntilMutation()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        session.Mutate(p => p.SaveObservations.Add(Obs("a.rws")), SaveMode.None);
        var off = new StatsOptions { IncludeDevMode = false };
        var v1 = session.GetView(off);
        Assert.Same(v1, session.GetView(off));
        Assert.Same(v1, session.GetView(new StatsOptions { IncludeDevMode = false }));   // equal options, same view
        Assert.NotSame(v1, session.GetView(new StatsOptions { IncludeDevMode = true }));
        Assert.Equal(1, v1.Stats.ColonyCount);
        session.Mutate(p => p.SaveObservations.Add(Obs("b.rws", "Thing_Human9")), SaveMode.None);
        var v2 = session.GetView(off);
        Assert.NotSame(v1, v2);
        Assert.Equal(2, v2.Stats.ColonyCount);
        Assert.Equal(1, v1.Stats.ColonyCount);   // a view already handed out does not change
    }

    [Fact]
    public void Mutate_WhenReadOnly_DoesNotWriteAndRecordsError()
    {
        using var dir = new TempDir();
        const string json = """{ "schemaVersion": 3, "saveObservations": [] }""";
        File.WriteAllText(dir.File("profile.json"), json);
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        Assert.True(session.LoadResult.ReadOnly);
        session.Mutate(p => p.SaveObservations.Add(Obs("a.rws")), SaveMode.Immediate);   // does not throw
        Assert.Equal(json, File.ReadAllText(dir.File("profile.json")));
        Assert.IsType<InvalidOperationException>(session.LastSaveError);
        session.Mutate(p => p.SaveObservations.Add(Obs("b.rws")), SaveMode.Background);
        session.Flush();
        Assert.Equal(json, File.ReadAllText(dir.File("profile.json")));
        Assert.Equal(2, session.Read(p => p.SaveObservations.Count));   // still usable in memory
    }

    [Fact]
    public void GetView_NewCollisionKeys_ArePersisted()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        session.Mutate(p =>
        {
            p.SaveObservations.Add(Obs("a.rws", "Thing_Human1,Thing_Human2"));
            p.SaveObservations.Add(Obs("b.rws", "Thing_Human1,Thing_Human3"));
        }, SaveMode.Immediate);
        Assert.Empty(Reload(dir).ColonyKeys);
        Assert.Equal(2, session.GetView(new StatsOptions()).Colonies.Count);
        session.Flush();
        Assert.Equal(new[] { "#2", "" }, Reload(dir).ColonyKeys.Select(k => k.Key.Contains('#') ? k.Key[k.Key.IndexOf('#')..] : "").OrderByDescending(s => s));
    }
}
