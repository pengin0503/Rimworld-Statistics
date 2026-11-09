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
    [Fact]
    public void Compaction_NeverCombinesDifferentCampaignsOrOtherRevisions()
    {
        var p = Build(compact: true);
        var firstCampaignCount = p.MetricObservations.Count;
        var other = Identity("another-campaign-with-identical-fingerprint");
        ProfileMutations.ApplyLiveObservation(p, other.ModCampaignId!, other,
            new ObservedValues { TicksGame = 10 }, new DisplayInfo(), Epoch);
        for (var i = 0; i < 15; i++)
        {
            var o = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Key = "other-" + i,
                Identity = other.Clone(), SaveTick = i, ObservedUtc = Epoch.AddSeconds(i),
            };
            o.Metrics[FacilitiesSnapshot.OutdoorTempMax] = MetricEntry.Of(
                MetricKeys.Revision(FacilitiesSnapshot.OutdoorTempMax),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = i });
            ProfileMutations.UpsertMetricObservation(p, o);
        }
        Assert.Equal(firstCampaignCount, p.MetricObservations.Count(o =>
            o.Identity.ModCampaignId == "snapshot-retention"));
        Assert.InRange(p.MetricObservations.Count(o =>
            o.Identity.ModCampaignId == other.ModCampaignId), 2, 4);

        var legacy = new MetricObservation
        {
            Source = MetricObservationSource.SaveSnapshot, Key = "legacy",
            Identity = other.Clone(), SaveTick = 20, ObservedUtc = Epoch.AddSeconds(20),
        };
        legacy.Metrics[FacilitiesSnapshot.OutdoorTempMax] = MetricEntry.Of(999,
            new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 999 });
        ProfileMutations.UpsertMetricObservation(p, legacy);
        for (var i = 21; i < 30; i++)
        {
            var o = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Key = "other-" + i,
                Identity = other.Clone(), SaveTick = i, ObservedUtc = Epoch.AddSeconds(i),
            };
            o.Metrics[FacilitiesSnapshot.OutdoorTempMax] = MetricEntry.Of(
                MetricKeys.Revision(FacilitiesSnapshot.OutdoorTempMax),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = i });
            ProfileMutations.UpsertMetricObservation(p, o);
        }
        Assert.Contains(p.MetricObservations, o => o.Key == "legacy");
        Assert.Equal(firstCampaignCount, p.MetricObservations.Count(o =>
            o.Identity.ModCampaignId == "snapshot-retention"));
    }

    [Fact]
    public void Compaction_MatchesUncompactedHistoryAcrossMissingPartialAndDlcChanges()
    {
        var definitions = SnapshotCatalog.Definitions.Where(d => d.Key is
            FacilitiesSnapshot.OutdoorTempMax or FacilitiesSnapshot.OutdoorTempMin
            or RosterSnapshot.AugmentedPeak or StockSnapshot.PersonaWeaponsPeak
            or FacilitiesSnapshot.RunningGenerators).ToArray();
        for (var seed = 0; seed < 12; seed++)
        {
            var random = new Random(seed + 1234);
            var original = new Profile();
            var compact = new Profile();
            var identity = Identity("random-campaign-" + seed);
            foreach (var p in new[] { original, compact })
                ProfileMutations.ApplyLiveObservation(p, identity.ModCampaignId!, identity,
                    new ObservedValues { TicksGame = 20000 }, new DisplayInfo(), Epoch);
            for (var i = 0; i < 65; i++)
            {
                var snapshot = new MetricObservation
                {
                    Source = MetricObservationSource.SaveSnapshot, Key = "random-" + seed + "-" + i,
                    Identity = identity.Clone(), SaveTick = random.Next(1000),
                    ObservedUtc = Epoch.AddSeconds(i),
                    ActiveDlcIds = random.Next(3) == 0 ? new List<string> { AcquisitionUnits.Royalty } : new List<string>(),
                };
                foreach (var d in definitions)
                {
                    if (random.Next(9) == 0)
                        continue;
                    var value = new MetricValue
                    {
                        Coverage = random.Next(7) == 0 ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly,
                        RealValue = random.Next(12) - 5,
                    };
                    snapshot.Metrics[d.StoredKey] = MetricEntry.Of(d.Revision, value);
                }
                original.MetricObservations.Add(snapshot);
                ProfileMutations.UpsertMetricObservation(compact, snapshot is { } m ? new MetricObservation
                {
                    Source = m.Source, Key = m.Key, Identity = m.Identity.Clone(),
                    SaveTick = m.SaveTick, ObservedUtc = m.ObservedUtc, ActiveDlcIds = m.ActiveDlcIds?.ToList(),
                    Metrics = m.Metrics.ToDictionary(x => x.Key, x => x.Value),
                } : throw new InvalidOperationException());
            }
            var raw = Rows(original).ToDictionary(r => r.Definition.Key);
            var reduced = Rows(compact).ToDictionary(r => r.Definition.Key);
            foreach (var d in definitions)
            {
                var a = raw[d.Key];
                var b = reduced[d.Key];
                Assert.True(a.Value.Number == b.Value.Number &&
                    a.Value.Coverage == b.Value.Coverage &&
                    a.Value.SampleCount == b.Value.SampleCount &&
                    a.Value.ThroughTick == b.Value.ThroughTick &&
                    a.Targets == b.Targets && a.Valid == b.Valid,
                    $"seed={seed}, key={d.Key}: raw=({a.Value.Number},{a.Value.Coverage},{a.Value.SampleCount},{a.Value.ThroughTick}) " +
                    $"compact=({b.Value.Number},{b.Value.Coverage},{b.Value.SampleCount},{b.Value.ThroughTick})");
            }
        }
    }

    [Fact]
    public void Compaction_DoesNotEraseUnknownJsonFieldsAtAnyNestingLevel()
    {
        static System.Text.Json.JsonElement Json(string value)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(value);
            return doc.RootElement.Clone();
        }

        var p = new Profile();
        var id = Identity("extension-data");
        for (var i = 0; i < 8; i++)
        {
            var obs = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Key = "ext-" + i,
                Identity = id.Clone(), SaveTick = i, ObservedUtc = Epoch.AddSeconds(i),
            };
            var entry = MetricEntry.Of(MetricKeys.Revision(FacilitiesSnapshot.OutdoorTempMax),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = i });
            obs.Metrics[FacilitiesSnapshot.OutdoorTempMax] = entry;
            switch (i)
            {
                case 0:
                    obs.ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
                        { ["futureObservation"] = Json("{\"revision\":3}") };
                    break;
                case 1:
                    obs.Identity.ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
                        { ["futureIdentity"] = Json("\"extra\"") };
                    break;
                case 2:
                    entry.ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
                        { ["futureEntry"] = Json("true") };
                    break;
                case 3:
                    entry.Values[MetricEntry.Scalar].ExtensionData = new Dictionary<string, System.Text.Json.JsonElement>
                        { ["futureValue"] = Json("23") };
                    break;
            }
            ProfileMutations.UpsertMetricObservation(p, obs);
        }
        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(p));
        Assert.Equal(new[] { "ext-0", "ext-1", "ext-2", "ext-3" },
            restored.MetricObservations.Where(o => o.Key is "ext-0" or "ext-1" or "ext-2" or "ext-3")
                .Select(o => o.Key).Order().ToArray());
        Assert.True(restored.MetricObservations.Single(o => o.Key == "ext-0").ExtensionData!.ContainsKey("futureObservation"));
        Assert.True(restored.MetricObservations.Single(o => o.Key == "ext-1").Identity.ExtensionData!.ContainsKey("futureIdentity"));
        Assert.True(restored.MetricObservations.Single(o => o.Key == "ext-2")
            .Metrics[FacilitiesSnapshot.OutdoorTempMax].ExtensionData!.ContainsKey("futureEntry"));
        Assert.True(restored.MetricObservations.Single(o => o.Key == "ext-3")
            .Metrics[FacilitiesSnapshot.OutdoorTempMax].Values[MetricEntry.Scalar].ExtensionData!.ContainsKey("futureValue"));
    }

    [Fact]
    public void Compaction_PreservesUnknownProviderCapturePropertiesOnAnIntermediateSnapshot()
    {
        // Opaque provider metadata may have been written by a newer SDK revision.
        // A non-extremal intermediate snapshot is normally removed; its unknown
        // JSON must survive when the current reader cannot merge it losslessly.
        using var doc = System.Text.Json.JsonDocument.Parse("{\"providerNext\":42}");
        var descriptor = new ModMetricDescriptor
        {
            ProviderId = "sample.capture", OwnerPackageId = "sample.analytics",
            FamilyId = "stock", MetricName = "total", Revision = 1,
            Source = MetricObservationSource.SaveSnapshot,
            Category = MetricCategory.Colony, Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.ObservationMax,
        };
        var id = Identity("capture-extension");
        var p = new Profile();
        ProfileMutations.ApplyLiveObservation(p, id.ModCampaignId!, id,
            new ObservedValues { TicksGame = 1000 }, new DisplayInfo(), Epoch);
        ProfileMutations.RememberModMetrics(p, new[] { descriptor });

        for (var i = 0; i < 12; i++)
        {
            var row = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot,
                Key = "capture-" + i,
                Identity = id.Clone(),
                SaveTick = i,
                ObservedUtc = Epoch.AddMinutes(i),
            };
            row.Metrics[descriptor.StableKey] = MetricEntry.Of(1,
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = i });
            row.ProviderCaptures = new Dictionary<string, ModProviderCapture>
            {
                [descriptor.ProviderId] = new ModProviderCapture("sample.analytics", "1.2.0", "1.1.0")
                {
                    ExtensionData = i == 3
                        ? new Dictionary<string, System.Text.Json.JsonElement>
                            { ["futureCaptureMetadata"] = doc.RootElement.Clone() }
                        : null,
                },
            };
            ProfileMutations.UpsertMetricObservation(p, row);
        }

        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(p));
        var protectedRow = Assert.Single(restored.MetricObservations, o => o.Key == "capture-3");
        Assert.Equal(42, protectedRow.ProviderCaptures![descriptor.ProviderId]
            .ExtensionData!["futureCaptureMetadata"].GetProperty("providerNext").GetInt32());
        Assert.Contains(restored.MetricObservations, o => o.Key == "capture-11");
        Assert.True(restored.MetricObservations.Count < 12,
            "Ordinary provider snapshots must still be eligible for compaction");
    }

    [Fact]
    public void UnattributedSnapshots_AreNotCompactedUntilAColonyCanOwnThem()
    {
        var profile = new Profile();
        var identity = Identity("unattributed-campaign");
        for (var i = 0; i < 10; i++)
        {
            var snapshot = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Key = "unattributed-" + i,
                Identity = identity.Clone(), SaveTick = i, ObservedUtc = Epoch.AddMinutes(i),
            };
            snapshot.Metrics[FacilitiesSnapshot.OutdoorTempMax] = MetricEntry.Of(
                MetricKeys.Revision(FacilitiesSnapshot.OutdoorTempMax),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = i });
            ProfileMutations.UpsertMetricObservation(profile, snapshot);
        }
        var colonies = CampaignMatcher.BuildColonies(profile);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        var view = MetricCalculator.BuildView(profile, colonies, stats);
        Assert.Empty(colonies);
        Assert.Equal(10, view.Unattributed);
        Assert.Equal(10, profile.MetricObservations.Count);
    }

    [Fact]
    public void Compaction_RetainsOriginalDisplayedObservationCountEvenAfterLosingAttribution()
    {
        var raw = Build(compact: false);
        var compact = Build(compact: true);
        NumericView View(Profile p)
        {
            var colonies = CampaignMatcher.BuildColonies(p);
            return MetricCalculator.BuildView(p, colonies,
                StatsCalculator.Calculate(colonies, new StatsOptions()));
        }
        Assert.Equal(250, Assert.Single(View(raw).Colonies).Observations);
        Assert.Equal(250, Assert.Single(View(compact).Colonies).Observations);
        compact = ProfileJson.Deserialize(ProfileJson.Serialize(compact));
        Assert.Equal(250, Assert.Single(View(compact).Colonies).Observations);
        compact.LiveCampaigns.Clear();
        var unknown = View(compact);
        Assert.Empty(unknown.Colonies);
        Assert.Equal(250, unknown.Unattributed);
    }

    [Fact]
    public void HistoricalFallback_DoesNotOverrideLastActualFactionCaptureAfterCompaction()
    {
        var profile = new Profile();
        var identity = Identity("saved-factions-then-disabled");
        ProfileMutations.ApplyLiveObservation(profile, identity.ModCampaignId!, identity,
            new ObservedValues { TicksGame = 1000 }, new DisplayInfo(), Epoch);
        var historic = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "sha-history",
            Identity = identity.Clone(), SaveTick = 50, ObservedUtc = Epoch,
            EvidenceByAcquisition = new Dictionary<string, MetricEvidence>
            { ["factions"] = MetricEvidence.Experimental },
            Metrics = FactionStateSnapshot.Build(new[]
            { new FactionStateFact("old", true, true, false, 20) }, partial: true),
        };
        ProfileMutations.UpsertMetricObservation(profile, historic);

        for (var i = 1; i <= 3; i++)
        {
            var snapshot = new MetricObservation
            {
                Source = MetricObservationSource.SaveSnapshot, Key = "faction-snapshot-" + i,
                Identity = identity.Clone(), SaveTick = i * 100, ObservedUtc = Epoch.AddMinutes(i),
            };
            if (i == 1)
            {
                snapshot.Metrics = FactionStateSnapshot.Build(new[]
                {
                    new FactionStateFact("a", true, true, false, 35),
                    new FactionStateFact("b", true, false, true, -90),
                });
                snapshot.EvidenceByAcquisition = new Dictionary<string, MetricEvidence>
                { ["factions"] = MetricEvidence.Experimental };
            }
            else
            {
                // The faction acquisition unit has been disabled. Other
                // snapshot units still record and trigger compaction.
                snapshot.Metrics[FacilitiesSnapshot.OutdoorTempMax] = MetricEntry.Of(
                    MetricKeys.Revision(FacilitiesSnapshot.OutdoorTempMax),
                    new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = i });
            }
            ProfileMutations.UpsertMetricObservation(profile, snapshot);
        }
        Assert.Contains(profile.MetricObservations, o =>
            o.Source == MetricObservationSource.SaveSnapshot
            && o.Metrics.ContainsKey(FactionStateSnapshot.Relations));
        var colonies = CampaignMatcher.BuildColonies(profile);
        var defs = SnapshotCatalog.Definitions.Where(d => d.Key == FactionStateSnapshot.Relations).ToArray();
        var row = Assert.Single(Assert.Single(MetricCalculator.BuildView(profile, colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()), defs).Colonies).Rows);
        Assert.Equal(2.0, row.Value.Number);
        Assert.Equal(MetricCoverage.Partial, row.Value.Coverage); // retained, but stale
        Assert.Equal(MetricEvidence.Experimental, row.Value.Evidence);
    }

}
