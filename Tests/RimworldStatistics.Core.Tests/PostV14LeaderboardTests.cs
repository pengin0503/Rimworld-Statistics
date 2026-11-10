namespace RimworldStatistics.Core.Tests;

public sealed class PostV14LeaderboardTests
{
    private static NumericRow Source(string key, double? total, MetricCoverage coverage,
        params (string Item, double Amount)[] parts)
    {
        var definition = MetricRegistry.Find(key)!;
        return new NumericRow
        {
            Definition = definition, Targets = coverage == MetricCoverage.NotApplicable ? 0 : 1,
            Valid = total is null || coverage == MetricCoverage.NotApplicable ? 0 : 1,
            Value = new NumericValue { Number = total, Coverage = coverage, Evidence = MetricEvidence.Experimental },
            Breakdowns = parts.Select(p => new NumericRow
            {
                Definition = definition,
                Breakdown = p.Item,
                Value = new NumericValue { Number = p.Amount, Coverage = coverage, Evidence = MetricEvidence.Experimental },
                Targets = 1, Valid = 1,
            }).ToArray(),
        };
    }

    private static NumericRow Leaderboard(string sourceKey, params (string Colony, NumericRow Row)[] colonies)
    {
        var inputs = colonies.ToDictionary(c => c.Colony,
            c => (IReadOnlyList<NumericRow>)new[] { c.Row }, StringComparer.Ordinal);
        return CrossColonyAggregator.Build(MetricRegistry.Catalog, inputs)
            .Single(r => r.Definition.Key == CrossColonyCatalog.TopPrefix + sourceKey);
    }

    [Fact]
    public void FullRank_IsSumPerDef_AndEachContributionIsFromOneIndependentColony()
    {
        var result = Leaderboard(WorkFamily.HarvestByCrop,
            ("a", Source(WorkFamily.HarvestByCrop, 13, MetricCoverage.Complete, ("Rice", 10), ("Potato", 3))),
            ("b", Source(WorkFamily.HarvestByCrop, 22, MetricCoverage.Complete, ("Rice", 2), ("Potato", 20))));
        Assert.Equal(23d, result.Value.Number);
        Assert.Equal("Potato", result.Value.Label); // existing top-one contract
        Assert.Equal(new[] { "Potato", "Rice" }, result.Breakdowns.Select(r => r.Breakdown));
        Assert.Equal(new[] { 23d, 12d }, result.Breakdowns.Select(r => r.Value.Number!.Value).ToArray());
        Assert.Equal((2, 2), (result.Targets, result.Valid));
        var potato = result.Breakdowns[0];
        Assert.Equal(new[] { "b", "a" }, potato.Breakdowns.Select(c => c.Breakdown));
        Assert.Equal(new[] { 20d, 3d }, potato.Breakdowns.Select(c => c.Value.Number!.Value).ToArray());
        Assert.Equal(MetricCoverage.Complete, potato.Value.Coverage);
        Assert.Equal(MetricEvidence.Experimental, potato.Value.Evidence);
        Assert.All(potato.Breakdowns, p => Assert.Equal((1, 1), (p.Valid, p.Targets)));
    }

    [Fact]
    public void UnknownColony_AndOverflowBucket_NeverBecomeZeroOrRankedItems()
    {
        var result = Leaderboard(TradeFamily.SalesUnits,
            ("a", Source(TradeFamily.SalesUnits, 30, MetricCoverage.Complete, ("Rice", 10))),
            ("b", Source(TradeFamily.SalesUnits, null, MetricCoverage.Unknown)),
            ("c", Source(TradeFamily.SalesUnits, 4, MetricCoverage.Complete,
                (MetricKeys.UnknownBucket, 3), ("Rice", 1))));
        Assert.Equal((3, 2), (result.Targets, result.Valid));
        Assert.Equal(MetricCoverage.Partial, result.Value.Coverage);
        Assert.Equal(11d, result.Value.Number);
        var only = Assert.Single(result.Breakdowns);
        Assert.Equal("Rice", only.Breakdown);
        Assert.Equal(11d, only.Value.Number);
        Assert.Equal(new[] { "a", "c" }, only.Breakdowns.Select(b => b.Breakdown));
        Assert.DoesNotContain(result.Breakdowns, b => b.Breakdown == MetricKeys.UnknownBucket);
        Assert.Equal(MetricAvailability.SomeMeasuredColonies, MetricAvailabilityPolicy.Classify(result));
    }

    [Fact]
    public void DuplicatedBreakdownKey_RejectsInflatedContribution_AndRemainsPartial()
    {
        var result = Leaderboard(WorkFamily.Sown_,
            ("a", Source(WorkFamily.Sown_, 7, MetricCoverage.Complete, ("Rice", 5), ("Rice", 2))));
        Assert.Equal(5d, result.Value.Number);
        Assert.Equal(MetricCoverage.Partial, result.Value.Coverage);
        Assert.Equal(5d, Assert.Single(result.Breakdowns).Value.Number);
        Assert.Single(Assert.Single(result.Breakdowns).Breakdowns);
    }

    [Fact]
    public void DeterministicTie_PreservesExistingTopLabel_AndFullRank()
    {
        var result = Leaderboard(TradeFamily.BuysUnits,
            ("x", Source(TradeFamily.BuysUnits, 5, MetricCoverage.Complete, ("Z", 5))),
            ("y", Source(TradeFamily.BuysUnits, 5, MetricCoverage.Complete, ("A", 5))));
        Assert.Equal("A / Z", result.Value.Label);
        Assert.Equal(new[] { "A", "Z" }, result.Breakdowns.Select(b => b.Breakdown));
    }

    [Fact]
    public void MissingAndZero_AreDifferentForRankAndCoverage()
    {
        var unknown = Leaderboard(WorkFamily.HarvestByCrop,
            ("missing", Source(WorkFamily.HarvestByCrop, null, MetricCoverage.Unknown)));
        Assert.Null(unknown.Value.Number);
        Assert.Empty(unknown.Breakdowns);
        Assert.Equal(MetricAvailability.NoMeasuredColonies, MetricAvailabilityPolicy.Classify(unknown));

        var measuredZero = Leaderboard(WorkFamily.HarvestByCrop,
            ("measured", Source(WorkFamily.HarvestByCrop, 0, MetricCoverage.Complete)));
        Assert.Null(measuredZero.Value.Number);
        Assert.Empty(measuredZero.Breakdowns); // zero observed events is not a winning crop
        Assert.Equal((1, 1), (measuredZero.Targets, measuredZero.Valid));
        Assert.Equal(MetricAvailability.AllMeasuredColonies, MetricAvailabilityPolicy.Classify(measuredZero));

        var notApplicable = Leaderboard(RosterSnapshot.Passions,
            ("dlc-missing", Source(RosterSnapshot.Passions, null, MetricCoverage.NotApplicable)));
        Assert.Equal(MetricCoverage.NotApplicable, notApplicable.Value.Coverage);
        Assert.Equal(MetricAvailability.NoApplicableColonies, MetricAvailabilityPolicy.Classify(notApplicable));
    }

    [Fact]
    public void CoverageOfTwentyFourOutOfThirtyFive_IsAvailabilityNotConfirmedCause()
    {
        var definition = MetricRegistry.Find(RosterSnapshot.Free)!;
        NumericRow Row(int targets, int valid, MetricCoverage coverage, int? since = null)
            => new()
            {
                Definition = definition, Targets = targets, Valid = valid,
                Value = new NumericValue { Coverage = coverage, SinceTick = since },
            };
        Assert.Equal(MetricAvailability.NoMeasuredColonies,
            MetricAvailabilityPolicy.Classify(Row(35, 0, MetricCoverage.Unknown)));
        Assert.Equal(MetricAvailability.SomeMeasuredColonies,
            MetricAvailabilityPolicy.Classify(Row(35, 1, MetricCoverage.Partial)));
        Assert.Equal(MetricAvailability.AllMeasuredColonies,
            MetricAvailabilityPolicy.Classify(Row(35, 35, MetricCoverage.Partial)));
        Assert.Equal(MetricAvailability.NoApplicableColonies,
            MetricAvailabilityPolicy.Classify(Row(0, 0, MetricCoverage.NotApplicable)));
        Assert.True(MetricAvailabilityPolicy.IsPeriodIncomplete(Row(35, 35, MetricCoverage.Complete, 900)));
        Assert.False(MetricAvailabilityPolicy.IsPeriodIncomplete(Row(35, 35, MetricCoverage.Complete)));
    }


    [Fact]
    public void NumericallyFilledButUnknownEvidence_DoesNotBecomeAWinningDef()
    {
        var sourceKey = WorkFamily.HarvestByCrop;
        var result = Leaderboard(sourceKey,
            ("a", Source(sourceKey, 10, MetricCoverage.Unknown, ("UnknownFromOldProfile", 10))),
            ("b", Source(sourceKey, 2, MetricCoverage.Complete, ("Confirmed", 2))));
        Assert.Equal("Confirmed", result.Value.Label);
        Assert.Equal(2d, result.Value.Number);
        Assert.Equal(MetricCoverage.Partial, result.Value.Coverage);
        Assert.Equal(1, result.Valid);
        Assert.Single(result.Breakdowns);
    }

    [Fact]
    public void UnknownChildNumber_IsNotReliableItemQuantity()
    {
        var sourceKey = TradeFamily.SalesUnits;
        var definition = MetricRegistry.Find(sourceKey)!;
        var parent = Source(sourceKey, 10, MetricCoverage.Complete, ("Rice", 10));
        var invalid = new NumericRow
        {
            Definition = definition, Breakdown = "Rice",
            Value = new NumericValue { Number = 10, Coverage = MetricCoverage.Unknown },
        };
        var row = new NumericRow
        {
            Definition = definition, Value = parent.Value, Targets = 1, Valid = 1,
            Breakdowns = new[] { invalid },
        };
        var ranked = Leaderboard(sourceKey, ("a", row));
        Assert.Empty(ranked.Breakdowns);
        Assert.Null(ranked.Value.Number);
        Assert.Equal(MetricCoverage.Partial, ranked.Value.Coverage);
    }
    [Fact]
    public void NewPresentation_DoesNotAlter415CatalogKeysOrStorageContract()
    {
        Assert.Equal(331, MetricRegistry.BaseCatalog.Count);
        Assert.Equal(415, MetricRegistry.Catalog.Count);
        Assert.Equal(MetricRegistry.Catalog.Count,
            MetricRegistry.Catalog.Select(d => d.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.False(MetricRegistry.Find(CrossColonyCatalog.TopPrefix + WorkFamily.HarvestByCrop)!.HasBreakdown);
        Assert.Equal(10, SaveReader.Version);
    }
}
