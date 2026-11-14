namespace RimworldStatistics.Core.Tests;

public sealed class SpeciesBreakdownTests
{
    [Fact]
    public void LegacyJoinsRemainInTotalsAndOnlyNewJoinsGetSpecies()
    {
        var state = new FamilyState { SinceTick = 0, ThroughTick = 60000 };
        MembershipFamily.Joined(state, "old", MembershipFamily.RouteOther, null, 100);
        MembershipFamily.Joined(state, "new", MembershipFamily.RouteOther, null, 200, "author.race/Ratkin");
        MembershipFamily.Joined(state, "new", MembershipFamily.RouteOther, null, 300, "author.race/Ratkin");
        var into = new Dictionary<string, MetricEntry>();
        MembershipFamily.Family.Build(new FamilyOutputContext(state, 60000, into));
        Assert.Equal(2L, into[MembershipFamily.FirstJoin].Values[MetricEntry.Scalar].IntegerValue);
        var species = into[MembershipFamily.JoinsBySpecies].Values;
        Assert.Equal(1L, species["author.race/Ratkin"].IntegerValue);
        Assert.Equal(1L, species[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, species[MetricEntry.Scalar].Coverage);
    }
    [Fact]
    public void DeathBreakdownUsesFreeColonistsAndRetainsOlderUnknownCounts()
    {
        var state = new FamilyState { SinceTick = 0, ThroughTick = 60000 };
        HealthDeathFamily.Died(state, new DeathFacts { PawnId = "legacy", VictimKind = DeathFacts.Humanlike, FreeColonist = true }, 10);
        HealthDeathFamily.Died(state, new DeathFacts { PawnId = "colonist", VictimKind = DeathFacts.Humanlike, FreeColonist = true, Species = "mod.race/Alien" }, 20);
        HealthDeathFamily.Died(state, new DeathFacts { PawnId = "visitor", VictimKind = DeathFacts.Humanlike, Species = "mod.race/Alien" }, 30);
        var into = new Dictionary<string, MetricEntry>();
        HealthDeathFamily.Family.Build(new FamilyOutputContext(state, 60000, into));
        Assert.Equal(2L, into[HealthDeathFamily.DeathCauses].Values[MetricEntry.Scalar].IntegerValue);
        var species = into[HealthDeathFamily.DeathsBySpecies].Values;
        Assert.Equal(1L, species["mod.race/Alien"].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, species[MetricEntry.Scalar].Coverage);
    }
}
