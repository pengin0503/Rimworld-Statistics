namespace RimworldStatistics.Core.Tests;

public sealed class ProfileMutationsTests
{
    private static readonly DateTime t0 = new(2025, 5, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime t1 = t0.AddHours(3);

    private static ColonyIdentity Identity(string colony, string pawns)
        => new()
        {
            SeedString = "seed-" + colony, PersistentRandomValue = 42, StartingTile = "10,0", GameStartAbsTick = 0,
            FirstWealthBits = BitConverter.SingleToUInt32Bits(9000f), StartingPawnIds = pawns.Split(',').ToList(),
        };

    private static SaveObservation Obs(string file, DateTime time, string colony = "A", int ticks = 1000, string pawns = "Thing_Human1",
        int raids = 0, double wealthMax = 100, double realPlay = 10, long size = 500, int reader = SaveReader.Version,
        ObservationStatus status = ObservationStatus.Ok, params EndingType[] traces)
        => new()
        {
            FileName = file, FileSize = size, LastWriteUtc = time, ReaderVersion = reader, Status = status,
            Identity = Identity(colony, pawns),
            Values = new ObservedValues { TicksGame = ticks, NumRaidsEnemy = raids, WealthMax = wealthMax, RealPlayTime = realPlay },
            EndingTraces = traces.ToList(),
        };

    private static EndingEvent Ev(string campaign, EndingType type, int ordinal, bool dev, int departed, int total)
        => new() { CampaignId = campaign, Type = type, Ordinal = ordinal, DevMode = dev, Departed = departed, LaunchedTotalAfter = total };

    [Fact]
    public void NeedsRead_FalseOnlyForSameContentOkObservationWithCurrentReader()
    {
        var p = new Profile();
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, size: 500));
        Assert.False(ProfileMutations.NeedsRead(p, "a.rws", 500, t0, SaveReader.Version));
        Assert.True(ProfileMutations.NeedsRead(p, "a.rws", 500, t1, SaveReader.Version));       // time changed
        Assert.True(ProfileMutations.NeedsRead(p, "a.rws", 501, t0, SaveReader.Version));       // size changed
        Assert.True(ProfileMutations.NeedsRead(p, "a.rws", 500, t0, SaveReader.Version + 1));   // newer reader
        Assert.True(ProfileMutations.NeedsRead(p, "new.rws", 500, t0, SaveReader.Version));     // never seen

        var err = new Profile();
        ProfileMutations.AddSaveObservation(err, Obs("e.rws", t0, status: ObservationStatus.Error));
        Assert.True(ProfileMutations.NeedsRead(err, "e.rws", 500, t0, SaveReader.Version));     // failures are retried

        var old = new Profile();
        var superseded = Obs("s.rws", t0); superseded.Superseded = true;
        old.SaveObservations.Add(superseded);
        Assert.True(ProfileMutations.NeedsRead(old, "s.rws", 500, t0, SaveReader.Version));     // only a superseded record

        // Same content and not a failure: reading again gives the same answer (spec §7.1), so it is not re-read.
        var unid = new Profile();
        ProfileMutations.AddSaveObservation(unid, Obs("u.rws", t0, status: ObservationStatus.Unidentifiable));
        Assert.False(ProfileMutations.NeedsRead(unid, "u.rws", 500, t0, SaveReader.Version));
    }

    [Fact]
    public void Overwrite_ByDifferentColony_KeepsOldAsSuperseded()
    {
        var p = new Profile();
        ProfileMutations.AddSaveObservation(p, Obs("Autosave-1.rws", t0, colony: "A", ticks: 500000));
        ProfileMutations.AddSaveObservation(p, Obs("Autosave-1.rws", t1, colony: "B", ticks: 10000));
        Assert.Equal(2, p.SaveObservations.Count);
        Assert.True(p.SaveObservations.Single(o => o.Values.TicksGame == 500000).Superseded);
        Assert.False(p.SaveObservations.Single(o => o.Values.TicksGame == 10000).Superseded);
        Assert.Equal(2, CampaignMatcher.BuildColonies(p).Count);   // colony A's numbers stay counted
    }

    [Fact]
    public void Overwrite_BySameColonyDominating_DropsOld()
    {
        var p = new Profile();
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, ticks: 1000, raids: 1, wealthMax: 100, realPlay: 10, traces: EndingType.Ship));
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t1, ticks: 2000, raids: 1, wealthMax: 150, realPlay: 20, traces: new[] { EndingType.Ship, EndingType.VoidEmbraced }));
        var only = Assert.Single(p.SaveObservations);
        Assert.Equal(2000, only.Values.TicksGame);
        Assert.False(only.Superseded);
    }

    [Theory]
    [InlineData("ticks")]
    [InlineData("raids")]
    [InlineData("wealth")]
    [InlineData("realPlay")]
    [InlineData("traces")]
    public void Overwrite_BySameColonyNotDominatingInOneField_KeepsBoth(string lower)
    {
        var p = new Profile();
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, ticks: 1000, raids: 2, wealthMax: 100, realPlay: 10, traces: EndingType.Ship));
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t1,
            ticks: lower == "ticks" ? 999 : 5000, raids: lower == "raids" ? 1 : 2, wealthMax: lower == "wealth" ? 99 : 100,
            realPlay: lower == "realPlay" ? 9 : 10, traces: lower == "traces" ? Array.Empty<EndingType>() : new[] { EndingType.Ship }));
        Assert.Equal(2, p.SaveObservations.Count);
        Assert.Single(p.SaveObservations, o => o.Superseded);
    }

    [Fact]
    public void Overwrite_DominatingButFewerPawns_KeepsOldAsEvidence()
    {
        var p = new Profile();
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, ticks: 1000, pawns: "A,B,C"));
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t1, ticks: 9000, raids: 5, wealthMax: 999, realPlay: 99, pawns: "A,B"));
        Assert.Equal(2, p.SaveObservations.Count);
        Assert.True(p.SaveObservations.Single(o => o.Identity.StartingPawnIds.Count == 3).Superseded);
        ProfileMutations.AddSaveObservation(p, Obs("other.rws", t0, pawns: "A,C"));
        Assert.Single(CampaignMatcher.BuildColonies(p));   // {A,B,C} still joins {A,B} and {A,C}
    }

    [Fact]
    public void Overwrite_BySameColonyNotDominating_KeepsBoth()
    {
        var p = new Profile();
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, ticks: 900000));
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t1, ticks: 300000));   // an older save loaded and saved over the name
        Assert.Equal(2, p.SaveObservations.Count);
        Assert.True(p.SaveObservations.Single(o => o.Values.TicksGame == 900000).Superseded);
    }

    [Fact]
    public void Overwrite_SameCampaignIdWithoutFingerprint_DropsDominatedOld()
    {
        var p = new Profile();
        var a = Obs("a.rws", t0, ticks: 100); a.Identity = new ColonyIdentity { ModCampaignId = "g1", StartingPawnIds = { "Thing_Human1" } };
        var b = Obs("a.rws", t1, ticks: 200); b.Identity = new ColonyIdentity { ModCampaignId = "g1", StartingPawnIds = { "Thing_Human1" } };
        ProfileMutations.AddSaveObservation(p, a);
        ProfileMutations.AddSaveObservation(p, b);
        Assert.Equal(200, Assert.Single(p.SaveObservations).Values.TicksGame);
    }

    [Fact]
    public void SameContent_NewerReaderVersion_Replaces()
    {
        var p = new Profile();
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, ticks: 1, reader: 1));
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, ticks: 2, reader: 2));
        Assert.Equal((2, 2, false), (Assert.Single(p.SaveObservations).ReaderVersion, p.SaveObservations[0].Values.TicksGame, p.SaveObservations[0].Superseded));
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, ticks: 3, reader: 2));   // same version: nothing to replace
        Assert.Equal(2, Assert.Single(p.SaveObservations).Values.TicksGame);
    }

    [Fact]
    public void ErrorObservation_IsReplacedByAnyNewRead()
    {
        var p = new Profile();
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, status: ObservationStatus.Error));
        ProfileMutations.AddSaveObservation(p, Obs("a.rws", t0, ticks: 7));               // same content, re-read fine
        Assert.Equal((ObservationStatus.Ok, 7), (Assert.Single(p.SaveObservations).Status, p.SaveObservations[0].Values.TicksGame));

        var q = new Profile();
        ProfileMutations.AddSaveObservation(q, Obs("a.rws", t0, status: ObservationStatus.Error));
        ProfileMutations.AddSaveObservation(q, Obs("a.rws", t1, colony: "B"));             // content changed, other colony
        Assert.False(Assert.Single(q.SaveObservations).Superseded);                        // no Error kept as history
    }

    private static ObservedValues V(int ticks, double wealthLast, int raids = 0, double wealthMax = 0)
        => new() { TicksGame = ticks, WealthLast = wealthLast, NumRaidsEnemy = raids, WealthMax = wealthMax };

    [Fact]
    public void ApplyLive_TracksMaxAndWealthLastAtMaxTicks()
    {
        var p = new Profile();
        var id = Identity("A", "Thing_Human1");
        ProfileMutations.ApplyLiveObservation(p, "g", id, V(100, 10, raids: 5, wealthMax: 50), new DisplayInfo { FactionName = "early" }, t0);
        ProfileMutations.ApplyLiveObservation(p, "g", id, V(300, 30, raids: 2, wealthMax: 70), new DisplayInfo { FactionName = "late" }, t0.AddHours(1));
        ProfileMutations.ApplyLiveObservation(p, "g", id, V(200, 99, raids: 1, wealthMax: 60), new DisplayInfo { FactionName = "reloaded" }, t0.AddHours(2));
        var c = Assert.Single(p.LiveCampaigns);
        Assert.Equal((300, 30.0, 5, 70.0), (c.Max.TicksGame, c.Max.WealthLast, c.Max.NumRaidsEnemy, c.Max.WealthMax));
        Assert.Equal((200, 99.0), (c.Latest.TicksGame, c.Latest.WealthLast));
        Assert.Equal("late", c.Display.FactionName);   // the display of the furthest point, like observations (spec §9.2)
        Assert.Equal(t0.AddHours(2), c.LastSavedUtc);
    }

    [Fact]
    public void ApplyLive_FillsMissingFingerprintFields_UnionsPawns()
    {
        var p = new Profile();
        var early = Identity("A", "Thing_Human1,Thing_Human2"); early.FirstWealthBits = null;   // new game: no history record yet
        ProfileMutations.RegisterLiveCampaign(p, "g", early, new DisplayInfo(), t0);
        Assert.False(p.LiveCampaigns[0].Identity.HasFingerprint);
        var later = Identity("A", "Thing_Human2"); later.SeedString = "different";
        ProfileMutations.ApplyLiveObservation(p, "g", later, V(30000, 9000), new DisplayInfo(), t1);
        var id = Assert.Single(p.LiveCampaigns).Identity;
        Assert.Equal(BitConverter.SingleToUInt32Bits(9000f), id.FirstWealthBits);   // filled
        Assert.Equal("seed-A", id.SeedString);                                       // present values are kept
        Assert.Equal(new[] { "Thing_Human1", "Thing_Human2" }, id.StartingPawnIds.OrderBy(x => x));
    }

    [Fact]
    public void RegisterLiveCampaign_IsIdempotent()
    {
        var p = new Profile();
        ProfileMutations.RegisterLiveCampaign(p, "g", Identity("A", "Thing_Human1"), new DisplayInfo { WorldName = "w" }, t0);
        ProfileMutations.ApplyLiveObservation(p, "g", Identity("A", "Thing_Human1"), V(500, 1), new DisplayInfo { WorldName = "w" }, t1);
        ProfileMutations.RegisterLiveCampaign(p, "g", Identity("A", "Thing_Human1"), new DisplayInfo { WorldName = "w" }, t1.AddDays(1));
        var c = Assert.Single(p.LiveCampaigns);
        Assert.Equal(500, c.Max.TicksGame);   // registering again does not reset what was recorded

        var q = new Profile();   // display belongs to the save-time observation, never to a re-registration
        ProfileMutations.RegisterLiveCampaign(q, "g", Identity("A", "Thing_Human1"), new DisplayInfo { FactionName = "first" }, t0);
        ProfileMutations.RegisterLiveCampaign(q, "g", Identity("A", "Thing_Human1"), new DisplayInfo { FactionName = "again" }, t1);
        Assert.Equal("first", Assert.Single(q.LiveCampaigns).Display.FactionName);
    }

    [Fact]
    public void ApplyLive_TakesWorldValuesOnlyWhenTheSeedWasUnknown()
    {
        var p = new Profile();
        ProfileMutations.RegisterLiveCampaign(p, "g", new ColonyIdentity { StartingPawnIds = { "Thing_Human1" } }, new DisplayInfo(), t0);
        ProfileMutations.ApplyLiveObservation(p, "g", Identity("A", "Thing_Human1"), V(10, 1), new DisplayInfo(), t1);
        var id = p.LiveCampaigns[0].Identity;
        Assert.Equal(("seed-A", 42, true), (id.SeedString, id.PersistentRandomValue, id.HasFingerprint));
        var other = Identity("A", "Thing_Human1"); other.PersistentRandomValue = 7;
        ProfileMutations.ApplyLiveObservation(p, "g", other, V(20, 1), new DisplayInfo(), t1);
        Assert.Equal(42, p.LiveCampaigns[0].Identity.PersistentRandomValue);
    }

    [Fact]
    public void UpsertEnding_SameKeyAndProvenance_KeepsMax()
    {
        var p = new Profile();
        Assert.True(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: false, departed: 2, total: 2)));
        Assert.False(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: false, departed: 3, total: 5)));
        Assert.False(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: false, departed: 1, total: 1)));
        var e = Assert.Single(p.EndingEvents);
        Assert.Equal(3, e.Departed);
        Assert.Equal(5, e.LaunchedTotalAfter);
        Assert.True(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 1, dev: false, departed: 1, total: 6)));   // second ship
        Assert.True(ProfileMutations.UpsertEnding(p, Ev("h", EndingType.Ship, 0, dev: false, departed: 1, total: 1)));   // other campaign
    }

    [Fact]
    public void UpsertEnding_DevAndNormal_AreKeptSeparately()
    {
        var p = new Profile();
        Assert.True(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: true, departed: 100, total: 100)));
        Assert.True(ProfileMutations.UpsertEnding(p, Ev("g", EndingType.Ship, 0, dev: false, departed: 1, total: 1)));
        Assert.Equal(2, p.EndingEvents.Count);
        Assert.Equal(1, p.EndingEvents.Single(e => !e.DevMode).Departed);   // the dev 100 does not leak in
    }

    [Fact]
    public void SetExcluded_AddsPrimaryOnce_RemovesAllAliases()
    {
        var p = new Profile();
        ProfileMutations.SetExcluded(p, new[] { "fp:a", "fp:a#2" }, true);
        ProfileMutations.SetExcluded(p, new[] { "fp:a", "fp:a#2" }, true);
        Assert.Equal(new[] { "fp:a" }, p.UserDecisions.ExcludedCampaignKeys);

        var q = new Profile();
        q.UserDecisions.ExcludedCampaignKeys.Add("fp:a#2");
        ProfileMutations.SetExcluded(q, new[] { "fp:a", "fp:a#2" }, false);
        Assert.Empty(q.UserDecisions.ExcludedCampaignKeys);
    }
}
