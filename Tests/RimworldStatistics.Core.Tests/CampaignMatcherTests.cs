namespace RimworldStatistics.Core.Tests;

public sealed class CampaignMatcherTests
{
    private static readonly DateTime T0 = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string Pawns(string ids) => string.Join(",", ids.Split(',').Select(i => i.StartsWith("Thing_") ? i : "Thing_Human" + i));

    private static ColonyIdentity Id(string seed = "s", int prv = 7, string tile = "1,0", int start = 0, float wealth0 = 12560f, string pawns = "1", string? id = null)
        => new()
        {
            SeedString = seed, PersistentRandomValue = prv, StartingTile = tile, GameStartAbsTick = start,
            FirstWealthBits = BitConverter.SingleToUInt32Bits(wealth0), StartingPawnIds = Pawns(pawns).Split(',').ToList(), ModCampaignId = id,
        };

    private static SaveObservation Obs(string file, string seed = "s", string tile = "1,0", int start = 0, float wealth0 = 12560f, string pawns = "1", string? id = null, int ticks = 1000, int day = 0)
        => new()
        {
            FileName = file, FileSize = 100, LastWriteUtc = T0.AddDays(day), ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok,
            Identity = Id(seed, 7, tile, start, wealth0, pawns, id), Values = new ObservedValues { TicksGame = ticks },
        };

    private static readonly string Hash = Id().FingerprintHash()!;

    private static Profile Profile(params SaveObservation[] obs)
    {
        var p = new Profile();
        p.SaveObservations.AddRange(obs);
        return p;
    }

    private static IEnumerable<string> Files(Colony c) => c.Observations.Select(o => o.FileName).OrderBy(f => f, StringComparer.Ordinal);

    [Fact]
    public void ExampleA_PermadeathRename_Merges()
    {
        var c = Assert.Single(CampaignMatcher.BuildColonies(Profile(
            Obs("人々派契約国「バットストバ」 (パーマデス).rws.old", pawns: "891,951,1047", day: 0),
            Obs("rim共同体 (パーマデス).rws", pawns: "891", day: 3))));
        Assert.Equal(2, c.Observations.Count);
        Assert.Equal($"fp:{Hash}", c.Key);
        Assert.Equal(Hash, c.FingerprintHash);
        Assert.Equal(new[] { "Thing_Human1047", "Thing_Human891", "Thing_Human951" }, c.StartingPawnUnion.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void ExampleB_SameWorldDifferentStart_Separates()
    {
        var colonies = CampaignMatcher.BuildColonies(Profile(Obs("a.rws", wealth0: 12560f), Obs("b.rws", wealth0: 13212f)));
        Assert.Equal(2, colonies.Count);
        Assert.NotEqual(colonies[0].Key, colonies[1].Key);
        Assert.All(colonies, c => Assert.StartsWith("fp:", c.Key));
    }

    [Fact]
    public void ExampleC_EarlyAndLateSavesWithDifferentNames_Merge()
    {
        var c = Assert.Single(CampaignMatcher.BuildColonies(Profile(
            Obs("序盤.rws", pawns: "5,6,7", ticks: 60000), Obs("Autosave-3.rws", pawns: "5,6,7", ticks: 9_000_000, day: 40))));
        Assert.Equal(new[] { "Autosave-3.rws", "序盤.rws" }, Files(c));
    }

    [Fact]
    public void ExampleD_DeadStartingPawns_SubsetMerges()
        => Assert.Single(CampaignMatcher.BuildColonies(Profile(Obs("a.rws", pawns: "359,794,406"), Obs("b.rws", pawns: "406", day: 9))));

    [Fact]
    public void CampaignId_TakesPriority_EvenWithoutFingerprint()
    {
        var p = Profile(new SaveObservation
        {
            FileName = "modded.rws", Status = ObservationStatus.Ok,
            Identity = new ColonyIdentity { ModCampaignId = "g1", StartingPawnIds = { "Thing_Human1" } },
        });
        p.LiveCampaigns.Add(new LiveCampaign { CampaignId = "g1", Identity = Id(pawns: "1,2") });
        var c = Assert.Single(CampaignMatcher.BuildColonies(p));
        Assert.Single(c.Observations);
        Assert.Single(c.LiveCampaigns);
        Assert.Equal($"fp:{Hash}", c.Key);   // the live record supplies the fingerprint

        var idOnly = Profile(new SaveObservation { FileName = "x.rws", Status = ObservationStatus.Ok, Identity = new ColonyIdentity { ModCampaignId = "g2" } });
        Assert.Equal("id:g2", Assert.Single(CampaignMatcher.BuildColonies(idOnly)).Key);
    }

    [Fact]
    public void ErrorAndUnidentifiable_AreNotGrouped()
    {
        var error = Obs("broken.rws"); error.Status = ObservationStatus.Error;   // fingerprint fields present, still ignored
        var unid = new SaveObservation { FileName = "nokey.rws", Status = ObservationStatus.Unidentifiable, Identity = new ColonyIdentity { StartingPawnIds = { "Thing_Human1" } } };
        var c = Assert.Single(CampaignMatcher.BuildColonies(Profile(error, unid, Obs("good.rws"))));
        Assert.Equal(new[] { "good.rws" }, Files(c));
        Assert.Empty(CampaignMatcher.BuildColonies(Profile(error, unid)));
    }

    [Fact]
    public void SameFingerprint_NonSubsetPawns_GetDistinctPersistedKeys()
    {
        var p = Profile(Obs("a.rws", pawns: "123,124"), Obs("b.rws", pawns: "123,125"));
        var keys = CampaignMatcher.BuildColonies(p).Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, keys.Distinct().Count());
        Assert.Equal($"fp:{Hash}", keys[0]);
        Assert.Equal($"fp:{Hash}#2", keys[1]);
        Assert.Equal(2, p.ColonyKeys.Count);
        p.SaveObservations.Add(Obs("old.rws", pawns: "123,124,126", day: -100));   // an older save found later
        Assert.Equal(keys, CampaignMatcher.BuildColonies(p).Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void NewCollidingColony_DoesNotTakeOverExistingKey()
    {
        // The first colony of a hash keeps fp:<hash> (spec §6.2) even when a colliding colony found later sorts first.
        var p = Profile(Obs("a.rws", pawns: "1,2"));
        Assert.Equal($"fp:{Hash}", Assert.Single(CampaignMatcher.BuildColonies(p)).Key);
        p.UserDecisions.ExcludedCampaignKeys.Add($"fp:{Hash}");
        p.SaveObservations.Add(Obs("z.rws", pawns: "7,8,9", day: -50));   // more pawns and older: sorts before a.rws
        var colonies = CampaignMatcher.BuildColonies(p);
        var a = Assert.Single(colonies, c => c.Observations.Any(o => o.FileName == "a.rws"));
        var z = Assert.Single(colonies, c => c.Observations.Any(o => o.FileName == "z.rws"));
        Assert.Equal($"fp:{Hash}", a.Key);
        Assert.True(a.Excluded);
        Assert.Equal($"fp:{Hash}#2", z.Key);
        Assert.False(z.Excluded);
    }

    [Fact]
    public void AncestorFoundLater_MergesBranches_AndKeepsDecisionViaAlias()
    {
        var p = Profile(Obs("b1.rws", pawns: "1,2"), Obs("b2.rws", pawns: "1,3"));   // two branches of one fingerprint
        var first = CampaignMatcher.BuildColonies(p);
        Assert.Equal(2, first.Count);
        p.UserDecisions.ExcludedCampaignKeys.Add($"fp:{Hash}#2");
        p.SaveObservations.Add(Obs("ancestor.rws", pawns: "1,2,3"));
        var c = Assert.Single(CampaignMatcher.BuildColonies(p));
        Assert.Equal($"fp:{Hash}", c.Key);
        Assert.Contains($"fp:{Hash}#2", c.AliasKeys);
        Assert.True(c.Excluded);   // the exclusion attached to the alias still applies
    }

    [Fact]
    public void ResultDoesNotDependOnObservationOrder()
    {
        SaveObservation[] All() => new[]
        {
            Obs("b1.rws", pawns: "1,2", day: 2), Obs("b2.rws", pawns: "1,3", day: 3), Obs("anc.rws", pawns: "1,2,3", day: 1),
            Obs("x1.rws", pawns: "4,5", day: 5), Obs("x2.rws", pawns: "4,6", day: 5),            // colliding, never joined
            Obs("other.rws", seed: "t", pawns: "9", day: 4), Obs("other2.rws", seed: "t", pawns: "9,10", day: 6),
            Obs("solo.rws", wealth0: 1f, pawns: "11", day: 0), Obs("id.rws", seed: "u", pawns: "12", id: "g7", day: 8),
        };
        string Describe(IReadOnlyList<Colony> cs) => string.Join(" | ", cs.Select(c => $"{c.Key}:{string.Join(",", Files(c))}").OrderBy(s => s, StringComparer.Ordinal));

        var expected = Describe(CampaignMatcher.BuildColonies(Profile(All())));
        for (var seed = 1; seed <= 5; seed++)
        {
            var shuffled = All();
            new Random(seed).Shuffle(shuffled);
            Assert.Equal(expected, Describe(CampaignMatcher.BuildColonies(Profile(shuffled))));
        }
        // {1,2,3}+branches, {4,5}, {4,6} share one fingerprint; plus seed t, the 1f-wealth start and the id-stamped save.
        Assert.Equal(6, CampaignMatcher.BuildColonies(Profile(All())).Count);
    }

    [Fact]
    public void ManualMerge_CombinesListedColonies()
    {
        var p = Profile(Obs("a.rws", seed: "A"), Obs("b.rws", seed: "B"), Obs("c.rws", seed: "C"));
        var byFile = CampaignMatcher.BuildColonies(p).ToDictionary(c => c.Observations[0].FileName, c => c.Key);
        p.UserDecisions.ManualMerges.Add(new List<string> { byFile["b.rws"], byFile["a.rws"] });
        var colonies = CampaignMatcher.BuildColonies(p);
        Assert.Equal(2, colonies.Count);
        var merged = Assert.Single(colonies, c => c.Observations.Count == 2);
        Assert.Equal(byFile["b.rws"], merged.Key);
        Assert.Contains(byFile["a.rws"], merged.AliasKeys);
        Assert.Equal(new[] { "a.rws", "b.rws" }, Files(merged));
    }

    [Fact]
    public void ManualSplits_AreStoredButNotApplied()
    {
        var p = Profile(Obs("a.rws", pawns: "1,2"), Obs("b.rws", pawns: "1"));
        p.UserDecisions.ManualSplits.Add(new List<string> { $"fp:{Hash}" });
        Assert.Single(CampaignMatcher.BuildColonies(p));
    }

    [Fact]
    public void ExcludedKey_MarksColony()
    {
        var p = Profile(Obs("a.rws", seed: "A"), Obs("b.rws", seed: "B"));
        var keyA = CampaignMatcher.BuildColonies(p).Single(c => c.Observations[0].FileName == "a.rws").Key;
        p.UserDecisions.ExcludedCampaignKeys.Add(keyA);
        var colonies = CampaignMatcher.BuildColonies(p);
        Assert.True(colonies.Single(c => c.Key == keyA).Excluded);
        Assert.False(colonies.Single(c => c.Key != keyA).Excluded);
    }

    [Fact]
    public void Endings_AttachToColonyOfTheirCampaign()
    {
        var p = Profile(Obs("a.rws", pawns: "1"), Obs("mod.rws", seed: "m", pawns: "2", id: "g2"));
        p.LiveCampaigns.Add(new LiveCampaign { CampaignId = "g1", Identity = Id(pawns: "1") });
        p.EndingEvents.Add(new EndingEvent { CampaignId = "g1", Type = EndingType.Ship });
        p.EndingEvents.Add(new EndingEvent { CampaignId = "g2", Type = EndingType.Archonexus });
        p.EndingEvents.Add(new EndingEvent { CampaignId = "g9", Type = EndingType.Other });   // no other record of g9
        p.SaveObservations[0].EndingTraces.Add(EndingType.AmbiguousLaunch);
        var colonies = CampaignMatcher.BuildColonies(p);
        Assert.Equal(3, colonies.Count);
        var c1 = colonies.Single(c => c.Key == $"fp:{Hash}");
        Assert.Equal(EndingType.Ship, Assert.Single(c1.Endings).Type);
        Assert.Equal(new[] { EndingType.AmbiguousLaunch }, c1.Traces);
        Assert.Equal(EndingType.Archonexus, Assert.Single(colonies.Single(c => c.Observations.Any(o => o.FileName == "mod.rws")).Endings).Type);
        var orphan = colonies.Single(c => c.Key == "id:g9");
        Assert.Empty(orphan.Observations);
        Assert.Equal(EndingType.Other, Assert.Single(orphan.Endings).Type);
    }

    [Fact]
    public void FindCampaignIdFor_ReturnsMatchingLiveCampaign()
    {
        var p = new Profile();
        p.LiveCampaigns.Add(new LiveCampaign { CampaignId = "g1", Identity = Id(pawns: "1,2,3") });
        Assert.Equal("g1", CampaignMatcher.FindCampaignIdFor(p, Id(pawns: "2")));
        Assert.Null(CampaignMatcher.FindCampaignIdFor(p, Id(pawns: "4")));            // same fingerprint, not a subset
        Assert.Null(CampaignMatcher.FindCampaignIdFor(p, Id(wealth0: 1f, pawns: "2"))); // different fingerprint
        Assert.Null(CampaignMatcher.FindCampaignIdFor(p, new ColonyIdentity { StartingPawnIds = { "Thing_Human2" } }));
        Assert.True(CampaignMatcher.SameColony(Id(pawns: "1,2"), Id(pawns: "2")));
        Assert.False(CampaignMatcher.SameColony(Id(pawns: "1,2"), Id(pawns: "1,3")));
    }
}
