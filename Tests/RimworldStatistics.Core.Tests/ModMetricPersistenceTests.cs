using System.Text.Json;

namespace RimworldStatistics.Core.Tests;

public sealed class ModMetricPersistenceTests
{
    private static ModMetricDescriptor Descriptor(int revision = 1) => new()
    {
        ProviderId = "sample.provider", OwnerPackageId = "example.stats",
        FamilyId = "activity", MetricName = "completed", Revision = revision,
        Source = MetricObservationSource.SaveSnapshot,
        Category = MetricCategory.Work, Unit = MetricUnit.Count, Overall = OverallAggregation.Sum,
    };

    [Fact]
    public void RememberedDefinitionsRenderViaTheExistingCalculatorWithoutAnInstalledProvider()
    {
        var descriptor = Descriptor();
        var profile = new Profile();
        ProfileMutations.RememberModMetrics(profile, new[] { descriptor });
        var id = new ColonyIdentity
        {
            SeedString = "provider", PersistentRandomValue = 7, StartingTile = "4,0",
            GameStartAbsTick = 10, FirstWealthBits = 15,
            ModCampaignId = "campaign-provider", StartingPawnIds = { "Thing_Human1" },
        };
        ProfileMutations.ApplyLiveObservation(profile, id.ModCampaignId!, id,
            new ObservedValues { TicksGame = 100 }, new DisplayInfo(), DateTime.UtcNow);
        var observation = new MetricObservation
        {
            Key = "with-provider", Source = MetricObservationSource.SaveSnapshot,
            Identity = id.Clone(), SaveTick = 100, ObservedUtc = DateTime.UtcNow,
            ProviderCaptures = new Dictionary<string, ModProviderCapture>
            {
                [descriptor.ProviderId] = new("example.stats", "1.0.3", "1.0.0"),
            },
        };
        observation.Metrics[descriptor.StableKey] = MetricEntry.Of(1,
            new MetricValue { IntegerValue = 7, Coverage = MetricCoverage.SnapshotOnly });
        ProfileMutations.UpsertMetricObservation(profile, observation);

        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(profile));
        Assert.Single(restored.ModMetricDescriptors);
        Assert.Equal("1.0.3", restored.MetricObservations.Single().ProviderCaptures![descriptor.ProviderId].OwnerVersion);
        var colonies = CampaignMatcher.BuildColonies(restored);
        var hidden = MetricRegistry.Compose(restored.ModMetricDescriptors,
            new HashSet<string>(StringComparer.Ordinal));
        Assert.DoesNotContain(hidden, d => d.Key == descriptor.StableKey);
        var definitions = MetricRegistry.Compose(restored.ModMetricDescriptors,
            MetricDisplayPolicy.AllDetailsVisibleUnits);
        Assert.Contains(definitions, d => d.Key == descriptor.StableKey);
        var view = MetricCalculator.BuildView(restored, colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()), definitions);
        Assert.Equal(7, Assert.Single(Assert.Single(view.Colonies).Rows,
            r => r.Definition.Key == descriptor.StableKey).Value.Number);
    }

    [Fact]
    public void RemovingProviderNeverDeletesItsLastSnapshotAndLabelsTheValuePartial()
    {
        var descriptor = Descriptor();
        var p = new Profile();
        var id = new ColonyIdentity
        {
            SeedString = "removed-provider", PersistentRandomValue = 4,
            StartingTile = "2,0", GameStartAbsTick = 1, FirstWealthBits = 20,
            ModCampaignId = "provider-removal", StartingPawnIds = { "Thing_Human1" },
        };
        ProfileMutations.ApplyLiveObservation(p, id.ModCampaignId!, id,
            new ObservedValues { TicksGame = 1000 }, new DisplayInfo(), DateTime.UtcNow);
        ProfileMutations.RememberModMetrics(p, new[] { descriptor });
        for (var tick = 1; tick <= 35; tick++)
        {
            var obs = new MetricObservation
            {
                Key = "save-" + tick, Source = MetricObservationSource.SaveSnapshot,
                Identity = id.Clone(), SaveTick = tick * 10,
                ObservedUtc = new DateTime(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc).AddSeconds(tick),
            };
            obs.Metrics[FacilitiesSnapshot.OutdoorTempMax] = MetricEntry.Of(
                MetricKeys.Revision(FacilitiesSnapshot.OutdoorTempMax),
                new MetricValue { RealValue = tick, Coverage = MetricCoverage.SnapshotOnly });
            if (tick <= 3)
            {
                obs.Metrics[descriptor.StableKey] = MetricEntry.Of(1,
                    new MetricValue { IntegerValue = 7, Coverage = MetricCoverage.SnapshotOnly });
                obs.EvidenceByAcquisition = new Dictionary<string, MetricEvidence>
                {
                    [descriptor.AcquisitionId] = MetricEvidence.Experimental,
                };
                obs.ProviderCaptures = new Dictionary<string, ModProviderCapture>
                {
                    [descriptor.ProviderId] = new("example.stats", "1.0.0", "1.0.0"),
                };
            }
            ProfileMutations.UpsertMetricObservation(p, obs);
        }
        Assert.Contains(p.MetricObservations, o => o.Metrics.ContainsKey(descriptor.StableKey));
        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(p));
        Assert.Single(restored.ModMetricDescriptors);
        var captured = Assert.Single(restored.MetricObservations, o => o.SaveTick == 30);
        Assert.Equal(MetricEvidence.Experimental, captured.EvidenceByAcquisition![descriptor.AcquisitionId]);
        Assert.All(restored.MetricObservations.Where(o => o.Metrics.ContainsKey(descriptor.StableKey)),
            o => Assert.Equal(MetricEvidence.Experimental, o.EvidenceByAcquisition![descriptor.AcquisitionId]));
        Assert.False(captured.CompactedEvidenceByMetric?.ContainsKey(descriptor.StableKey) ?? false);
        var colonies = CampaignMatcher.BuildColonies(restored);
        var defs = MetricRegistry.Compose(restored.ModMetricDescriptors,
            new HashSet<string> { MetricRegistry.OptionalModVisibilityId });
        var view = MetricCalculator.BuildView(restored, colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()), defs);
        var row = Assert.Single(Assert.Single(view.Colonies).Rows, r => r.Definition.Key == descriptor.StableKey);
        Assert.Equal(descriptor.AcquisitionId, row.Definition.Acquisition);
        Assert.Equal(7, row.Value.Number);
        Assert.Equal(MetricCoverage.Partial, row.Value.Coverage);
        Assert.Equal(30, row.Value.ThroughTick);
        Assert.Equal(MetricEvidence.Experimental, row.Value.Evidence);
    }

    [Fact]
    public void RemovedEventProviderRetainsCompressedAncestorCountsWithoutDoubleAdding()
    {
        var descriptor = Descriptor() with
        {
            FamilyId = "requests", MetricName = "started",
            Source = MetricObservationSource.Checkpoint,
        };
        var profile = new Profile();
        var id = new ColonyIdentity
        {
            SeedString = "provider-ancestors", PersistentRandomValue = 3,
            StartingTile = "3,0", GameStartAbsTick = 1, FirstWealthBits = 20,
            ModCampaignId = "provider-event", StartingPawnIds = { "Thing_Human1" },
        };
        ProfileMutations.ApplyLiveObservation(profile, id.ModCampaignId!, id,
            new ObservedValues { TicksGame = 300 }, new DisplayInfo(), DateTime.UtcNow);
        ProfileMutations.RememberModMetrics(profile, new[] { descriptor });
        var old = new MetricObservation
        {
            Key = "parent", PayloadHash = "parent-content", ParentKey = null,
            Identity = id.Clone(), Source = MetricObservationSource.Checkpoint, SaveTick = 100,
            EvidenceByAcquisition = new Dictionary<string, MetricEvidence>
            {
                [descriptor.AcquisitionId] = MetricEvidence.Experimental,
            },
        };
        old.Metrics[descriptor.StableKey] = MetricEntry.Of(1,
            new MetricValue { IntegerValue = 3, Coverage = MetricCoverage.Complete, SinceTick = 0, ThroughTick = 100 });
        ProfileMutations.UpsertMetricObservation(profile, old);
        var afterRemoval = new MetricObservation
        {
            Key = "child", PayloadHash = "child-content", ParentKey = "parent",
            Identity = id.Clone(), Source = MetricObservationSource.Checkpoint, SaveTick = 200,
        };
        ProfileMutations.UpsertMetricObservation(profile, afterRemoval);

        var restored = ProfileJson.Deserialize(ProfileJson.Serialize(profile));
        Assert.True(Assert.Single(restored.MetricObservations, o => o.Key == "parent").Compressed);
        var colonies = CampaignMatcher.BuildColonies(restored);
        var defs = MetricRegistry.Compose(restored.ModMetricDescriptors,
            new HashSet<string> { MetricRegistry.OptionalModVisibilityId });
        var view = MetricCalculator.BuildView(restored, colonies,
            StatsCalculator.Calculate(colonies, new StatsOptions()), defs);
        var row = Assert.Single(Assert.Single(view.Colonies).Rows,
            r => r.Definition.Key == descriptor.StableKey);
        Assert.Equal(3, row.Value.Number);
        Assert.Equal(MetricCoverage.Partial, row.Value.Coverage);
        Assert.Equal(MetricEvidence.Experimental, row.Value.Evidence);
        Assert.Equal(100, row.Value.ThroughTick);
    }

    [Fact]
    public void UnknownOrUnregisteredProviderDataIsNeverDeletedAndWrongRevisionIsNotMisinterpreted()
    {
        var profile = new Profile();
        var desc = Descriptor(revision: 2);
        ProfileMutations.RememberModMetrics(profile, new[] { desc });
        var observation = new MetricObservation
        {
            Key = "old", Source = MetricObservationSource.SaveSnapshot,
            Metrics = new Dictionary<string, MetricEntry>
            {
                [desc.StableKey] = MetricEntry.Of(1, new MetricValue
                {
                    IntegerValue = 33, Coverage = MetricCoverage.SnapshotOnly,
                }),
                ["mod.future.unknown.metric"] = MetricEntry.Of(9, new MetricValue { IntegerValue = 12 }),
            },
        };
        profile.MetricObservations.Add(observation);
        var json = ProfileJson.Serialize(profile);
        var restored = ProfileJson.Deserialize(json);
        Assert.Equal(33, restored.MetricObservations.Single().Metrics[desc.StableKey].Values[""].IntegerValue);
        Assert.Contains("mod.future.unknown.metric", restored.MetricObservations.Single().Metrics);
        var defined = Assert.Single(MetricRegistry.Compose(restored.ModMetricDescriptors,
            new HashSet<string> { MetricRegistry.OptionalModVisibilityId }), d => d.Key == desc.StableKey);
        Assert.Equal(2, defined.Revision);
    }

    [Fact]
    public void ProviderCaptureCombine_KeepsUnknownMetadataWhenVersionsAreMixed()
    {
        using var document = JsonDocument.Parse("{\"earlier\":1,\"later\":2}");
        var previous = new ModProviderCapture("example.stats", "1.0.0", "1.0.0")
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureEarlier"] = document.RootElement.GetProperty("earlier").Clone(),
            },
        };
        var incoming = new ModProviderCapture("example.stats", "1.1.0", "1.1.0")
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureLater"] = document.RootElement.GetProperty("later").Clone(),
            },
        };

        var combined = ModProviderCapture.Combine(previous, incoming);
        Assert.Equal("mixed", combined.ProviderVersion);
        Assert.Equal(new[] { "1.0.0", "1.1.0" }, combined.SeenOwnerVersions);
        Assert.Equal(1, combined.ExtensionData!["futureEarlier"].GetInt32());
        Assert.Equal(2, combined.ExtensionData["futureLater"].GetInt32());
    }

    [Fact]
    public void ProviderCaptureCombine_KeepsNewMetadataWhenVersionIsUnchanged()
    {
        using var document = JsonDocument.Parse("true");
        var previous = new ModProviderCapture("example.stats", "1.0.0", "1.0.0");
        var incoming = previous with
        {
            ExtensionData = new Dictionary<string, JsonElement>
            {
                ["futureFlag"] = document.RootElement.Clone(),
            },
        };
        var combined = ModProviderCapture.Combine(previous, incoming);
        Assert.True(combined.ExtensionData!["futureFlag"].GetBoolean());
        Assert.Equal("1.0.0", combined.OwnerVersion);
        Assert.Equal("1.0.0", combined.ProviderVersion);
    }

    [Fact]
    public void DescriptorAndProviderMetadataKeepUnknownFutureJsonFields()
    {
        var p = new Profile();
        ProfileMutations.RememberModMetrics(p, new[] { Descriptor() });
        var json = ProfileJson.Serialize(p)
            .Replace("\"metricName\": \"completed\"", "\"metricName\": \"completed\", \"futureProviderField\": 42");
        var restored = ProfileJson.Deserialize(json);
        Assert.Contains("\"futureProviderField\": 42", ProfileJson.Serialize(restored));
    }
}
