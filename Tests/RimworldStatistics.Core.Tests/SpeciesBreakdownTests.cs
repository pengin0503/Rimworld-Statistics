namespace RimworldStatistics.Core.Tests;

public sealed class SpeciesBreakdownTests
{
    [Fact]
    public void LegacyJoinsRemainInTotalsAndOnlyNewJoinsGetSpecies()
    {
        var state = new FamilyState { SinceTick = 0, ThroughTick = 60000 };
        MembershipFamily.Joined(state, "old", MembershipFamily.RouteOther, null, 100);
        MembershipFamily.Joined(state, "new", MembershipFamily.RouteOther, null, 200, "author.race/Ratkin", "author.genes/Highmate");
        MembershipFamily.Joined(state, "new", MembershipFamily.RouteOther, null, 300, "author.race/Ratkin", "author.genes/Changed");
        var into = new Dictionary<string, MetricEntry>();
        MembershipFamily.Family.Build(new FamilyOutputContext(state, 60000, into));
        Assert.Equal(2L, into[MembershipFamily.FirstJoin].Values[MetricEntry.Scalar].IntegerValue);
        var species = into[MembershipFamily.JoinsBySpecies].Values;
        Assert.Equal(1L, species["author.race/Ratkin"].IntegerValue);
        Assert.Equal(1L, species[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, species[MetricEntry.Scalar].Coverage);
        var xenotypes = into[MembershipFamily.JoinsByXenotype].Values;
        Assert.Equal(MetricCoverage.Partial, xenotypes[MetricEntry.Scalar].Coverage);
        Assert.Equal(1, xenotypes["author.genes/Highmate"].IntegerValue);
        Assert.DoesNotContain("author.genes/Changed", xenotypes.Keys);
    }
    [Fact]
    public void DeathBreakdownUsesFreeColonistsAndRetainsOlderUnknownCounts()
    {
        var state = new FamilyState { SinceTick = 0, ThroughTick = 60000 };
        HealthDeathFamily.Died(state, new DeathFacts { PawnId = "legacy", VictimKind = DeathFacts.Humanlike, FreeColonist = true }, 10);
        HealthDeathFamily.Died(state, new DeathFacts { PawnId = "colonist", VictimKind = DeathFacts.Humanlike, FreeColonist = true, Species = "mod.race/Alien", Xenotype = "mod.genes/Hybrid" }, 20);
        HealthDeathFamily.Died(state, new DeathFacts { PawnId = "visitor", VictimKind = DeathFacts.Humanlike, Species = "mod.race/Alien", Xenotype = "mod.genes/Foreign" }, 30);
        var into = new Dictionary<string, MetricEntry>();
        HealthDeathFamily.Family.Build(new FamilyOutputContext(state, 60000, into));
        Assert.Equal(2L, into[HealthDeathFamily.DeathCauses].Values[MetricEntry.Scalar].IntegerValue);
        var species = into[HealthDeathFamily.DeathsBySpecies].Values;
        Assert.Equal(1L, species["mod.race/Alien"].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, species[MetricEntry.Scalar].Coverage);
        var xenotypes = into[HealthDeathFamily.DeathsByXenotype].Values;
        Assert.Equal(1, xenotypes["mod.genes/Hybrid"].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, xenotypes[MetricEntry.Scalar].Coverage);
        Assert.DoesNotContain("mod.genes/Foreign", xenotypes.Keys);
    }
}
