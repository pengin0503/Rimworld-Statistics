namespace RimworldStatistics.Core.Tests;

/// <summary>Lifetime presentation and persisted family breakdowns: independent of game hooks.</summary>
public sealed class LifetimeCumulativeTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private static MetricObservation Observe(LiveMetricAccumulator a, int tick = 200)
    {
        var d = a.PrepareSave(tick, Now, "test")!;
        a.Commit(d);
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "test" }, null, null);
    }

    private static (LiveMetricAccumulator Acc, FamilyState State) NewGame(LiveFamily family)
    {
        var a = LiveMetricAccumulator.StartNew(new[] { family.Spec }, 0);
        return (a, a.Family(family.Name)!);
    }

    private static MetricValue Scalar(MetricObservation o, string key)
        => o.Scalar(key, MetricKeys.Revision(key))!;

    private static IReadOnlyDictionary<string, MetricValue> Children(MetricObservation o, string key)
        => o.Metrics[key].Values.Where(x => x.Key != MetricEntry.Scalar)
            .ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);

    [Fact]
    public void Trade_ExistingTotalRemainsMoneyAndUnitsBreakDownByItem()
    {
        var (a, state) = NewGame(TradeFamily.Family);
        TradeFamily.Traded(state, TradeFamily.Orbital, null, new[]
        {
            new TradeLine { Def = "Steel", Count = 10, Total = 25, PlayerSells = true },
            new TradeLine { Def = "Component", Count = 2, Total = 60, PlayerSells = true },
            new TradeLine { Def = "Medicine", Count = 3, Total = 45, PlayerSells = false },
        });
        var o = Observe(a);
        Assert.Equal(1, Scalar(o, TradeFamily.Deals).IntegerValue);
        Assert.Equal(1, Children(o, TradeFamily.Deals)[TradeFamily.Orbital].IntegerValue);
        Assert.Equal(12, Scalar(o, TradeFamily.SalesUnits).IntegerValue);
        Assert.Equal(10, Children(o, TradeFamily.SalesUnits)["Steel"].IntegerValue);
        Assert.Equal(2, Children(o, TradeFamily.SalesUnits)["Component"].IntegerValue);
        Assert.Equal(3, Scalar(o, TradeFamily.BuysUnits).IntegerValue);
        Assert.Equal(3, Children(o, TradeFamily.BuysUnits)["Medicine"].IntegerValue);
        Assert.Equal(85, Scalar(o, TradeFamily.SalesTotal).RealValue);
    }

    [Fact]
    public void SowingAndSurgery_RevealOutcomesWithoutNewCounters()
    {
        var (w, ws) = NewGame(WorkFamily.Family);
        WorkFamily.Sown(ws, "Rice");
        WorkFamily.Sown(ws, "Rice");
        WorkFamily.Sown(ws, "Corn");
        var wo = Observe(w);
        Assert.Equal(3, Scalar(wo, WorkFamily.Sown_).IntegerValue);
        Assert.Equal(2, Children(wo, WorkFamily.Sown_)["Rice"].IntegerValue);
        Assert.Equal(1, Children(wo, WorkFamily.Sown_)["Corn"].IntegerValue);

        var (m, ms) = NewGame(MedicineFamily.Family);
        MedicineFamily.Surgery(ms, "surgeon", true, false);
        MedicineFamily.Surgery(ms, "surgeon", true, false);
        MedicineFamily.Surgery(ms, "surgeon", false, false);
        var mo = Observe(m);
        Assert.Equal(3, Scalar(mo, MedicineFamily.Surgeries).IntegerValue);
        Assert.Equal(2, Children(mo, MedicineFamily.Surgeries)["success"].IntegerValue);
        Assert.Equal(1, Children(mo, MedicineFamily.Surgeries)["failure"].IntegerValue);
        Assert.Equal(2.0 / 3.0, Scalar(mo, MedicineFamily.SurgerySuccessRate).RealValue!.Value, 10);
    }

    [Fact]
    public void Overflow_WeakensChildrenButNotMeasuredParent()
    {
        var (a, s) = NewGame(WorkFamily.Family);
        for (int i = 0; i <= FamilyOps.KeyCap; i++)
            s.CountBy("sown", "crop-" + i);
        s.Count("sown", FamilyOps.KeyCap + 1);
        var o = Observe(a);
        Assert.Equal(FamilyOps.KeyCap + 1, Scalar(o, WorkFamily.Sown_).IntegerValue);
        Assert.Equal(MetricCoverage.Complete, Scalar(o, WorkFamily.Sown_).Coverage);
        Assert.Equal(FamilyOps.KeyCap, Children(o, WorkFamily.Sown_).Count);
        Assert.All(Children(o, WorkFamily.Sown_).Values, v => Assert.Equal(MetricCoverage.Partial, v.Coverage));
    }

    [Fact]
    public void LifetimeView_IsNotAnAggregationOrAUnitHeuristic()
    {
        Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == TradeFamily.Deals)));
        Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == WorkFamily.Sown_)));
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == TradeFamily.SalesPeak)));
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == MedicineFamily.SurgerySuccessRate)));
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == MetricKeys.WealthItemsLatest)));
        Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(new MetricDefinition
        {
            Key = TradeFamily.Deals, StoredKey = TradeFamily.Deals, LedgerIds = "external", Category = MetricCategory.Economy,
            Unit = MetricUnit.Count, Provenance = MetricProvenance.LiveAggregate,
            Overall = OverallAggregation.Sum, LabelKey = "external", DescriptionKey = "external", ProviderId = "example",
        }));
        Assert.Equal(MetricRegistry.Catalog.Count, MetricDisplayPolicy.SelectDefinitions(MetricRegistry.Catalog, MetricDisplayPolicy.AllDetailsVisibleUnits).Count);
    }

    [Fact]
    public void LifetimeDisplay_FoldsStandaloneTradeAliasesButRetainsOldDefinitions()
    {
        var catalog = MetricRegistry.Catalog;
        var parent = catalog.Single(d => d.Key == TradeFamily.Deals);
        var orbital = catalog.Single(d => d.Key == TradeFamily.OrbitalDeals);
        var caravan = catalog.Single(d => d.Key == TradeFamily.VisitingCaravanDeals);
        Assert.True(MetricDisplayPolicy.IsLifetimePrimaryRow(parent));
        Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(orbital));
        Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(caravan));
        Assert.False(MetricDisplayPolicy.IsLifetimePrimaryRow(orbital));
        Assert.False(MetricDisplayPolicy.IsLifetimePrimaryRow(caravan));
        var all = MetricDisplayPolicy.SelectDefinitions(catalog, MetricDisplayPolicy.AllDetailsVisibleUnits);
        Assert.Contains(all, d => d.Key == TradeFamily.OrbitalDeals);
        Assert.Contains(all, d => d.Key == TradeFamily.VisitingCaravanDeals);
        Assert.Contains(all, d => d.Key == TradeFamily.Deals);
        Assert.Equal(catalog.Count, all.Count);
    }

    [Fact]
    public void OldCheckpoint_IsReevaluatedWithoutMutatingFamilyRevisions()
    {
        Assert.Equal(17, SaveReader.Version);
        Assert.Equal(1, TradeFamily.Family.Revision);
        Assert.Equal(1, WorkFamily.Family.Revision);
        Assert.Equal(2, MedicineFamily.Family.Revision);
        var (a, s) = NewGame(WorkFamily.Family);
        WorkFamily.Sown(s, "Rice");
        var draft = a.PrepareSave(100, Now, "legacy")!;
        var parsed = CheckpointJson.Parse(draft.Json).Payload!;
        var o = CheckpointMetrics.ToObservation(parsed, draft.Json, new ColonyIdentity { ModCampaignId = "legacy" }, null, null);
        Assert.Equal(1, Scalar(o, WorkFamily.Sown_).IntegerValue);
        Assert.Equal(1, Children(o, WorkFamily.Sown_)["Rice"].IntegerValue);
    }

    [Fact]
    public void HistoricalEventFamilies_AreVisibleInTheLifetimeFilter()
    {
        var keys = new[]
        {
            DisasterFamily.VolcanicWinters, DisasterFamily.Eclipses, DisasterFamily.CropBlights,
            DisasterFamily.Meteors, DisasterFamily.ShortCircuits, DisasterFamily.Fires,
            DisasterFamily.BuildingsBurned, DisasterFamily.ToxicFalloutDays,
            CaravanFamily.Departures, CaravanFamily.Lost_, CaravanFamily.TilesTravelled,
            CaravanFamily.UniqueTiles, CaravanFamily.Ambushes, CaravanFamily.SitesCleared,
            CaravanFamily.SettlementsFounded, CaravanFamily.SettlementsAbandoned,
            CaravanFamily.AncientDangersOpened, WorkFamily.Sculptures,
            WorkFamily.Legendary, WorkFamily.Masterwork, WorkFamily.MealsByDef,
            WorkFamily.HarvestByCrop, WorkFamily.QualityBreakdown,
            MembershipFamily.Routes, TradeFamily.SoldDefs,
        };
        foreach (var key in keys)
        {
            var definition = MetricRegistry.Catalog.Single(d => d.Key == key);
            Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(definition), key);
        }
        foreach (var key in new[] { CaravanFamily.LongestExpedition, TradeFamily.SalesPeak,
            MedicineFamily.SurgerySuccessRate, MetricKeys.WealthItemsLatest })
            Assert.False(MetricDisplayPolicy.IsLifetimeCumulative(MetricRegistry.Catalog.Single(d => d.Key == key)), key);
    }

    [Fact]
    public void ReimportSameCheckpoint_EnrichesExistingScalarWithNewBreakdown()
    {
        var profile = new Profile();
        MetricObservation Checkpoint(bool detailed, long scalar)
        {
            var observation = new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint,
                Key = "immutable-checkpoint", PayloadHash = "same-saved-bytes",
                SaveTick = 100, Identity = new ColonyIdentity { ModCampaignId = "campaign" },
            };
            var entry = MetricEntry.Of(MetricKeys.Revision(TradeFamily.Deals), new MetricValue
            {
                Coverage = MetricCoverage.Complete, IntegerValue = scalar,
            });
            if (detailed)
            {
                entry.Values["orbital"] = new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 1 };
                entry.Values["visiting_caravan"] = new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 2 };
            }
            observation.Metrics[TradeFamily.Deals] = entry;
            return observation;
        }
        ProfileMutations.UpsertMetricObservation(profile, Checkpoint(false, 3));
        ProfileMutations.UpsertMetricObservation(profile, Checkpoint(true, 3));
        ProfileMutations.UpsertMetricObservation(profile, Checkpoint(true, 3)); // idempotent
        var saved = Assert.Single(profile.MetricObservations).Metrics[TradeFamily.Deals];
        Assert.Equal(3, saved.Values.Count);
        Assert.Equal(3L, saved.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, saved.Values["orbital"].IntegerValue);
        Assert.Equal(2L, saved.Values["visiting_caravan"].IntegerValue);

        ProfileMutations.UpsertMetricObservation(profile, Checkpoint(true, 9)); // contradictory decode
        Assert.Equal(3L, saved.Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, saved.Values["orbital"].IntegerValue);
    }

    [Fact]
    public void DamagedSurgerySuccessCount_DoesNotProduceImpossiblePercentage()
    {
        var (acc, state) = NewGame(MedicineFamily.Family);
        state.Count("surgeries", 1);
        state.Count("surgeries_ok", 2);
        var observed = Observe(acc);
        var surgeries = Scalar(observed, MedicineFamily.Surgeries);
        var rate = Scalar(observed, MedicineFamily.SurgerySuccessRate);
        Assert.Equal(MetricCoverage.Partial, surgeries.Coverage);
        Assert.Equal(MetricCoverage.Unknown, rate.Coverage);
        Assert.Null(rate.RealValue);
        Assert.Null(rate.Numerator);
        Assert.Null(rate.Denominator);
    }

    [Fact]
    public void BreakdownLabelKeys_RequireTheParentMetricContext()
    {
        var trade = MetricRegistry.Catalog.Single(d => d.Key == TradeFamily.Deals);
        var recruitment = MetricRegistry.Catalog.Single(d => d.Key == MembershipFamily.Routes);
        Assert.Equal("RWS_Details_TradeKindOther", MetricDisplayPolicy.BreakdownLabelKey(trade, "other"));
        Assert.Equal("RWS_Details_Other", MetricDisplayPolicy.BreakdownLabelKey(recruitment, "other"));
        Assert.Equal("RWS_Details_SurgerySuccess",
            MetricDisplayPolicy.BreakdownLabelKey(MetricRegistry.Catalog.Single(d => d.Key == MedicineFamily.Surgeries), "success"));
        Assert.Null(MetricDisplayPolicy.BreakdownLabelKey(recruitment, "success"));
        Assert.Null(MetricDisplayPolicy.BreakdownLabelKey(recruitment, MetricRankings.PermadeathOn));
        Assert.Equal("RWS_Details_Permadeath", MetricDisplayPolicy.BreakdownLabelKey(
            MetricRegistry.Catalog.Single(d => d.Key == MetricRankings.ByPermadeathDays), MetricRankings.PermadeathOn));
    }

}
