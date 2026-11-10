namespace RimworldStatistics.Core.Tests;

public sealed class NumericSnapshotRosterTests
{
    [Fact]
    public void CountsLivingPlayerPawnsOnlyOnceAcrossCaravanAndMapIndexes()
    {
        var human = new RosterPawnFact
        {
            Id = "Thing_A", Kind = RosterKind.FreeColonist, AgeYears = 28,
            Augmented = true, Injured = true,
            Skills = new[]
            {
                new RosterSkillFact("Shooting", 20, "Major"), new RosterSkillFact("Crafting", 20, "Minor"),
            },
        };
        var result = RosterSnapshot.Build(new[]
        {
            human, human,
            new RosterPawnFact { Id = "Thing_B", Kind = RosterKind.FreeColonist, AgeYears = 42,
                Skills = new[]{ new RosterSkillFact("Shooting", 10, "None") } },
            new RosterPawnFact { Id = "Thing_P", Kind = RosterKind.Prisoner },
            new RosterPawnFact { Id = "Thing_Cow", Kind = RosterKind.ColonyAnimal, RaceDef = "Cow" },
            new RosterPawnFact { Id = "Thing_Alpaca", Kind = RosterKind.ColonyAnimal, RaceDef = "Alpaca" },
            new RosterPawnFact { Id = "Thing_Mech", Kind = RosterKind.ColonyMech },
            new RosterPawnFact { Id = "Thing_Enemy", Kind = RosterKind.Other },
        });
        long N(string key) => result[key].Values[""].IntegerValue!.Value;
        Assert.Equal(2, N(RosterSnapshot.Free));
        Assert.Equal(1, N(RosterSnapshot.Prisoners));
        Assert.Equal(2, N(RosterSnapshot.Animals));
        Assert.Equal(1, N(RosterSnapshot.Mechs));
        Assert.Equal(1, N(RosterSnapshot.AugmentedPeak));
        Assert.Equal(1, N(RosterSnapshot.Skill20PawnsPeak));
        Assert.Equal(2, N(RosterSnapshot.Skill20PerPawnPeak));
        Assert.Equal(2, N(RosterSnapshot.AnimalDefsPeak));
        Assert.Equal(1, N(RosterSnapshot.InjuredPeak));
        Assert.Equal(42d, result[RosterSnapshot.Oldest].Values[""].RealValue);
        var mean = result[RosterSnapshot.AgeMean].Values[""];
        Assert.Equal(35d, mean.RealValue);
        Assert.Equal(70d, mean.Numerator);
        Assert.Equal(2d, mean.Denominator);
        Assert.Equal(1, result[RosterSnapshot.Passions].Values["Shooting:Major"].IntegerValue);
        Assert.Equal(1, result[RosterSnapshot.Passions].Values["Shooting:None"].IntegerValue);
    }

    [Fact]
    public void EmptyRosterProducesKnownZeroCountsButNoAge()
    {
        var result = RosterSnapshot.Build(Array.Empty<RosterPawnFact>());
        Assert.Equal(0, result[RosterSnapshot.Free].Values[""].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable, result[RosterSnapshot.AgeMean].Values[""].Coverage);
        Assert.Equal(MetricCoverage.NotApplicable, result[RosterSnapshot.Oldest].Values[""].Coverage);
    }

    [Fact]
    public void SlaveStatusHasIndependentLiveSnapshotCountAndCannotBeDoubleCounted()
    {
        Assert.True(Enum.TryParse<RosterKind>("Slave", out var slave));
        var r = RosterSnapshot.Build(new[] {
            new RosterPawnFact { Id = "S1", Kind = slave },
            new RosterPawnFact { Id = "S1", Kind = slave },
            new RosterPawnFact { Id = "F1", Kind = RosterKind.FreeColonist, AgeYears = 20 },
            new RosterPawnFact { Id = "P1", Kind = RosterKind.Prisoner },
        });
        Assert.Equal(1, r["population.snapshot.slaves"].Values[""].IntegerValue);
        Assert.Equal(1, r[RosterSnapshot.Free].Values[""].IntegerValue);
        Assert.Equal(1, r[RosterSnapshot.Prisoners].Values[""].IntegerValue);
        Assert.NotNull(MetricRegistry.Find("population.snapshot.slaves"));
        Assert.Null(MetricRegistry.Find("facilities.running_generators.latest"));
    }

    [Fact]
    public void CatalogRetainsPeakSelectionWithoutPublishing()
    {
        Assert.Equal(ColonySelection.ObservationMax, MetricRegistry.Find(RosterSnapshot.InjuredPeak)!.Selection);
        Assert.Equal(ColonySelection.ObservationMax, MetricRegistry.Find(RosterSnapshot.AugmentedPeak)!.Selection);
        Assert.False(MetricRegistry.Find(RosterSnapshot.Free)!.Published);
    }
}
