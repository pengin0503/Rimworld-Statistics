using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RimworldStatistics.Api;

namespace RimworldStatistics.Core.Tests;

public sealed class AuditCoreRegressionTests
{
    private static readonly DateTime UtcNow = new(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyStats MakeColonyStats(string key, string faction = "Colony", bool hasObservedPoint = true, int maxTicks = 60_000) => new()
    {
        Key = key,
        FactionName = faction,
        WorldName = "Terra",
        Scenario = "Crashlanded",
        Storyteller = "Cassandra",
        Difficulty = "Rough",
        HasObservedPoint = hasObservedPoint,
        MaxTicks = maxTicks,
        GameVersions = ["1.7.5011 rev24"],
        EndingCounts = new Dictionary<EndingType, int>(),
    };

    private static ColonyIdentity MakeIdentity(string campaignId) => new()
    {
        ModCampaignId = campaignId,
        SeedString = "seed-" + campaignId,
        PersistentRandomValue = 42,
        StartingTile = "10,0",
        GameStartAbsTick = 0,
        FirstWealthBits = BitConverter.SingleToUInt32Bits(10_000f),
    };

    [Fact]
    public void CheckpointArchive_ParentWithValidAndConflictedChild_DoesNotLoopAcrossReopens()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "rws-audit-archive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try
        {
            var archive = new CountingArchive();
            const string campaign = "camp-loop-1";
            var id = MakeIdentity(campaign);
            var builtIn = MetricRegistry.ImplementedCatalog
                .First(d => d.Source == MetricObservationSource.Checkpoint && d.Selection == ColonySelection.Representative);

            MetricObservation MakeCheckpoint(string key, string? parent, int tick, int val, string hash) => new()
            {
                Key = key,
                ParentKey = parent,
                SaveTick = tick,
                PayloadHash = hash,
                Source = MetricObservationSource.Checkpoint,
                Identity = id,
                Metrics =
                {
                    [builtIn.StoredKey] = MetricEntry.Of(builtIn.Revision, new MetricValue
                    {
                        IntegerValue = val,
                        Coverage = MetricCoverage.Complete,
                        SinceTick = 0,
                        ThroughTick = tick,
                    }),
                },
            };

            var session = new ProfileSession(new ProfileStore(tempPath), () => UtcNow, archive);
            session.Mutate(p =>
            {
                ProfileMutations.ApplyLiveObservation(p, campaign, id,
                    new ObservedValues { TicksGame = 1000 }, new DisplayInfo { FactionName = "Loopers" }, UtcNow);
                ProfileMutations.UpsertMetricObservation(p, MakeCheckpoint("P", null, 100, 10, "hash-p"));
                ProfileMutations.UpsertMetricObservation(p, MakeCheckpoint("C1", "P", 200, 20, "hash-c1"));
                ProfileMutations.UpsertMetricObservation(p, MakeCheckpoint("C2", "P", 300, 30, "hash-c2-a"));
            }, SaveMode.None);
            session.Flush();
            Assert.Equal(1, archive.AppendCalls);

            // C2 conflicts while C1 remains valid; P is restored once and never re-archived or re-scanned on reopen.
            session.Mutate(p =>
            {
                ProfileMutations.UpsertMetricObservation(p, MakeCheckpoint("C2", "P", 300, 99, "hash-c2-b"));
            }, SaveMode.None);
            session.Flush();

            var appendsAfterConflict = archive.AppendCalls;
            var readsAfterConflict = archive.ReadCalls;

            for (var cycle = 0; cycle < 3; cycle++)
            {
                var reopened = new ProfileSession(new ProfileStore(tempPath), () => UtcNow, archive);
                reopened.Mutate(_ => { }, SaveMode.None);
                reopened.Flush();
            }

            Assert.Equal(appendsAfterConflict, archive.AppendCalls);
            Assert.Equal(readsAfterConflict, archive.ReadCalls);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void ImportProblems_ClassifiesFormatAndOverflowExceptionsAsDamaged()
    {
        var formatObs = new SaveObservation { Status = ObservationStatus.Error, ErrorReason = "FormatException: bad int" };
        var overflowObs = new SaveObservation { Status = ObservationStatus.Error, ErrorReason = "OverflowException: too big" };
        Assert.Equal(ImportProblemKind.Damaged, ImportProblems.Classify(formatObs));
        Assert.Equal(ImportProblemKind.Damaged, ImportProblems.Classify(overflowObs));
    }

    [Fact]
    public void SaveReader_ReadRecorderChartVerified_CatchesOverflowExceptionWithoutThrowing()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "rws-audit-chart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try
        {
            var xml = """
                <?xml version="1.0" encoding="utf-8"?>
                <savegame>
                  <meta><gameVersion>1.7.5011 rev24</gameVersion></meta>
                  <game>
                    <tickManager><ticksGame>99999999999999999999999999999</ticksGame></tickManager>
                    <history>
                      <autoRecorderGroups>
                        <li>
                          <recorders>
                            <li>
                              <def>Wealth_Total</def>
                              <records></records>
                            </li>
                          </recorders>
                        </li>
                      </autoRecorderGroups>
                    </history>
                  </game>
                </savegame>
                """;
            var savePath = Path.Combine(tempPath, "overflow.rws");
            File.WriteAllText(savePath, xml, Encoding.UTF8);
            var info = new FileInfo(savePath);
            var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(savePath)));
            var alias = new MetricFileAlias
            {
                FileName = info.Name,
                FileSize = info.Length,
                LastWriteUtc = info.LastWriteTimeUtc,
            };

            var (matched, points) = SaveReader.ReadRecorderChartVerified(
                savePath, alias, hash, HistoryMetricReader.WealthTotal, runningGain: false);
            Assert.False(matched);
            Assert.Empty(points);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void SaveReader_DuplicateWealthTotalWithoutRecords_InvalidatesDuplicateRecorder()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "rws-audit-wealth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try
        {
            var validRecords = Convert.ToBase64String(BitConverter.GetBytes(1000f).Concat(BitConverter.GetBytes(2000f)).ToArray());
            string MakeXml(bool duplicateWithoutRecords) => $"""
                <?xml version="1.0" encoding="utf-8"?>
                <savegame>
                  <meta><gameVersion>1.7.5011 rev24</gameVersion></meta>
                  <game>
                    <info><realPlayTimeInteracting>10</realPlayTimeInteracting><startingTile>10</startingTile></info>
                    <tickManager><ticksGame>60000</ticksGame></tickManager>
                    <world>
                      <info><seedString>seed</seedString><name>Terra</name></info>
                      <factionManager>
                        <allFactions>
                          <li><def>PlayerColony</def><loadID>1</loadID><name>Alpha</name><isPlayer>True</isPlayer></li>
                        </allFactions>
                      </factionManager>
                    </world>
                    <history>
                      <autoRecorderGroups>
                        <li>
                          <recorders>
                            <li>
                              <def>Wealth_Total</def>
                              <records>{validRecords}</records>
                            </li>
                            {(duplicateWithoutRecords ? "<li><def>Wealth_Total</def></li>" : "")}
                          </recorders>
                        </li>
                      </autoRecorderGroups>
                    </history>
                  </game>
                </savegame>
                """;

            var singlePath = Path.Combine(tempPath, "single-wealth.rws");
            File.WriteAllText(singlePath, MakeXml(duplicateWithoutRecords: false), Encoding.UTF8);
            var singleDetail = new SaveReader().ReadDetailed(singlePath);
            Assert.Equal(ObservationStatus.Ok, singleDetail.Observation.Status);
            Assert.Contains(MetricKeys.WealthTotalMean, singleDetail.Metrics!.Metrics.Keys);

            var dupPath = Path.Combine(tempPath, "dup-wealth.rws");
            File.WriteAllText(dupPath, MakeXml(duplicateWithoutRecords: true), Encoding.UTF8);
            var dupDetail = new SaveReader().ReadDetailed(dupPath);
            Assert.Equal(ObservationStatus.Ok, dupDetail.Observation.Status);
            Assert.DoesNotContain(MetricKeys.WealthTotalMean, dupDetail.Metrics!.Metrics.Keys);

            var dupInfo = new FileInfo(dupPath);
            var dupHash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dupPath)));
            var dupAlias = new MetricFileAlias { FileName = dupInfo.Name, FileSize = dupInfo.Length, LastWriteUtc = dupInfo.LastWriteTimeUtc };
            var (matched, chartPoints) = SaveReader.ReadRecorderChartVerified(
                dupPath, dupAlias, dupHash, HistoryMetricReader.WealthTotal, runningGain: false);
            Assert.True(matched);
            Assert.Empty(chartPoints);
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void ModMetricDescriptorAndExternalSdk_ValidateRequiredDlcAndSelectionContracts()
    {
        var baseDesc = new ModMetricDescriptor
        {
            ProviderId = "example.mod",
            OwnerPackageId = "example.mod",
            FamilyId = "fam",
            MetricName = "metric",
            Revision = 1,
        };

        Assert.True((baseDesc with { RequiredDlc = null }).IsValid());
        Assert.True((baseDesc with { RequiredDlc = "Ludeon.RimWorld.Ideology" }).IsValid());
        Assert.False((baseDesc with { RequiredDlc = "" }).IsValid());
        Assert.False((baseDesc with { RequiredDlc = "   " }).IsValid());

        var sdk = new ExternalSdkMetricRegistry();
        var baseApi = new ApiMetricDefinition
        {
            ProviderId = "example.sdk",
            ProviderVersion = "1.0.0",
            OwnerPackageId = "example.sdk",
            FamilyId = "fam",
            MetricName = "m1",
            Kind = ApiMetricKind.Event,
            EventMode = ApiEventMode.Count,
            Category = nameof(MetricCategory.Colony),
            Unit = nameof(MetricUnit.Count),
            Overall = nameof(OverallAggregation.Sum),
            Selection = nameof(ColonySelection.ObservationMax),
        };
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, sdk.Register(baseApi, snapshot: null));

        var breakdownSelectApi = baseApi with
        {
            MetricName = "m2",
            Kind = ApiMetricKind.SaveSnapshot,
            Selection = nameof(ColonySelection.ObservationMaxByBreakdown),
        };
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, sdk.Register(breakdownSelectApi, snapshot: () => 1.0));

        var whitespaceDlcApi = baseApi with
        {
            MetricName = "m3",
            Selection = nameof(ColonySelection.Representative),
            RequiredDlc = " ",
        };
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, sdk.Register(whitespaceDlcApi, snapshot: null));
    }

    [Fact]
    public void MetricRankings_InputPeak_ExcludesNotApplicableFromTargetsAndPreservesCompleteCoverage()
    {
        var colonies = new[]
        {
            MakeColonyStats("c1", "One"),
            MakeColonyStats("c2", "Two"),
        };
        var salesPeakDef = MetricRegistry.Find(TradeFamily.SalesPeak)!;
        var rows = new Dictionary<string, IReadOnlyList<NumericRow>>(StringComparer.Ordinal)
        {
            ["c1"] =
            [
                new NumericRow
                {
                    Definition = salesPeakDef,
                    Value = new NumericValue { Number = 2500, Coverage = MetricCoverage.Complete, Evidence = MetricEvidence.Verified },
                },
            ],
            ["c2"] =
            [
                new NumericRow
                {
                    Definition = salesPeakDef,
                    Value = new NumericValue { Coverage = MetricCoverage.NotApplicable, Evidence = MetricEvidence.Verified },
                },
            ],
        };

        var built = MetricRankings.Build(colonies, rows).ToDictionary(r => r.Definition.Key, StringComparer.Ordinal);
        var peakSale = built[MetricRankings.LargestSale];
        Assert.Equal(MetricCoverage.Complete, peakSale.Value.Coverage);
        Assert.Equal(1, peakSale.Targets);
        Assert.Equal(1, peakSale.Valid);
        Assert.Equal(2500, peakSale.Value.Number);
    }

    [Fact]
    public void EndingOnlyColony_BasicMetricsAndScatterPlot_RetainKnownEndingsSavesAndLiveDeparted()
    {
        var liveEnding = new EndingEvent
        {
            CampaignId = "camp-end",
            Type = EndingType.Ship,
            Ordinal = 1,
            Departed = 4,
            Source = EndingSource.Live,
            TicksGame = 120_000,
            RealTimeUtc = UtcNow,
        };
        var endingOnly = new ColonyStats
        {
            Key = "end-only",
            FactionName = "Travelers",
            HasObservedPoint = false,
            SaveCount = 0,
            Departed = 4,
            CountedEndings = [liveEnding],
            GameVersions = [],
            EndingCounts = new Dictionary<EndingType, int> { [EndingType.Ship] = 1 },
        };

        Assert.Equal(1, BasicMetrics.Value(endingOnly, BasicMetrics.Endings));
        Assert.Equal(0, BasicMetrics.Value(endingOnly, BasicMetrics.Saves));
        Assert.Equal(4, BasicMetrics.Value(endingOnly, BasicMetrics.Departed));
        Assert.Null(BasicMetrics.Value(endingOnly, BasicMetrics.Days));

        var view = new StatsView
        {
            Colonies = [new Colony { Key = "end-only", Endings = { liveEnding } }],
            Stats = new StatsResult
            {
                Colonies = [endingOnly],
                ColonyCount = 1,
                TotalEndings = 1,
                TotalDeparted = 4,
                EndingsByType = new Dictionary<EndingType, int> { [EndingType.Ship] = 1 },
            },
            Numeric = NumericView.Empty,
        };

        var scatterKnown = MetricScatterPlot.Build(view, BasicMetrics.Endings, BasicMetrics.Departed)!;
        var point = Assert.Single(scatterKnown.Points);
        Assert.Equal((1d, 4d), (point.X.Number, point.Y.Number));
        Assert.Null(point.X.ThroughTick);

        var scatterUnknown = MetricScatterPlot.Build(view, BasicMetrics.Days, BasicMetrics.Endings)!;
        Assert.Empty(scatterUnknown.Points);
        Assert.Contains(scatterUnknown.Exclusions,
            e => e.ColonyKey == "end-only" && e.Reason == ScatterExclusionReason.Missing);
    }

    [Fact]
    public void ColonySummaryReport_RanksMinMetricsAscendingAndClassifiesMoodMinAsPeakOrExtreme()
    {
        var moodMinDef = MetricRegistry.Find(MetricKeys.MoodMin)!;
        var first100KDef = MetricRegistry.Find(MetricKeys.WealthTotalFirst100K)!;

        Assert.Equal(ColonyReportValueKind.PeakOrExtreme, ColonySummaryReport.ClassifyValueKind(moodMinDef));
        Assert.False(LeaderboardBuilder.DefaultDescending(moodMinDef));
        Assert.False(LeaderboardBuilder.DefaultDescending(first100KDef));

        var cFast = MakeColonyStats("fast", "Fast", maxTicks: 120_000);
        var cSlow = MakeColonyStats("slow", "Slow", maxTicks: 600_000);
        var view = new StatsView
        {
            Colonies = [new Colony { Key = "fast" }, new Colony { Key = "slow" }],
            Stats = new StatsResult
            {
                Colonies = [cFast, cSlow],
                ColonyCount = 2,
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = new NumericView
            {
                Overall = [],
                Colonies =
                [
                    new NumericColony
                    {
                        Key = "fast",
                        Rows =
                        [
                            new NumericRow { Definition = first100KDef, Value = new NumericValue { Number = 15, Coverage = MetricCoverage.Complete } },
                            new NumericRow { Definition = moodMinDef, Value = new NumericValue { Number = 12, Coverage = MetricCoverage.SnapshotOnly } },
                        ],
                    },
                    new NumericColony
                    {
                        Key = "slow",
                        Rows =
                        [
                            new NumericRow { Definition = first100KDef, Value = new NumericValue { Number = 80, Coverage = MetricCoverage.Complete } },
                            new NumericRow { Definition = moodMinDef, Value = new NumericValue { Number = 45, Coverage = MetricCoverage.SnapshotOnly } },
                        ],
                    },
                ],
            },
        };

        var fastReport = ColonySummaryReport.Build(view, "fast")!;
        Assert.Equal(1, fastReport.KeyRecords.Single(i => i.MetricKey == MetricKeys.MoodMin).Rank);
        Assert.Contains(fastReport.TopRankedHighlights, i => i.MetricKey == MetricKeys.WealthTotalFirst100K && i.Rank == 1);
    }

    [Fact]
    public void EndingTraces_RepresentativeAndUnexplained_PreferLiveAndSuppressExplainedTraces()
    {
        var seeded = new EndingEvent { Type = EndingType.VoidEmbraced, Ordinal = 1, Departed = 0, Source = EndingSource.SeededTrace, TicksGame = 0 };
        var live = new EndingEvent { Type = EndingType.VoidEmbraced, Ordinal = 1, Departed = 0, Source = EndingSource.Live, TicksGame = 180_000 };
        Assert.Same(live, EndingTraces.Representative([seeded, live]));

        var colony = new Colony
        {
            Key = "c-end",
            Endings =
            {
                new EndingEvent { Type = EndingType.Archonexus, Ordinal = 1, DevMode = true, Source = EndingSource.Live },
            },
            RecoveredEndings =
            {
                new LineageEntry { Type = EndingType.Ship },
            },
            Traces = { EndingType.Archonexus, EndingType.AmbiguousLaunch, EndingType.VoidDisrupted },
        };

        var unexplained = EndingTraces.Unexplained(colony);
        Assert.Equal([EndingType.VoidDisrupted], unexplained);
    }

    [Fact]
    public void InGameColonyAccess_IgnoresDamagedObservationsAndEpochPlaceholderUtc()
    {
        var okObs = new SaveObservation
        {
            FileName = "good.rws",
            Status = ObservationStatus.Ok,
            LastWriteUtc = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            Identity = new ColonyIdentity { ModCampaignId = "camp-ig" },
            Values = new ObservedValues { TicksGame = 180_000 },
        };
        var damagedNewer = new SaveObservation
        {
            FileName = "corrupt.rws",
            Status = ObservationStatus.Error,
            ErrorReason = "XmlException: truncated",
            LastWriteUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Identity = new ColonyIdentity { ModCampaignId = "camp-ig" },
            Values = new ObservedValues { TicksGame = 0 },
        };
        var epochLive = new LiveCampaign
        {
            CampaignId = "camp-ig",
            LastSavedUtc = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Latest = new ObservedValues { TicksGame = 60_000 },
            Max = new ObservedValues { TicksGame = 60_000 },
        };
        var colony = new Colony
        {
            Key = "c-ig",
            Observations = { okObs, damagedNewer },
            LiveCampaigns = { epochLive },
        };
        var view = new StatsView
        {
            Colonies = [colony],
            Stats = new StatsResult
            {
                Colonies = [MakeColonyStats("c-ig", maxTicks: 180_000)],
                EndingsByType = new Dictionary<EndingType, int>(),
            },
            Numeric = NumericView.Empty,
        };

        var res = InGameColonyAccess.Resolve(view, "camp-ig");
        Assert.True(res.Found);
        Assert.Equal(180_000, res.RecordedPoint.LastRecordedTicks);
        Assert.Equal(okObs.LastWriteUtc, res.RecordedPoint.LastRecordedUtc);
    }

    [Fact]
    public void ExcludedColony_RetainsSingleColonyChartHistoryAndExplicitBarInclusion()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "rws-audit-excl-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        try
        {
            var def = MetricRegistry.ImplementedCatalog
                .First(d => d.Source == MetricObservationSource.Checkpoint && d.Selection == ColonySelection.Representative);
            const string campaign = "camp-excl";
            var id = MakeIdentity(campaign);
            var session = new ProfileSession(new ProfileStore(tempPath), () => UtcNow);
            session.Mutate(p =>
            {
                ProfileMutations.ApplyLiveObservation(p, campaign, id,
                    new ObservedValues { TicksGame = 60_000 }, new DisplayInfo { FactionName = "Hidden" }, UtcNow);
                ProfileMutations.UpsertMetricObservation(p, new MetricObservation
                {
                    Key = "cp-1",
                    SaveTick = 60_000,
                    PayloadHash = "h1",
                    Source = MetricObservationSource.Checkpoint,
                    Identity = id,
                    Metrics =
                    {
                        [def.StoredKey] = MetricEntry.Of(def.Revision, new MetricValue
                        {
                            IntegerValue = 5,
                            Coverage = MetricCoverage.Complete,
                            SinceTick = 0,
                            ThroughTick = 60_000,
                        }),
                    },
                });
            }, SaveMode.None);

            var initialView = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
            var key = Assert.Single(initialView.Colonies).Key;
            session.Mutate(p => ProfileMutations.SetExcluded(p, [key], true), SaveMode.None);

            var view = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
            var history = session.ReadChartHistory(view, key, def, tempPath);
            Assert.Single(history.Points);

            Assert.Empty(MetricChartSeries.ByColony(view.Numeric, def));
            Assert.Single(MetricChartSeries.ByColony(view.Numeric, def, includeColonyKey: key));
            Assert.Empty(MetricChartSeries.BasicByColony(view.Stats, BasicMetrics.Days));
            Assert.Single(MetricChartSeries.BasicByColony(view.Stats, BasicMetrics.Days, includeColonyKey: key));
        }
        finally
        {
            if (Directory.Exists(tempPath))
                Directory.Delete(tempPath, recursive: true);
        }
    }

    [Fact]
    public void LowSeverityCoreGuards_FishingOverflowOptionalRevisionAndSnapshotChartEnumCheck()
    {
        // 1. FishingFamily overflow propagates Partial to AcceptedUnitsByDef
        var fishingState = new FamilyState { SinceTick = 0, ThroughTick = 200, Continuous = true };
        fishingState.Count("placed_units", long.MaxValue - 2);
        FishingFamily.RecordAccepted(fishingState, "job-1", "base", new Dictionary<string, int> { ["Fish_Salmon"] = 10 }, 200);
        var fishingOut = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        FishingFamily.Family.Build(new FamilyOutputContext(fishingState, 200, fishingOut));
        Assert.Equal(MetricCoverage.Partial, fishingOut[FishingFamily.AcceptedUnitsByDef].Values[MetricEntry.Scalar].Coverage);
        Assert.All(fishingOut[FishingFamily.AcceptedUnitsByDef].Values.Values, v => Assert.Equal(MetricCoverage.Partial, v.Coverage));

        // 2. OptionalTransitionRecording preserves built-in revision for non-mod keys while using State.Revision for mod keys
        var craftedDef = MetricRegistry.Find(WorkFamily.CraftedUnits)!;
        var coreState = new FamilyState { Revision = 7, SinceTick = 0, ThroughTick = 100, Continuous = true };
        OptionalTransitionRecording.RecordCount(coreState, "c", 3);
        var coreInto = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(coreState, 100, coreInto), "c", WorkFamily.CraftedUnits);
        Assert.Equal(craftedDef.Revision, coreInto[WorkFamily.CraftedUnits].Revision);

        var modState = new FamilyState { Revision = 7, SinceTick = 0, ThroughTick = 100, Continuous = true };
        OptionalTransitionRecording.RecordCount(modState, "c", 3);
        var modInto = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        OptionalTransitionRecording.PutCount(new FamilyOutputContext(modState, 100, modInto), "c", "mod.pkg.fam.key");
        Assert.Equal(7, modInto["mod.pkg.fam.key"].Revision);

        // 3. SnapshotChartKey.Fits rejects out-of-range MetricCoverage enum values
        var badKey = new SnapshotChartKey
        {
            Values = [1.0],
            Coverage = [(MetricCoverage)999],
        };
        Assert.False(badKey.Fits(rows: 1));

        // 4. CrossColonyCatalog.UsesThingDefBreakdown covers direct ThingDef splits and excludes categorical splits
        Assert.True(CrossColonyCatalog.UsesThingDefBreakdown(MetricRegistry.Find(WorkFamily.HarvestByCrop)!));
        Assert.True(CrossColonyCatalog.UsesThingDefBreakdown(MetricRegistry.Find(HealthDeathFamily.DeathsBySpecies)!));
        Assert.False(CrossColonyCatalog.UsesThingDefBreakdown(MetricRegistry.Find(MentalFamily.BreaksByDef)!));
    }

    private sealed class CountingArchive : ICheckpointArchive
    {
        private readonly List<(string Campaign, MetricObservation Observation)> _items = [];
        public int AppendCalls { get; private set; }
        public int ReadCalls { get; private set; }

        public void Append(string campaignId, IReadOnlyList<string> jsonLines)
        {
            AppendCalls++;
            foreach (var line in jsonLines)
            {
                var clone = JsonSerializer.Deserialize<MetricObservation>(line, ProfileJson.CompactOptions)!;
                _items.Add((campaignId, clone));
            }
        }

        public IReadOnlyList<MetricObservation> Read(string campaignId, string checkpointKey)
        {
            ReadCalls++;
            return _items
                .Where(x => x.Campaign == campaignId && x.Observation.Key == checkpointKey)
                .Select(x => x.Observation)
                .ToArray();
        }
    }
}
