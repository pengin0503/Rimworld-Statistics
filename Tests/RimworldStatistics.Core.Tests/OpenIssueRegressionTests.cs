namespace RimworldStatistics.Core.Tests;

public sealed class OpenIssueRegressionTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void StaleSessionCannotOverwriteAnotherSessionsRecord()
    {
        using var dir = new Fixtures.TempDir();
        var store = new ProfileStore(dir.Path);
        var first = new ProfileSession(store, () => Now);
        var second = new ProfileSession(new ProfileStore(dir.Path), () => Now);

        first.Mutate(p => p.EndingEvents.Add(new EndingEvent { CampaignId = "first" }), SaveMode.Immediate);
        second.Mutate(p => p.EndingEvents.Add(new EndingEvent { CampaignId = "second" }), SaveMode.Immediate);

        Assert.Equal("first", Assert.Single(store.Load(Now).Profile.EndingEvents).CampaignId);
        Assert.NotNull(second.LastSaveError);
    }

    [Fact]
    public void ReplacementFailureKeepsOlderBackups()
    {
        using var dir = new Fixtures.TempDir();
        var store = new ProfileStore(dir.Path);
        for (var n = 0; n < 4; n++)
            store.Save(new Profile { EndingEvents = [new EndingEvent { Ordinal = n }] });

        var backup = store.ProfilePath + ".bak1";
        File.Move(backup, backup + ".aside");
        Directory.CreateDirectory(backup);
        var older = File.ReadAllText(store.ProfilePath + ".bak3");

        Assert.ThrowsAny<IOException>(() => store.Save(new Profile()));
        Assert.Equal(older, File.ReadAllText(store.ProfilePath + ".bak3"));
        Assert.Equal(3, Assert.Single(store.Load(Now).Profile.EndingEvents).Ordinal);
    }

    [Fact]
    public void IdenticalStatisticsAndScanTimestampDoNotRotateBackups()
    {
        using var dir = new Fixtures.TempDir();
        var store = new ProfileStore(dir.Path);
        var session = new ProfileSession(store, () => Now);
        session.Mutate(p => p.EndingEvents.Add(new EndingEvent { Ordinal = 1 }), SaveMode.Immediate);
        session.Mutate(p => p.EndingEvents.Add(new EndingEvent { Ordinal = 2 }), SaveMode.Immediate);
        var older = File.ReadAllText(store.ProfilePath + ".bak1");

        session.Mutate(_ => { }, SaveMode.Immediate);
        session.Mutate(p => p.ImportState.LastScanUtc = Now, SaveMode.Immediate);

        Assert.Equal(older, File.ReadAllText(store.ProfilePath + ".bak1"));
        Assert.Equal(Now, store.Load(Now).Profile.ImportState.LastScanUtc);
    }

    private static SaveObservation Observation(string name, DateTime time, int ticks, params string[] pawns)
        => new()
        {
            FileName = name, FileSize = ticks, LastWriteUtc = time, ReaderVersion = SaveReader.Version,
            Status = ObservationStatus.Ok, Identity = new ColonyIdentity
            {
                SeedString = "shared", StartingTile = "10,0", FirstWealthBits = 100, StartingPawnIds = [.. pawns]
            },
            Values = new ObservedValues { TicksGame = ticks }
        };

    [Fact]
    public void OverwritePreservesAmbiguousBranchesOriginalAssignment()
    {
        var p = new Profile { SaveObservations = [
            Observation("a.rws", Now, 50, "A", "B"),
            Observation("b.rws", Now.AddHours(1), 100, "A", "C"),
            Observation("uncertain.rws", Now.AddHours(2), 200, "A")] };
        ProfileMutations.AddSaveObservation(p, Observation("a.rws", Now.AddHours(3), 60, "A", "B"));
        Assert.Equal(300, StatsCalculator.Calculate(CampaignMatcher.BuildColonies(p), new()).TotalTicks);
    }

    [Fact]
    public void UnidentifiableRereadKeepsPreviouslyCountedData()
    {
        var good = Observation("save.rws", Now, 60000, "A");
        good.ReaderVersion--;
        var p = new Profile { SaveObservations = [good] };
        var bad = Observation("save.rws", Now, 60000);
        bad.Status = ObservationStatus.Unidentifiable;
        bad.Identity = new();
        ProfileMutations.AddSaveObservation(p, bad);
        Assert.Equal(60000, StatsCalculator.Calculate(CampaignMatcher.BuildColonies(p), new()).TotalTicks);
        Assert.Same(bad, Assert.Single(p.SaveObservations, o => !o.Superseded));
    }

    [Fact]
    public void LiveReplayPromotesEstimatedEnding()
    {
        var p = new Profile();
        ProfileMutations.UpsertEnding(p, new EndingEvent { CampaignId = "c", Source = EndingSource.SeededTrace });
        ProfileMutations.UpsertEnding(p, new EndingEvent { CampaignId = "c", Source = EndingSource.Live, Departed = 4, RealTimeUtc = Now });
        var result = Assert.Single(p.EndingEvents);
        Assert.Equal((EndingSource.Live, 4, Now), (result.Source, result.Departed, result.RealTimeUtc));
    }

    [Fact]
    public void LoadedRegistrationDoesNotMoveLastPlayedDate()
    {
        var o = Observation("save.rws", Now, 60000, "A");
        var p = new Profile { SaveObservations = [o] };
        ProfileMutations.RegisterLiveCampaign(p, "c", o.Identity, new());
        Assert.Equal(Now, StatsCalculator.Calculate(CampaignMatcher.BuildColonies(p), new()).Colonies.Single().LastPlayedUtc);
    }

    [Fact]
    public void SeededEndingRetainsRegisteredColonyName()
    {
        var p = new Profile();
        ProfileMutations.RegisterLiveCampaign(p, "c", new ColonyIdentity(), new DisplayInfo { FactionName = "Known" });
        ProfileMutations.UpsertEnding(p, new EndingEvent { CampaignId = "c", Source = EndingSource.SeededTrace });
        var result = StatsCalculator.Calculate(CampaignMatcher.BuildColonies(p), new());
        Assert.Equal("Known", Assert.Single(result.Colonies).FactionName);
    }

    [Fact]
    public void ChangingCultureKeepsPreviouslyExcludedColony()
    {
        var prior = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            var o = Observation("save.rws", Now, 10);
            o.Identity.PersistentRandomValue = -4321;
            var p = new Profile { SaveObservations = [o] };
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("sv-SE");
            const string legacyHash = "f3159081d4282964";
            p.ColonyKeys.Add(new ColonyKeyEntry { FingerprintHash = legacyHash, Key = "fp:" + legacyHash });
            p.UserDecisions.ExcludedCampaignKeys.Add("fp:" + legacyHash);
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            Assert.True(Assert.Single(CampaignMatcher.BuildColonies(p)).Excluded);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = prior;
        }
    }

    [Fact]
    public void MalformedValuesDoNotPoisonOtherColonies()
    {
        var a = Observation("a.rws", Now, 20);
        var b = Observation("b.rws", Now, 20);
        b.Identity.SeedString = "other";
        a.Values.RealPlayTime = double.PositiveInfinity;
        b.Values.RealPlayTime = 300;
        a.Values.NumRaidsEnemy = int.MaxValue;
        b.Values.NumRaidsEnemy = 1;
        var result = StatsCalculator.Calculate(CampaignMatcher.BuildColonies(new Profile { SaveObservations = [a, b] }), new());
        Assert.Equal(300, result.TotalRealPlay);
        Assert.Equal(2147483648L, result.Raids);
    }

    [Fact]
    public void MissingWealthAndObservedZeroAreDistinguished()
    {
        var missing = Observation("missing.rws", Now, 10);
        var zero = Observation("zero.rws", Now, 10);
        zero.Identity.SeedString = "zero";
        zero.Values.WealthPoints = 1;
        var stats = StatsCalculator.Calculate(CampaignMatcher.BuildColonies(new Profile { SaveObservations = [missing, zero] }), new());
        Assert.Equal(1, stats.PeakWealthColonyCount);
        Assert.Equal(1, stats.LastWealthColonyCount);
        Assert.Equal(0, stats.PeakWealthSum);
    }

    [Fact]
    public void ImportedLineageRestoresDistinctOrdinalsWithoutAssumingNormalPlay()
    {
        var builder = new Fixtures.SaveXmlBuilder().Minimal().Stats(launched: 2)
            .Component(SaveReader.ComponentClassName,
                new System.Xml.Linq.XElement("campaignId", "c"),
                new System.Xml.Linq.XElement("lineageEndings",
                    new System.Xml.Linq.XElement("li", "ship|0|10"),
                    new System.Xml.Linq.XElement("li", "ship|1|20")));
        var data = builder.Build();
        using var stream = new MemoryStream(data);
        var observation = new SaveReader().Read(stream, "ending.rws", data.Length, Now);
        var p = new Profile { SaveObservations = [observation] };
        p.EndingEvents.Add(new EndingEvent { CampaignId = "c", Type = EndingType.Ship, Ordinal = 1 });
        var colony = Assert.Single(CampaignMatcher.BuildColonies(p));
        Assert.Equal(0, Assert.Single(colony.RecoveredEndings).Ordinal);
        Assert.Equal(1, StatsCalculator.CalculateColony(colony, new()).EndingCounts[EndingType.Ship]);
    }

    [Fact]
    public void SaveFileIndexDetectsOverwriteAndDeletion()
    {
        using var dir = new Fixtures.TempDir();
        var path = Path.Combine(dir.Path, "slot.rws");
        File.WriteAllText(path, "old");
        var original = new SaveObservation
        {
            FileName = "slot.rws", FileSize = 3, LastWriteUtc = File.GetLastWriteTimeUtc(path)
        };
        Assert.True(SaveFileIndex.Read(dir.Path).Contains(original));
        File.WriteAllText(path, "different content");
        Assert.False(SaveFileIndex.Read(dir.Path).Contains(original));
        File.Delete(path);
        Assert.False(SaveFileIndex.Read(dir.Path).Contains(original));
    }
}
