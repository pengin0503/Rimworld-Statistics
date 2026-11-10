namespace RimworldStatistics.Core.Tests;

public sealed class PostV14DlcCompatibilityTests
{
    private static readonly DateTime Utc = new(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyIdentity Identity(string id) => new()
    {
        SeedString = id, PersistentRandomValue = 1, StartingTile = "10,0",
        GameStartAbsTick = 0, FirstWealthBits = 1,
        ModCampaignId = "synthetic-" + id,
        StartingPawnIds = new List<string> { "Pawn" + id },
    };

    private static Profile ThreeIndependentDlcStates()
    {
        var p = new Profile();
        var d = MetricRegistry.Find(BiotechFamily.MechsCreated)!;
        foreach (var (id, active, value) in new[]
        {
            ("no-dlc", false, 0L), ("with-dlc-zero", true, 0L), ("with-dlc-three", true, 3L),
        })
        {
            var identity = Identity(id);
            var file = id + ".rws";
            p.SaveObservations.Add(new SaveObservation
            {
                FileName = file, FileSize = 100, LastWriteUtc = Utc,
                ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok,
                Identity = identity.Clone(), Values = { TicksGame = 60000 },
            });
            var captured = new MetricObservation
            {
                Source = d.Source, Key = "fixture-" + id,
                Identity = identity.Clone(), FormatVersion = SaveReader.Version,
                SaveTick = 60000, ObservedUtc = Utc,
                ActiveDlcIds = active ? new List<string> { "ludeon.rimworld.biotech" } : new List<string>(),
                ModSetHash = active ? new string('a', 64) : new string('b', 64),
            };
            captured.FileAliases.Add(new MetricFileAlias
                { FileName = file, FileSize = 100, LastWriteUtc = Utc });
            captured.Metrics[d.StoredKey] = MetricEntry.Of(MetricKeys.Revision(d.StoredKey),
                new MetricValue { IntegerValue = value, Coverage = MetricCoverage.Complete });
            p.MetricObservations.Add(captured);
        }
        return p;
    }

    [Fact]
    public void AbsentDlcZero_IsInapplicableNotZero_WhileEnabledZeroCounts()
    {
        var d = MetricRegistry.Find(BiotechFamily.MechsCreated)!;
        Assert.Equal("ludeon.rimworld.biotech", d.RequiredDlc);
        var p = ThreeIndependentDlcStates();
        var cols = CampaignMatcher.BuildColonies(p);
        Assert.Equal(3, cols.Count);
        var derived = MetricRegistry.Find(CrossColonyCatalog.MinPrefix + BiotechFamily.MechsCreated)!;
        var view = MetricCalculator.BuildView(p, cols, StatsCalculator.Calculate(cols, new StatsOptions()),
            new[] { d, derived });
        var overall = view.Overall.Single(r => r.Definition.Key == d.Key);
        Assert.Equal(3d, overall.Value.Number);
        Assert.Equal((2, 2, MetricCoverage.Complete),
            (overall.Targets, overall.Valid, overall.Value.Coverage));
        var minimum = view.Overall.Single(r => r.Definition.Key == derived.Key);
        Assert.Equal(0d, minimum.Value.Number);
        Assert.Equal(2, minimum.Targets);
        Assert.Equal(2, minimum.Valid);
        Assert.Equal(2, view.Colonies.Count(c => c.Rows.Single(r => r.Definition.Key == d.Key).Targets == 1));
        Assert.Equal(1, view.Colonies.Count(c => c.Rows.Single(r => r.Definition.Key == d.Key)
            .Value.Coverage == MetricCoverage.NotApplicable));
    }

    [Fact]
    public void RemovedDlcHasNoAutomaticZero_AndActiveDlcWithoutObservationIsUnknown()
    {
        var d = MetricRegistry.Find(BiotechFamily.MechsCreated)!;
        var p = ThreeIndependentDlcStates();
        var missing = p.MetricObservations.Single(o => o.Key == "fixture-with-dlc-zero");
        missing.Metrics.Remove(d.StoredKey);
        var cols = CampaignMatcher.BuildColonies(p);
        var view = MetricCalculator.BuildView(p, cols, StatsCalculator.Calculate(cols, new StatsOptions()),
            new[] { d });
        var row = Assert.Single(view.Overall);
        Assert.Equal((2, 1, MetricCoverage.Partial),
            (row.Targets, row.Valid, row.Value.Coverage));
        Assert.Equal(3d, row.Value.Number);
    }

    [Fact]
    public void ModFingerprintAndDlcCapture_RoundTripWithoutChangingCatalog()
    {
        var p = ThreeIndependentDlcStates();
        var json = ProfileJson.Serialize(p);
        var restored = ProfileJson.Deserialize(json);
        Assert.Equal(3, restored.MetricObservations.Count);
        foreach (var original in p.MetricObservations)
        {
            var copy = restored.MetricObservations.Single(o => o.Key == original.Key);
            Assert.Equal(original.ModSetHash, copy.ModSetHash);
            Assert.Equal(original.ActiveDlcIds, copy.ActiveDlcIds);
            Assert.Equal(original.Metrics.Keys.Order(StringComparer.Ordinal),
                copy.Metrics.Keys.Order(StringComparer.Ordinal));
        }
        Assert.Equal(415, MetricRegistry.Catalog.Count);
    }
}
