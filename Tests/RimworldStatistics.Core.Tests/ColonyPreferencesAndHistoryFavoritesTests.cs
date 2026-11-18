using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ColonyPreferencesAndHistoryFavoritesTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

    private static SaveObservation MakeSave(
        string file,
        string seed,
        int ticks,
        string? factionName = "New Arrivals",
        string? worldName = "Rimenia",
        double wealthMax = 25000d,
        double wealthLast = 22000d,
        int raids = 4) => new()
    {
        FileName = file,
        FileSize = 1024,
        LastWriteUtc = Now,
        ReaderVersion = SaveReader.Version,
        Status = ObservationStatus.Ok,
        Identity = new ColonyIdentity
        {
            SeedString = seed,
            StartingTile = "10,20",
            PersistentRandomValue = seed.GetHashCode(StringComparison.Ordinal),
            FirstWealthBits = 42,
            StartingPawnIds = [$"Thing_Human_{seed}"],
        },
        Display = new DisplayInfo
        {
            FactionName = factionName,
            WorldName = worldName,
            Scenario = "Crashlanded",
            Storyteller = "Cassandra Classic",
        },
        Values = new ObservedValues
        {
            TicksGame = ticks,
            RealPlayTime = 3600d,
            WealthMax = wealthMax,
            WealthLast = wealthLast,
            WealthPoints = 10,
            NumRaidsEnemy = raids,
            NumThreatsQueued = 6,
            ColonistsKilled = 1,
            GreatestPopulation = 8,
        },
    };

    private static MetricObservation MakeMetricObservation(SaveObservation obs, string metricKey, long value)
    {
        var metric = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile,
            FormatVersion = SaveReader.Version,
            Key = obs.FileName,
            Identity = obs.Identity.Clone(),
            SaveTick = obs.Values.TicksGame,
            ObservedUtc = obs.LastWriteUtc,
        };
        metric.FileAliases.Add(new MetricFileAlias
        {
            FileName = obs.FileName,
            FileSize = obs.FileSize,
            LastWriteUtc = obs.LastWriteUtc,
        });
        metric.Metrics[metricKey] = MetricEntry.Of(
            MetricKeys.Revision(metricKey),
            new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = value });
        return metric;
    }

    [Fact]
    public void LegacyAndMalformedSettingsJson_ParseSafely_AndPreserveExistingFavorites()
    {
        const string legacyJson = """
            {"version":1,"groups":[{"id":"g1","name":"Main run","colonyKeys":["col-1"]}],"favoriteMetricKeys":["basic.days","quests.completed"]}
            """;

        var parsed = ViewPreferences.Parse(legacyJson);
        Assert.Single(parsed.Groups);
        Assert.Equal(["basic.days", "quests.completed"], parsed.FavoriteMetricKeys);
        Assert.Empty(parsed.ColonyDisplayNames);
        Assert.Empty(parsed.FavoriteColonyMetrics);

        Assert.Empty(ViewPreferences.Parse(null).ColonyDisplayNames);
        Assert.Empty(ViewPreferences.Parse("not-json{{{").FavoriteColonyMetrics);
        Assert.Empty(ViewPreferences.Parse("""{"version":99,"colonyDisplayNames":{"c1":"Future"}}""").ColonyDisplayNames);
    }

    [Fact]
    public void ViewPreferences_SanitizesDisplayNamesAndDeduplicatesColonyMetricFavorites_AcrossBackupRestore()
    {
        var overlong = new string('A', 90);
        var dirtyJson = $$"""
            {
              "version": 1,
              "favoriteMetricKeys": ["basic.days", "basic.days"],
              "colonyDisplayNames": {
                "col-a": "  Glacier Bastion  ",
                "col-empty": "   ",
                "col-long": "{{overlong}}"
              },
              "favoriteColonyMetrics": [
                { "colonyKey": "col-a", "metricKey": "basic.days" },
                { "colonyKey": "col-a", "metricKey": "basic.days" },
                { "colonyKey": "", "metricKey": "basic.raids" },
                { "colonyKey": "col-a", "metricKey": "" },
                { "colonyKey": "col-b", "metricKey": "basic.peak_wealth" }
              ]
            }
            """;

        var parsed = ViewPreferences.Parse(dirtyJson);
        Assert.Equal(["basic.days"], parsed.FavoriteMetricKeys);
        Assert.Equal(2, parsed.ColonyDisplayNames.Count);
        Assert.Equal("Glacier Bastion", parsed.ColonyDisplayNames["col-a"]);
        Assert.Equal(ViewPreferences.MaxNameLength, parsed.ColonyDisplayNames["col-long"].Length);
        Assert.False(parsed.ColonyDisplayNames.ContainsKey("col-empty"));

        Assert.Equal(2, parsed.FavoriteColonyMetrics.Count);
        Assert.Equal(("col-a", "basic.days"), (parsed.FavoriteColonyMetrics[0].ColonyKey, parsed.FavoriteColonyMetrics[0].MetricKey));
        Assert.Equal(("col-b", "basic.peak_wealth"), (parsed.FavoriteColonyMetrics[1].ColonyKey, parsed.FavoriteColonyMetrics[1].MetricKey));

        using var backups = new TempDir();
        using var data = new TempDir();
        var backupPath = ProfileBackupTests.Make(backups, preferences: parsed.ToJson());
        var handoff = new RestoredPreferences();
        handoff.Offer(ProfileBackup.Restore(backupPath, data.Path), requested: true);

        string? restoredJson = null;
        Assert.True(handoff.ApplyTo(j => restoredJson = j));
        var restored = ViewPreferences.Parse(restoredJson);
        Assert.Equal("Glacier Bastion", restored.ColonyDisplayNames["col-a"]);
        Assert.Equal(2, restored.FavoriteColonyMetrics.Count);
        Assert.Equal(["basic.days"], restored.FavoriteMetricKeys);
    }

    [Fact]
    public void ColonyPresentation_ResolvesSameNamedColoniesIndependently_SearchesAllFields_AndNeverAltersAggregation()
    {
        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => Now);
        session.Mutate(p =>
        {
            p.SaveObservations.Add(MakeSave("run1.rws", "SeedOne", 180000, factionName: "New Arrivals", worldName: "Borealis", wealthMax: 40000));
            p.SaveObservations.Add(MakeSave("run2.rws", "SeedTwo", 300000, factionName: "New Arrivals", worldName: "Tropica", wealthMax: 60000));
        }, SaveMode.None);

        var view = session.GetView(new StatsOptions());
        Assert.Equal(2, view.Stats.ColonyCount);
        var borealis = view.Stats.Colonies.Single(c => c.WorldName == "Borealis");
        var tropica = view.Stats.Colonies.Single(c => c.WorldName == "Tropica");

        var prefs = new ViewPreferences();
        string Fallback(string? w) => $"Unnamed ({w ?? "-"})";

        Assert.True(ColonyPresentation.SetCustomName(prefs, borealis.Key, borealis.AliasKeys, "  Northern Vault  "));
        Assert.Equal("Northern Vault", ColonyPresentation.ResolveName(borealis, prefs, Fallback));
        Assert.Equal("New Arrivals", ColonyPresentation.ResolveName(tropica, prefs, Fallback));

        // Original faction name, keys, and aggregate statistics remain untouched.
        var afterView = session.GetView(new StatsOptions());
        Assert.Equal("New Arrivals", afterView.Stats.Colonies.Single(c => c.Key == borealis.Key).FactionName);
        Assert.Equal(480000L, afterView.Stats.TotalTicks);
        Assert.Equal(100000d, afterView.Stats.PeakWealthSum);

        // Search matches custom display name, overridden original faction name, and world name.
        Assert.True(ColonyPresentation.MatchesSearch(borealis, prefs, Fallback, "Northern"));
        Assert.True(ColonyPresentation.MatchesSearch(borealis, prefs, Fallback, "New Arrivals"));
        Assert.True(ColonyPresentation.MatchesSearch(borealis, prefs, Fallback, "Borealis"));
        Assert.False(ColonyPresentation.MatchesSearch(borealis, prefs, Fallback, "Tropica"));

        // Clearing custom name restores original faction name.
        Assert.True(ColonyPresentation.ClearCustomName(prefs, borealis.Key, borealis.AliasKeys));
        Assert.False(ColonyPresentation.HasCustomName(prefs, borealis));
        Assert.Equal("New Arrivals", ColonyPresentation.ResolveName(borealis, prefs, Fallback));
    }

    [Fact]
    public void ColonyMerge_PreservesDisplayNameAndFavoritesFromLegacyAliasKey_AndDeduplicatesOnRemove()
    {
        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => Now);
        session.Mutate(p =>
        {
            p.SaveObservations.Add(MakeSave("early.rws", "SeedA", 120000, factionName: "Outpost Alpha", worldName: "Tellus"));
            p.SaveObservations.Add(MakeSave("late.rws", "SeedB", 360000, factionName: "Citadel Alpha", worldName: "Tellus"));
        }, SaveMode.None);

        var preMerge = session.GetView(new StatsOptions());
        var first = preMerge.Stats.Colonies.Single(c => c.FactionName == "Outpost Alpha");
        var second = preMerge.Stats.Colonies.Single(c => c.FactionName == "Citadel Alpha");

        var prefs = new ViewPreferences();
        // Register display name and favorite on 'second' (which will become an alias after manual merge into 'first'),
        // plus a duplicate favorite for the same metric on 'first'.
        ColonyPresentation.SetCustomName(prefs, second.Key, second.AliasKeys, "Alpha Campaign");
        ColonyHistoryFavorites.Toggle(prefs, second.Key, second.AliasKeys, BasicMetrics.Days);
        ColonyHistoryFavorites.Toggle(prefs, first.Key, first.AliasKeys, BasicMetrics.Days);
        ColonyHistoryFavorites.Toggle(prefs, second.Key, second.AliasKeys, BasicMetrics.Raids);

        session.Mutate(p => p.UserDecisions.ManualMerges.Add([first.Key, second.Key]), SaveMode.None);
        var mergedView = session.GetView(new StatsOptions());
        var mergedColony = Assert.Single(mergedView.Stats.Colonies);
        Assert.Equal(first.Key, mergedColony.Key);
        Assert.Contains(second.Key, mergedColony.AliasKeys);

        // Display name set on legacy key resolves on the merged colony, and updating/clearing cleans up the stale alias key.
        Assert.Equal("Alpha Campaign", ColonyPresentation.ResolveName(mergedColony, prefs, _ => "Unnamed"));
        Assert.True(ColonyPresentation.SetCustomName(prefs, mergedColony.Key, mergedColony.AliasKeys, "Unified Alpha"));
        Assert.False(prefs.ColonyDisplayNames.ContainsKey(second.Key));
        Assert.Equal("Unified Alpha", prefs.ColonyDisplayNames[mergedColony.Key]);
        Assert.True(ColonyPresentation.ClearCustomName(prefs, mergedColony.Key, mergedColony.AliasKeys));
        Assert.Empty(prefs.ColonyDisplayNames);

        // Duplicate pre-merge favorites for BasicMetrics.Days coalesce into 1 resolved entry targeting CanonicalColonyKey.
        var resolved = ColonyHistoryFavorites.ResolveAll(mergedView, prefs);
        Assert.Equal(2, resolved.Count);
        var daysFav = resolved.Single(r => r.MetricKey == BasicMetrics.Days);
        Assert.True(daysFav.CanOpen);
        Assert.Equal(mergedColony.Key, daysFav.CanonicalColonyKey);
        Assert.Equal(2, daysFav.MatchedStoredColonyKeys.Count);

        var raidsFav = resolved.Single(r => r.MetricKey == BasicMetrics.Raids);
        Assert.True(raidsFav.CanOpen);
        Assert.Equal(mergedColony.Key, raidsFav.CanonicalColonyKey);

        // Removing the coalesced favorite removes both canonical and legacy alias entries in a single action.
        Assert.True(ColonyHistoryFavorites.Remove(prefs, daysFav));
        Assert.Single(ColonyHistoryFavorites.ResolveAll(mergedView, prefs));
        Assert.DoesNotContain(prefs.FavoriteColonyMetrics, f => f.MetricKey == BasicMetrics.Days);
    }

    [Fact]
    public void ColonyHistoryFavorites_ClassifiesOpenableAndAllUnopenableCases_AndAllowsRemovingBrokenEntries()
    {
        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => Now);
        var activeSave = MakeSave("active.rws", "SeedActive", 240000, factionName: "Sunspire", worldName: "Helios");
        var excludedSave = MakeSave("excl.rws", "SeedExcl", 120000, factionName: "Scrapheap", worldName: "Nox");
        var outOfScopeSave = MakeSave("other.rws", "SeedOther", 180000, factionName: "Farwatch", worldName: "Vega");

        var rememberedModMetric = new ModMetricDescriptor
        {
            ProviderId = "example.disabledmod.provider",
            OwnerPackageId = "example.disabledmod",
            FamilyId = "reactor",
            MetricName = "custom_output",
            Revision = 1,
            Selection = ColonySelection.Representative,
            Overall = OverallAggregation.Sum,
            LabelKey = "Example_ReactorOutput",
            DescriptionKey = "Example_ReactorOutputDesc",
        };

        session.Mutate(p =>
        {
            p.SaveObservations.AddRange([activeSave, excludedSave, outOfScopeSave]);
            p.MetricObservations.Add(MakeMetricObservation(activeSave, MetricKeys.ModsLatest, 7));
            ProfileMutations.RememberModMetrics(p, [rememberedModMetric]);
            ProfileMutations.UpsertEnding(p, new EndingEvent
            {
                CampaignId = "ending-only-campaign",
                Type = EndingType.Ship,
                Ordinal = 0,
                Source = EndingSource.Live,
                RealTimeUtc = Now,
                Departed = 5,
            });
        }, SaveMode.None);

        var fullView = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        var activeColony = fullView.Stats.Colonies.Single(c => c.FactionName == "Sunspire");
        var excludedColony = fullView.Stats.Colonies.Single(c => c.FactionName == "Scrapheap");
        var outOfScopeColony = fullView.Stats.Colonies.Single(c => c.FactionName == "Farwatch");
        var endingOnlyColony = fullView.Stats.Colonies.Single(c => !c.HasObservedPoint);

        session.Mutate(
            p => ProfileMutations.SetExcluded(p, [excludedColony.Key, .. excludedColony.AliasKeys], excluded: true),
            SaveMode.None);

        var scopeGroup = new ColonyGroup
        {
            Id = "g-main",
            Name = "Main Scope",
            ColonyKeys = [activeColony.Key, excludedColony.Key, endingOnlyColony.Key],
        };
        var scopedView = session.GetView(
            new StatsOptions(),
            MetricDisplayPolicy.AllDetailsVisibleUnits,
            ViewScope.ForGroup(scopeGroup));

        var prefs = new ViewPreferences
        {
            FavoriteMetricKeys = [BasicMetrics.Days],
            FavoriteColonyMetrics =
            [
                new() { ColonyKey = activeColony.Key, MetricKey = BasicMetrics.PeakWealth },
                new() { ColonyKey = activeColony.Key, MetricKey = CrossColonyCatalog.MaxPrefix + MetricKeys.ModsLatest },
                new() { ColonyKey = "missing-colony-key", MetricKey = BasicMetrics.Days },
                new() { ColonyKey = excludedColony.Key, MetricKey = BasicMetrics.Days },
                new() { ColonyKey = outOfScopeColony.Key, MetricKey = BasicMetrics.Days },
                new() { ColonyKey = activeColony.Key, MetricKey = "mod.uninstalled.author.missing_metric" },
                new() { ColonyKey = activeColony.Key, MetricKey = BasicMetrics.Endings },
                new() { ColonyKey = activeColony.Key, MetricKey = rememberedModMetric.StableKey },
                new() { ColonyKey = endingOnlyColony.Key, MetricKey = BasicMetrics.Days },
                new() { ColonyKey = activeColony.Key, MetricKey = CrossColonyCatalog.TopPrefix + WorkFamily.MealsByDef },
            ],
        };

        var resolved = ColonyHistoryFavorites.ResolveAll(scopedView, prefs);
        Assert.Equal(10, resolved.Count);

        // 1. Openable basic metric
        Assert.Equal(ColonyHistoryFavoriteStatus.Openable, resolved[0].Status);
        Assert.True(resolved[0].CanOpen);
        Assert.Equal(25000d, resolved[0].ComparisonValue!.Number);

        // 2. Openable derived metric resolved to its underlying per-colony source metric
        Assert.Equal(ColonyHistoryFavoriteStatus.Openable, resolved[1].Status);
        Assert.True(resolved[1].CanOpen);
        Assert.Equal(MetricKeys.ModsLatest, resolved[1].Definition!.Key);
        Assert.Equal(7d, resolved[1].ComparisonValue!.Number);

        // 3. Missing colony
        Assert.Equal(ColonyHistoryFavoriteStatus.ColonyMissing, resolved[2].Status);
        Assert.False(resolved[2].CanOpen);

        // 4. Excluded colony
        Assert.Equal(ColonyHistoryFavoriteStatus.ColonyExcluded, resolved[3].Status);
        Assert.False(resolved[3].CanOpen);

        // 5. Out-of-scope colony
        Assert.Equal(ColonyHistoryFavoriteStatus.ColonyOutOfScope, resolved[4].Status);
        Assert.False(resolved[4].CanOpen);

        // 6. Unknown metric from disabled/unregistered MOD
        Assert.Equal(ColonyHistoryFavoriteStatus.MetricUnknown, resolved[5].Status);
        Assert.False(resolved[5].CanOpen);

        // 7. Metric without per-save colony history
        Assert.Equal(ColonyHistoryFavoriteStatus.MetricNoHistory, resolved[6].Status);
        Assert.False(resolved[6].CanOpen);

        // 8. Remembered MOD metric with no measured reading on this colony
        Assert.Equal(ColonyHistoryFavoriteStatus.NoMeasuredValue, resolved[7].Status);
        Assert.False(resolved[7].CanOpen);
        Assert.Equal("example.disabledmod", resolved[7].Definition?.OwnerPackageId);

        // 9. Ending-only colony without observed save/live points
        Assert.Equal(ColonyHistoryFavoriteStatus.NoMeasuredValue, resolved[8].Status);
        Assert.False(resolved[8].CanOpen);

        // 10. Derived top-type ranking metric without per-colony scalar history
        Assert.Equal(ColonyHistoryFavoriteStatus.MetricNoHistory, resolved[9].Status);
        Assert.False(resolved[9].CanOpen);

        // Reordering works and preserves unopenable entries until explicitly removed.
        Assert.True(ColonyHistoryFavorites.Move(prefs, resolved, 2, -1));
        var reordered = ColonyHistoryFavorites.ResolveAll(scopedView, prefs);
        Assert.Equal("missing-colony-key", reordered[1].StoredColonyKey);

        // Every unopenable entry can be removed cleanly without affecting metric-level favorites.
        foreach (var entry in reordered.Where(r => !r.CanOpen).ToArray())
            Assert.True(ColonyHistoryFavorites.Remove(prefs, entry));

        var remaining = ColonyHistoryFavorites.ResolveAll(scopedView, prefs);
        Assert.Equal(2, remaining.Count);
        Assert.All(remaining, r => Assert.True(r.CanOpen));
        Assert.Equal([BasicMetrics.Days], prefs.FavoriteMetricKeys);
    }

    [Fact]
    public void DerivedCrossColonyHistoryKeys_CoalesceWithSourceMetricAndGuardWhitespaceAliases()
    {
        var prefs = new ViewPreferences
        {
            ColonyDisplayNames = new(StringComparer.Ordinal) { ["valid-key"] = "Valid Name" },
            FavoriteColonyMetrics =
            [
                new() { ColonyKey = "col-1", MetricKey = CrossColonyCatalog.MaxPrefix + MetricKeys.ModsLatest },
                new() { ColonyKey = "col-1", MetricKey = MetricKeys.ModsLatest },
            ],
        };

        Assert.Null(ColonyPresentation.TryGetCustomName(prefs, "other-key", ["   "]));

        Assert.True(ColonyHistoryFavorites.IsFavorite(prefs, "col-1", null, MetricKeys.ModsLatest));
        Assert.True(ColonyHistoryFavorites.IsFavorite(prefs, "col-1", null, CrossColonyCatalog.MaxPrefix + MetricKeys.ModsLatest));

        using var tmp = new TempDir();
        var session = new ProfileSession(new ProfileStore(tmp.Path), () => Now);
        var save = MakeSave("c1.rws", "SeedCanon", 60000, factionName: "Alpha", worldName: "World1");
        session.Mutate(p =>
        {
            p.SaveObservations.Add(save);
            p.MetricObservations.Add(MakeMetricObservation(save, MetricKeys.ModsLatest, 5));
        }, SaveMode.None);

        var view = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        var actualKey = view.Stats.Colonies[0].Key;
        prefs.FavoriteColonyMetrics =
        [
            new() { ColonyKey = actualKey, MetricKey = CrossColonyCatalog.MaxPrefix + MetricKeys.ModsLatest },
            new() { ColonyKey = actualKey, MetricKey = MetricKeys.ModsLatest },
        ];

        var resolved = ColonyHistoryFavorites.ResolveAll(view, prefs);
        Assert.Single(resolved);
        Assert.Equal(MetricKeys.ModsLatest, resolved[0].MetricKey);

        // Toggling from the opened history chart (which uses the canonical source key) removes both entries cleanly.
        Assert.True(ColonyHistoryFavorites.Toggle(prefs, actualKey, null, MetricKeys.ModsLatest));
        Assert.Empty(prefs.FavoriteColonyMetrics);
    }
}
