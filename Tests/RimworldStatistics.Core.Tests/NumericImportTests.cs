using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>Import of Phase A observations: one observation per content, never lost to later failures (plan A2).</summary>
public sealed class NumericImportTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T1 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T0 = new(2025, 12, 31, 0, 0, 0, DateTimeKind.Utc);

    private static byte[] Save(params float[] wealth) => Save(2, wealth);

    private static byte[] Save(int mods, params float[] wealth)
        => new SaveXmlBuilder().Meta("1.7.4985 rev1269", mods).Info(realPlay: 10f, tile: "100,0", pawns: "Thing_Human1")
            .Tick(ticksGame: 30000 * (wealth.Length - 1), start: 0).World(seed: "seed", prv: 1).WealthTotal(wealth).Build();

    private static void Write(TempDir dir, string name, byte[] bytes, DateTime time)
    {
        File.WriteAllBytes(dir.File(name), bytes);
        File.SetLastWriteTimeUtc(dir.File(name), time);
    }

    private static ProfileSession NewSession(TempDir store) => new(new ProfileStore(store.Path), () => Now);

    private static ImportResult Run(TempDir saves, ProfileSession s)
        => new ImportEngine(new SaveReader()).Run(saves.Path, s, null, TestContext.Current.CancellationToken);

    private static List<MetricObservation> Metrics(ProfileSession s) => s.Read(p => p.MetricObservations.ToList());

    [Fact]
    public void Copies_ShareOneObservation_WithEarliestTime()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var bytes = Save(100f, 150f);
        Write(saves, "a.rws", bytes, T1);
        Write(saves, "b-copy.rws", bytes, T0);
        Write(saves, "c-copy.rws", bytes, T2);
        var s = NewSession(store);
        Run(saves, s);
        var m = Assert.Single(Metrics(s));
        Assert.Equal(new[] { "a.rws", "b-copy.rws", "c-copy.rws" }, m.FileAliases.Select(a => a.FileName).Order(StringComparer.Ordinal));
        Assert.Equal(T0, m.ObservedUtc);
        Assert.Equal(3, s.Read(p => p.SaveObservations.Count));   // v1 still keeps one record per file
    }

    [Fact]
    public void SameNameNewContent_IsAnotherObservation_AndTheOldOneStays()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var s = NewSession(store);
        Write(saves, "a.rws", Save(100f, 150f), T1);
        Run(saves, s);
        var first = Assert.Single(Metrics(s)).Key;
        Write(saves, "a.rws", Save(3, 100f, 150f, 180f), T2);   // another mod count keeps the earlier observation relevant
        Run(saves, s);
        var all = Metrics(s);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, m => m.Key == first);
        Assert.Equal(new[] { 30000, 60000 }, all.Select(m => m.SaveTick).Order());
    }

    [Fact]
    public void SecondScan_DoesNotReadUnchangedFiles()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var s = NewSession(store);
        Write(saves, "a.rws", Save(100f, 150f), T1);
        Run(saves, s);
        var before = ProfileJson.Serialize(s.Read(p => p));
        var r = Run(saves, s);
        Assert.Equal((0, 1), (r.Read, r.Skipped));
        Assert.Single(Metrics(s));
        Assert.Equal(System.Text.Json.Nodes.JsonNode.Parse(before)!["metricObservations"]!.ToJsonString(),
            System.Text.Json.Nodes.JsonNode.Parse(ProfileJson.Serialize(s.Read(p => p)))!["metricObservations"]!.ToJsonString());
    }

    [Fact]
    public void DeletedFile_KeepsItsObservation()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var s = NewSession(store);
        Write(saves, "a.rws", Save(100f, 150f), T1);
        Run(saves, s);
        File.Delete(saves.File("a.rws"));
        Run(saves, s);
        Assert.Single(Metrics(s));
    }

    [Fact]
    public void FailedOverwrite_KeepsTheEarlierObservation_AndRetryAddsTheNewOne()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var s = NewSession(store);
        Write(saves, "a.rws", Save(100f, 150f), T1);
        Run(saves, s);
        var good = Assert.Single(Metrics(s));
        Write(saves, "a.rws", new byte[] { 1, 2, 3 }, T2);   // overwritten by something unreadable
        Run(saves, s);
        Assert.Equal(good.Key, Assert.Single(Metrics(s)).Key);
        Write(saves, "a.rws", Save(3, 100f, 150f, 200f), T2.AddMinutes(1));
        Run(saves, s);
        Assert.Equal(2, Metrics(s).Count);
    }

    [Fact]
    public void OlderReaderObservation_IsReadAgain()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var bytes = Save(100f, 150f);
        Write(saves, "a.rws", bytes, T1);
        var s = NewSession(store);
        s.Mutate(p => p.SaveObservations.Add(new SaveObservation
        {
            FileName = "a.rws", FileSize = bytes.Length, LastWriteUtc = T1, ReaderVersion = 3, Status = ObservationStatus.Ok,
        }), SaveMode.None);
        var r = Run(saves, s);
        Assert.Equal(1, r.Read);
        Assert.Single(Metrics(s));
        Assert.Equal(SaveReader.Version, s.Read(p => p.SaveObservations.Single().ReaderVersion));
    }

    [Fact]
    public void Upsert_MergesAliasesAndKeepsGoodValues()
    {
        var p = new Profile();
        MetricObservation Obs(string file, DateTime time, MetricValue v, int revision = 1) => new()
        {
            Source = MetricObservationSource.SaveFile, Key = "k", FormatVersion = 4, ObservedUtc = time,
            FileAliases = { new MetricFileAlias { FileName = file, FileSize = 1, LastWriteUtc = time } },
            Metrics = { ["x"] = MetricEntry.Of(revision, v) },
        };
        ProfileMutations.UpsertMetricObservation(p, Obs("a.rws", T1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 5 }));
        ProfileMutations.UpsertMetricObservation(p, Obs("a.rws", T1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 5 }));
        ProfileMutations.UpsertMetricObservation(p, Obs("b.rws", T0, new MetricValue { Coverage = MetricCoverage.Unknown }));
        var m = Assert.Single(p.MetricObservations);
        Assert.Equal(new[] { "a.rws", "b.rws" }, m.FileAliases.Select(a => a.FileName));
        Assert.Equal(T0, m.ObservedUtc);
        Assert.Equal(5L, m.Metrics["x"].Values[""].IntegerValue);    // a worse re-read never hides a good value

        ProfileMutations.UpsertMetricObservation(p, Obs("a.rws", T1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 7 }, revision: 2));
        Assert.Equal((2, 7L), (m.Metrics["x"].Revision, m.Metrics["x"].Values[""].IntegerValue));   // a newer definition replaces, never mixes
        ProfileMutations.UpsertMetricObservation(p, Obs("a.rws", T1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 9 }, revision: 1));
        Assert.Equal((2, 7L), (m.Metrics["x"].Revision, m.Metrics["x"].Values[""].IntegerValue));
    }

    private static MetricObservation Obs(string key, int tick, long? mods, string pawns = "Thing_Human1", string seed = "seed")
    {
        var m = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = key, SaveTick = tick, ObservedUtc = T1,
            Identity = new ColonyIdentity { SeedString = seed, StartingTile = "1,0", FirstWealthBits = 1, StartingPawnIds = pawns.Split(',').ToList() },
        };
        if (mods is { } n)
            m.Metrics[MetricKeys.ModsLatest] = MetricEntry.Of(1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = n });
        return m;
    }

    [Fact]
    public void Upsert_DropsAnEarlierObservationOfTheSameIdentity_WhenNothingCanChange()
    {
        // Autosaves of one session: the latest one is always the representative and the mod count is the same.
        var p = new Profile();
        ProfileMutations.UpsertMetricObservation(p, Obs("a", 60000, 5));
        ProfileMutations.UpsertMetricObservation(p, Obs("b", 120000, 5));
        ProfileMutations.UpsertMetricObservation(p, Obs("c", 90000, 5));     // older content read later
        Assert.Equal(new[] { "b" }, p.MetricObservations.Select(o => o.Key));
    }

    [Theory]
    [InlineData(60000, 4L, "Thing_Human1", "seed")]          // another mod count is a cross-observation extreme
    [InlineData(120000, 5L, "Thing_Human1", "seed")]         // same tick: the choice depends on time and key
    [InlineData(60000, 5L, "Thing_Human1,Thing_Human2", "seed")]   // other starting pawns: possibly another branch
    [InlineData(60000, 5L, "Thing_Human1", "other")]         // another colony
    public void Upsert_KeepsObservationsThatCouldMatter(int olderTick, long olderMods, string pawns, string seed)
    {
        var p = new Profile();
        ProfileMutations.UpsertMetricObservation(p, Obs("old", olderTick, olderMods, pawns, seed));
        ProfileMutations.UpsertMetricObservation(p, Obs("new", 120000, 5));
        Assert.Equal(2, p.MetricObservations.Count);
    }

    [Fact]
    public void Upsert_ReplacesQuarantinedEntry()
    {
        var p = new Profile();
        var existing = new MetricObservation { Source = MetricObservationSource.SaveFile, Key = "k" };
        existing.Metrics["x"] = new MetricEntry { Quarantined = System.Text.Json.JsonDocument.Parse("{\"bad\":1}").RootElement.Clone() };
        p.MetricObservations.Add(existing);
        ProfileMutations.UpsertMetricObservation(p, new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "k",
            Metrics = { ["x"] = MetricEntry.Of(1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 1 }) },
        });
        Assert.False(p.MetricObservations.Single().Metrics["x"].IsQuarantined);
    }
}
