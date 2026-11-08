namespace RimworldStatistics.Core.Tests;

/// <summary>Issue #27: compact live SaveSnapshots without changing any published selection or sample count.</summary>
public sealed class NumericSnapshotRetentionTests
{
    private static readonly DateTime Epoch = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyIdentity Identity(string campaign = "snapshot-retention") => new()
    {
        SeedString = "retention-test", PersistentRandomValue = 3, StartingTile = "123,0",
        GameStartAbsTick = 1, FirstWealthBits = 17,
        ModCampaignId = campaign, StartingPawnIds = { "Thing_Human1" },
    };

    private static Profile Build(bool compact)
    {
        var profile = new Profile();
        var id = Identity();
        ProfileMutations.ApplyLiveObservation(profile, id.ModCampaignId!, id, new ObservedValues { TicksGame = 3000 },
            new DisplayInfo(), Epoch);
        for (var i = 0; i < 250; i++)
        {
            var observation = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot,
                FormatVersion = 1, Key = "snapshot-" + i, Identity = id.Clone(),
                ObservedUtc = Epoch.AddMinutes(i), SaveTick = i,
                ActiveDlcIds = i % 3 == 0 ? new List<string> { AcquisitionUnits.Royalty } : new List<string>(),
            };
            void Number(string key, double v, MetricCoverage coverage = MetricCoverage.SnapshotOnly)
                => observation.Metrics[key] = MetricEntry.Of(MetricKeys.Revision(key),
                    new MetricValue { Coverage = coverage, RealValue = v });
            Number(FacilitiesSnapshot.OutdoorTempMax, i == 40 ? 100 : i % 11);
            Number(FacilitiesSnapshot.OutdoorTempMin, i == 25 ? -100 : -(i % 7));
            // A missing and a partial observation are witnesses that history is incomplete.
            if (i != 15)
                Number(RosterSnapshot.AugmentedPeak, i == 45 ? 50 : i % 6,
                    i == 17 ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly);
            Number(StockSnapshot.PersonaWeaponsPeak, i % 3 == 0 ? i % 5 : 0);
            var generator = MetricEntry.Of(MetricKeys.Revision(FacilitiesSnapshot.RunningGenerators),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = i == 249 ? 1 : 3 });
            generator.Values["SolarGenerator"] = new MetricValue
            {
                Coverage = MetricCoverage.SnapshotOnly, IntegerValue = i == 249 ? 1 : 3,
            };
            observation.Metrics[FacilitiesSnapshot.RunningGenerators] = generator;
            // Unknown future revision/metric must be kept conservatively.
            if (i == 5)
                Number("future.metric", 123);
            if (compact)
                ProfileMutations.UpsertMetricObservation(profile, observation);
            else
                profile.MetricObservations.Add(observation);
        }
        return profile;
    }

    private static IReadOnlyList<NumericRow> Rows(Profile p)
    {
        var defs = SnapshotCatalog.Definitions.Where(d => d.Key is
            FacilitiesSnapshot.OutdoorTempMax or FacilitiesSnapshot.OutdoorTempMin
            or FacilitiesSnapshot.RunningGenerators or RosterSnapshot.AugmentedPeak
            or StockSnapshot.PersonaWeaponsPeak).ToList();
        var colonies = CampaignMatcher.BuildColonies(p);
        return Assert.Single(MetricCalculator.BuildView(p, colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()), defs).Colonies).Rows;
    }

    [Fact]
    public void HundredsOfSnapshots_CompactWithoutChangingLatestExtremaCoverageOrSampleCounts()
    {
        var raw = Build(compact: false);
        var compact = Build(compact: true);
        var rowsBefore = Rows(raw).ToDictionary(r => r.Definition.Key);
        var rowsAfter = Rows(compact).ToDictionary(r => r.Definition.Key);
        foreach (var (key, before) in rowsBefore)
        {
            var after = rowsAfter[key];
            Assert.Equal(before.Value.Number, after.Value.Number);
            Assert.Equal(before.Value.Coverage, after.Value.Coverage);
            Assert.Equal(before.Value.SampleCount, after.Value.SampleCount);
            Assert.Equal(before.Value.ThroughTick, after.Value.ThroughTick);
            Assert.Equal(before.Targets, after.Targets);
            Assert.Equal(before.Valid, after.Valid);
            Assert.Equal(before.Breakdowns.Select(b => (b.Breakdown, b.Value.Number)),
                after.Breakdowns.Select(b => (b.Breakdown, b.Value.Number)));
        }
        Assert.Equal(250, raw.MetricObservations.Count);
        Assert.InRange(compact.MetricObservations.Count, 2, 16);
        Assert.Contains(compact.MetricObservations, o => o.Key == "snapshot-5");
        Assert.Contains(compact.MetricObservations, o => o.Key == "snapshot-249");
        Assert.Equal(250, rowsAfter[FacilitiesSnapshot.OutdoorTempMax].Value.SampleCount);
        Assert.Equal(249, rowsAfter[RosterSnapshot.AugmentedPeak].Value.SampleCount);
        Assert.Equal(MetricCoverage.Partial, rowsAfter[RosterSnapshot.AugmentedPeak].Value.Coverage);
    }

    [Fact]
    public void Compaction_RemainsCorrectAcrossJsonRoundTrip()
    {
        var compact = Build(compact: true);
        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(compact));
        var a = Rows(compact).ToDictionary(r => r.Definition.Key);
        var b = Rows(restored).ToDictionary(r => r.Definition.Key);
        foreach (var key in a.Keys)
        {
            Assert.Equal(a[key].Value.Number, b[key].Value.Number);
            Assert.Equal(a[key].Value.SampleCount, b[key].Value.SampleCount);
            Assert.Equal(a[key].Value.Coverage, b[key].Value.Coverage);
        }
    }
}
