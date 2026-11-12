using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ViewScopeTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    private static SaveObservation Save(string file, string seed, int ticks) => new()
    {
        FileName = file, FileSize = 100, LastWriteUtc = Now,
        ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok,
        Identity = new ColonyIdentity
        {
            SeedString = seed, StartingTile = "1,0", PersistentRandomValue = 1,
            FirstWealthBits = 1, StartingPawnIds = ["Thing_Human1"],
        },
        Values = { TicksGame = ticks },
    };

    [Fact]
    public void AllScopePreservesExistingView()
    {
        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => Now);
        session.Mutate(p =>
        {
            p.SaveObservations.Add(Save("a.rws", "A", 120000));
            p.SaveObservations.Add(Save("b.rws", "B", 240000));
        }, SaveMode.None);
        var oldView = session.GetView(new StatsOptions());
        var scoped = session.GetView(new StatsOptions(), scope: ViewScope.All);
        Assert.Same(oldView, scoped);
        Assert.Equal(2, scoped.Stats.ColonyCount);
        Assert.Equal(360000L, scoped.Stats.TotalTicks);
    }

    [Fact]
    public void SelectedScopeCountsOnlyMembersButPreservesAttribution()
    {
        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => Now);
        session.Mutate(p =>
        {
            p.SaveObservations.Add(Save("a.rws", "A", 120000));
            p.SaveObservations.Add(Save("b.rws", "B", 240000));
        }, SaveMode.None);
        var all = session.GetView(new StatsOptions());
        var first = all.Colonies.Single(c => c.Observations.Any(o => o.Identity.SeedString == "A")).Key;
        var group = new ColonyGroup { Id = "group1", Name = "A only", ColonyKeys = [first] };
        var filtered = session.GetView(new StatsOptions(), scope: ViewScope.ForGroup(group));
        Assert.Equal(2, filtered.Colonies.Count);
        Assert.Equal(1, filtered.Stats.ColonyCount);
        Assert.Equal(120000L, filtered.Stats.TotalTicks);
        Assert.Equal(1, filtered.Stats.OutOfScopeColonyCount);
        Assert.Equal(all.Numeric.Unattributed, filtered.Numeric.Unattributed);
        Assert.Equal(all.Numeric.Colonies.Select(c => c.Observations), filtered.Numeric.Colonies.Select(c => c.Observations));
        Assert.True(filtered.Stats.Colonies.Single(c => c.Key != first).OutOfScope);
        Assert.Same(filtered, session.GetView(new StatsOptions(), scope: ViewScope.ForGroup(group)));
    }

    [Fact]
    public void AliasKeysAndUnknownKeysAreHandled()
    {
        var colony = new Colony { Key = "new" };
        colony.AliasKeys.Add("old");
        Assert.True(ViewScope.ForGroup(new ColonyGroup { Id = "g", Name = "G", ColonyKeys = ["old"] }).Contains(colony));
        Assert.False(ViewScope.ForGroup(new ColonyGroup { Id = "g2", Name = "G", ColonyKeys = ["absent"] }).Contains(colony));
    }

    [Fact]
    public void ExclusionHasPriorityInsideGroup()
    {
        var a = new Colony { Key = "a", Excluded = true };
        a.Observations.Add(Save("a.rws", "A", 120000));
        var b = new Colony { Key = "b" };
        b.Observations.Add(Save("b.rws", "B", 240000));
        var scope = ViewScope.ForGroup(new ColonyGroup { Id = "both", Name = "Both", ColonyKeys = ["a", "b"] });
        scope.Apply([a, b]);
        var result = StatsCalculator.Calculate([a, b], new StatsOptions());
        Assert.Equal(1, result.ColonyCount);
        Assert.Equal(240000L, result.TotalTicks);
    }
}

public sealed class ViewPreferencesTests
{
    [Fact]
    public void EmptyOrInvalidJsonUsesDefaults()
    {
        Assert.Empty(ViewPreferences.Parse(null).Groups);
        Assert.Empty(ViewPreferences.Parse("not-json").Groups);
    }

    [Fact]
    public void RoundTripsNamesAndMembers()
    {
        var p = new ViewPreferences { Groups = [new ColonyGroup
        {
            Id = "g1", Name = "Main campaign", ColonyKeys = ["a", "b"]
        }] };
        var restored = ViewPreferences.Parse(p.ToJson());
        Assert.Single(restored.Groups);
        Assert.Equal("Main campaign", restored.Groups[0].Name);
        Assert.Equal(new[] { "a", "b" }, restored.Groups[0].ColonyKeys);
    }

    [Fact]
    public void SanitizesDuplicateAndEmptyKeys()
    {
        var restored = ViewPreferences.Parse("""
            {"version":1,"groups":[{"id":"g","name":"Test","colonyKeys":["a","a","","b"]},
                {"id":"g","name":"Duplicate","colonyKeys":["c"]}]}
            """);
        Assert.Single(restored.Groups);
        Assert.Equal(new[] { "a", "b" }, restored.Groups[0].ColonyKeys);
    }
}