using System.Text.Json;
using RimworldStatistics.Api;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class Issue357RegressionTests
{
    private const string Campaign = "issue-357";
    private static readonly DateTime Now = new(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyIdentity Identity(string id = Campaign, string seed = "seed-357") => new()
    {
        ModCampaignId = id,
        SeedString = seed,
        PersistentRandomValue = 1,
        StartingTile = "1,0",
        GameStartAbsTick = 0,
        FirstWealthBits = 1,
    };

    private static MetricObservation Checkpoint(string key, string? parent, int tick, string payloadHash = "hash") => new()
    {
        Source = MetricObservationSource.Checkpoint,
        Key = key,
        ParentKey = parent,
        PayloadHash = key + "-" + payloadHash,
        SaveTick = tick,
        Identity = Identity(),
    };

    private static MetricEntry Count(long value, int revision = 1) => MetricEntry.Of(revision,
        new MetricValue { IntegerValue = value, Coverage = MetricCoverage.Complete, SinceTick = 0, ThroughTick = 10 });

    [Fact]
    public void ArchivePlanner_SkipsParentWithConflictedChild_PreventingSessionRestoreLoop()
    {
        using var dir = new TempDir();
        var archive = new FileCheckpointArchive(dir.File("archive"));
        var builtIn = MetricRegistry.Compose(null, MetricDisplayPolicy.AllDetailsVisibleUnits)
            .First(d => d.Source == MetricObservationSource.Checkpoint && d.Selection == ColonySelection.Representative
                && !SnapshotSelectionPolicy.UsesLastCapture(d) && d.RequiredDlc is null && d.Overall == OverallAggregation.Sum);

        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now, archive);
        session.Mutate(p =>
        {
            ProfileMutations.ApplyLiveObservation(p, Campaign, Identity(), new ObservedValues { TicksGame = 100 }, new DisplayInfo(), Now);
            var parent = Checkpoint("P", null, 10);
            parent.Metrics[builtIn.StoredKey] = Count(1, builtIn.Revision);
            var validChild = Checkpoint("C1", "P", 20);
            validChild.Metrics[builtIn.StoredKey] = Count(2, builtIn.Revision);
            var conflictedChild = Checkpoint("C2", "P", 30);
            conflictedChild.Metrics[builtIn.StoredKey] = Count(3, builtIn.Revision);

            ProfileMutations.UpsertMetricObservation(p, parent);
            ProfileMutations.UpsertMetricObservation(p, validChild);
            ProfileMutations.UpsertMetricObservation(p, conflictedChild);

            var conflict = Checkpoint("C2", "P", 30, payloadHash: "other");
            conflict.Metrics[builtIn.StoredKey] = Count(99, builtIn.Revision);
            ProfileMutations.UpsertMetricObservation(p, conflict);
        }, SaveMode.None);
        session.Flush();

        Assert.Empty(Directory.Exists(dir.File("archive")) ? Directory.GetFiles(dir.File("archive")) : Array.Empty<string>());
        Assert.False(session.Read(p => p.MetricObservations.Single(o => o.Key == "P").ArchivedMetrics));

        var reloaded = new ProfileSession(new ProfileStore(dir.Path), () => Now, archive);
        reloaded.Mutate(_ => { }, SaveMode.None);
        reloaded.Flush();
        Assert.Empty(Directory.Exists(dir.File("archive")) ? Directory.GetFiles(dir.File("archive")) : Array.Empty<string>());
    }

    [Fact]
    public void BranchLastCaptureMismatch_WhenKeptInlineOnParent_DoesNotRestoreEverySession()
    {
        using var dir = new TempDir();
        var archiveDir = dir.File("archive");
        var archive = new FileCheckpointArchive(archiveDir);
        var builtIn = MetricRegistry.Compose(null, MetricDisplayPolicy.AllDetailsVisibleUnits)
            .First(d => d.Source == MetricObservationSource.Checkpoint && d.Selection == ColonySelection.Representative
                && !SnapshotSelectionPolicy.UsesLastCapture(d) && d.RequiredDlc is null && d.Overall == OverallAggregation.Sum);
        var modDesc = new ModMetricDescriptor
        {
            OwnerPackageId = "test.mod",
            ProviderId = "test.mod",
            FamilyId = "events",
            MetricName = "count",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Selection = ColonySelection.Representative,
            Overall = OverallAggregation.Sum,
            LabelKey = "L",
            DescriptionKey = "D",
        };

        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now, archive);
        session.Mutate(p =>
        {
            ProfileMutations.ApplyLiveObservation(p, Campaign, Identity(), new ObservedValues { TicksGame = 100 }, new DisplayInfo(), Now);
            ProfileMutations.RememberModMetrics(p, [modDesc]);

            var parent = Checkpoint("P", null, 10);
            parent.Metrics[builtIn.StoredKey] = Count(1, builtIn.Revision);
            parent.Metrics[modDesc.StableKey] = Count(4, 1);

            var branchWithMod = Checkpoint("C1", "P", 20);
            branchWithMod.Metrics[builtIn.StoredKey] = Count(2, builtIn.Revision);
            branchWithMod.Metrics[modDesc.StableKey] = Count(5, 1);

            var branchWithoutMod = Checkpoint("C2", "P", 25);
            branchWithoutMod.Metrics[builtIn.StoredKey] = Count(3, builtIn.Revision);

            ProfileMutations.UpsertMetricObservation(p, parent);
            ProfileMutations.UpsertMetricObservation(p, branchWithMod);
            ProfileMutations.UpsertMetricObservation(p, branchWithoutMod);
        }, SaveMode.None);
        session.Flush();

        Assert.Single(archive.Read(Campaign, "P"));
        Assert.True(session.Read(p => p.MetricObservations.Single(o => o.Key == "P").ArchivedMetrics));
        Assert.True(session.Read(p => p.MetricObservations.Single(o => o.Key == "P").Metrics.ContainsKey(modDesc.StableKey)));

        var reloaded = new ProfileSession(new ProfileStore(dir.Path), () => Now, archive);
        Assert.True(reloaded.Read(p => p.MetricObservations.Single(o => o.Key == "P").ArchivedMetrics));
        reloaded.Mutate(p =>
        {
            var thirdBranch = Checkpoint("C3", "P", 30);
            thirdBranch.Metrics[builtIn.StoredKey] = Count(4, builtIn.Revision);
            ProfileMutations.UpsertMetricObservation(p, thirdBranch);
        }, SaveMode.None);
        reloaded.Flush();

        Assert.Single(archive.Read(Campaign, "P"));
    }

    [Fact]
    public void OverflowAndFormatFailures_AreClassifiedAsDamagedAndCaughtInChartVerification()
    {
        Assert.Equal(ImportProblemKind.Damaged, ImportProblems.Classify(new SaveObservation
        {
            Status = ObservationStatus.Error,
            ErrorReason = "OverflowException: Value was either too large or too small for an Int32.",
        }));
        Assert.Equal(ImportProblemKind.Damaged, ImportProblems.Classify(new SaveObservation
        {
            Status = ObservationStatus.Error,
            ErrorReason = "FormatException: The input string 'abc' was not in a correct format.",
        }));

        using var dir = new TempDir();
        var path = dir.File("overflow.rws");
        var xml = System.Text.Encoding.UTF8.GetString(new SaveXmlBuilder().Minimal().Build())
            .Replace("<ticksGame>60000</ticksGame>", "<ticksGame>99999999999999999999</ticksGame>");
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        File.WriteAllBytes(path, bytes);
        var info = new FileInfo(path);
        var alias = new MetricFileAlias { FileName = info.Name, FileSize = info.Length, LastWriteUtc = info.LastWriteTimeUtc };
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

        var (verified, points) = SaveReader.ReadRecorderChartVerified(path, alias, hash, HistoryMetricReader.WealthTotal, runningGain: false);
        Assert.False(verified);
        Assert.Empty(points);
    }

    [Fact]
    public void WealthTotalAfterDef_BoundsWhitespaceAllocationsAndRejectsDuplicateEmptyDef()
    {
        var payload = Convert.ToBase64String(SaveXmlBuilder.FloatBytes(100f, 150f, 120f));
        var spaced = payload[..4] + "\n" + new string(' ', 1024 * 1024) + payload[4..];
        var builder = new SaveXmlBuilder().Meta("1.7.4985 rev1269", 3).Info(realPlay: 10f, tile: "100,0", pawns: "Thing_Human1")
            .Tick(ticksGame: 60000, start: 0).World(seed: "seed", prv: 1).WealthTotalRaw("records", spaced);
        var bytes = builder.Build();
        var reader = new SaveReader();
        using var warmup = new MemoryStream(bytes);
        _ = reader.ReadDetailed(warmup, "w.rws", bytes.Length, DateTime.UnixEpoch);

        using var measured = new MemoryStream(bytes);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var read = reader.ReadDetailed(measured, "w.rws", bytes.Length, DateTime.UnixEpoch);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
        Assert.Equal(150d, read.Observation.Values.WealthMax);
        Assert.True(allocated < 256 * 1024, $"Allocated {allocated} bytes");

        var xml = System.Text.Encoding.UTF8.GetString(new SaveXmlBuilder().Minimal().WealthTotal(100f, 150f, 120f).Build());
        xml = xml.Replace("</recorders>", "<li><def>Wealth_Total</def></li></recorders>");
        var dupBytes = System.Text.Encoding.UTF8.GetBytes(xml);
        using var dupStream = new MemoryStream(dupBytes);
        var dupRead = reader.ReadDetailed(dupStream, "dup.rws", dupBytes.Length, DateTime.UnixEpoch);
        Assert.Equal(ObservationStatus.Ok, dupRead.Observation.Status);
        Assert.False(dupRead.Metrics!.Metrics.ContainsKey(MetricKeys.WealthTotalMean));
    }

    [Fact]
    public void ModDescriptorAndExternalSdk_RejectInvalidSelectionsAndBlankRequiredDlc()
    {
        var baseDesc = new ModMetricDescriptor
        {
            ProviderId = "test.provider",
            OwnerPackageId = "test.owner",
            FamilyId = "fam",
            MetricName = "metric",
            Revision = 1,
            Source = MetricObservationSource.SaveSnapshot,
            Overall = OverallAggregation.Max,
        };
        Assert.False((baseDesc with { Selection = ColonySelection.ObservationMaxByBreakdown, HasBreakdown = false }).IsValid());
        Assert.True((baseDesc with { Selection = ColonySelection.ObservationMaxByBreakdown, HasBreakdown = true }).IsValid());
        Assert.False((baseDesc with { RequiredDlc = "" }).IsValid());
        Assert.False((baseDesc with { RequiredDlc = "   " }).IsValid());
        Assert.True((baseDesc with { RequiredDlc = AcquisitionUnits.Biotech }).IsValid());

        var sdk = new ExternalSdkMetricRegistry();
        var eventDef = new ApiMetricDefinition
        {
            ProviderId = "test.sdk",
            OwnerPackageId = "test.owner",
            FamilyId = "events",
            MetricName = "count",
            Kind = ApiMetricKind.Event,
            Selection = "ObservationMax",
        };
        Assert.Equal(ApiRegistrationResult.InvalidDefinition, sdk.Register(eventDef, null));
        Assert.Equal(ApiRegistrationResult.InvalidDefinition,
            sdk.Register(eventDef with { Selection = "Representative", RequiredDlc = "" }, null));
    }

    [Fact]
    public void InputPeak_ExcludesNotApplicableColoniesFromTargets()
    {
        var p = new Profile();
        foreach (var s in new[] { "A", "B" })
        {
            p.SaveObservations.Add(new SaveObservation
            {
                FileName = s + ".rws", FileSize = 1, LastWriteUtc = Now, ReaderVersion = SaveReader.Version,
                Status = ObservationStatus.Ok, Identity = Identity("camp-" + s, s), Values = { TicksGame = 60000 },
            });
        }
        var cpA = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint, Key = "cp-A", Identity = Identity("camp-A", "A"),
            SaveTick = 60000, ObservedUtc = Now,
        };
        cpA.Metrics[TradeFamily.SalesPeak] = MetricEntry.Of(MetricKeys.Revision(TradeFamily.SalesPeak),
            new MetricValue { Coverage = MetricCoverage.Complete, RealValue = 500 });
        var cpB = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint, Key = "cp-B", Identity = Identity("camp-B", "B"),
            SaveTick = 60000, ObservedUtc = Now,
        };
        cpB.Metrics[TradeFamily.SalesPeak] = MetricEntry.Of(MetricKeys.Revision(TradeFamily.SalesPeak),
            new MetricValue { Coverage = MetricCoverage.NotApplicable });
        p.MetricObservations.Add(cpA);
        p.MetricObservations.Add(cpB);

        var colonies = CampaignMatcher.BuildColonies(p);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        var view = MetricCalculator.BuildView(p, colonies, stats, MetricRegistry.Catalog);
        var peak = view.Overall.Single(r => r.Definition.Key == MetricRankings.LargestSale);
        Assert.Equal(500d, peak.Value.Number);
        Assert.Equal(1, peak.Targets);
        Assert.Equal(1, peak.Valid);
        Assert.Equal(MetricCoverage.Complete, peak.Value.Coverage);
    }

    [Fact]
    public void EndingOnlyColony_ExposesEndingsSavesAndLiveDepartedWhileKeepingCountersNull()
    {
        var liveEnding = new EndingEvent
        {
            CampaignId = "c-live", Type = EndingType.Ship, Ordinal = 0,
            Source = EndingSource.Live, Departed = 4, RealTimeUtc = Now,
        };
        var liveOnly = new ColonyStats
        {
            Key = "c-live",
            HasObservedPoint = false,
            Departed = 4,
            SaveCount = 0,
            GameVersions = [],
            EndingCounts = new Dictionary<EndingType, int> { [EndingType.Ship] = 1 },
            CountedEndings = [liveEnding],
        };
        Assert.Equal(1d, BasicMetrics.Value(liveOnly, BasicMetrics.Endings));
        Assert.Equal(0d, BasicMetrics.Value(liveOnly, BasicMetrics.Saves));
        Assert.Equal(4d, BasicMetrics.Value(liveOnly, BasicMetrics.Departed));
        Assert.Null(BasicMetrics.Value(liveOnly, BasicMetrics.Days));
        Assert.Null(BasicMetrics.Value(liveOnly, BasicMetrics.Raids));

        var traceOnly = new ColonyStats
        {
            Key = "c-trace",
            HasObservedPoint = false,
            Departed = 0,
            SaveCount = 0,
            GameVersions = [],
            EndingCounts = new Dictionary<EndingType, int> { [EndingType.Ship] = 1 },
            CountedEndings = [],
        };
        Assert.Equal(1d, BasicMetrics.Value(traceOnly, BasicMetrics.Endings));
        Assert.Equal(0d, BasicMetrics.Value(traceOnly, BasicMetrics.Saves));
        Assert.Null(BasicMetrics.Value(traceOnly, BasicMetrics.Departed));
    }

    [Fact]
    public void ExportEndings_PrefersLiveOnZeroDepartedSuppressesExplainedTraceAndReadsExplainedTraceDate()
    {
        using var root = new TempDir();
        var t1 = new DateTime(2026, 11, 10, 12, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

        var obs = new SaveObservation
        {
            FileName = "stamped.rws", FileSize = 10, LastWriteUtc = t1, ReaderVersion = SaveReader.Version,
            Status = ObservationStatus.Ok, Identity = Identity("c1", "s1"), Values = { TicksGame = 60000 },
            ExplainedTraces = [EndingType.VoidEmbraced],
        };
        var colony = new Colony { Key = "col-1" };
        colony.Observations.Add(obs);
        colony.Traces.Add(EndingType.VoidEmbraced);
        colony.Traces.Add(EndingType.AmbiguousLaunch);
        colony.RecoveredEndings.Add(new LineageEntry(EndingType.RoyalAscent, 0, 50000));

        var seeded = new EndingEvent
        {
            CampaignId = "c1", Type = EndingType.CerebrexDestroyed, Ordinal = 0,
            Source = EndingSource.SeededTrace, Departed = 0, RealTimeUtc = t1,
        };
        var liveZero = new EndingEvent
        {
            CampaignId = "c1", Type = EndingType.CerebrexDestroyed, Ordinal = 0,
            Source = EndingSource.Live, Departed = 0, RealTimeUtc = t2,
        };

        var stats = new ColonyStats
        {
            Key = "col-1",
            HasObservedPoint = true,
            MaxTicks = 60000,
            SaveCount = 1,
            GameVersions = [],
            EndingCounts = new Dictionary<EndingType, int> { [EndingType.CerebrexDestroyed] = 1 },
            CountedEndings = [seeded, liveZero],
        };
        var view = new StatsView
        {
            Colonies = [colony],
            Stats = new StatsResult { ColonyCount = 1, Colonies = [stats], EndingsByType = stats.EndingCounts },
            Numeric = NumericView.Empty,
        };
        var labels = new ExportText(new Dictionary<string, string>(), new Dictionary<string, string>(),
            new Dictionary<NumericRow, string>(), "en", "All", "2.0.0");

        var summary = StatisticsExport.Write(view, labels, _ => UnitRecordingState.NotSelected, root.Path, TestContext.Current.CancellationToken);
        var lines = File.ReadAllLines(Path.Combine(summary.Directory, "endings.csv"));
        Assert.Equal(4, lines.Length);
        Assert.Contains(lines, l => l.Contains(",CerebrexDestroyed,1,0,Live"));
        Assert.Contains(lines, l => l.Contains(",RoyalAscent,1,,RecoveredLineage"));
        Assert.Contains(lines, l => l.StartsWith("2026-11-10T12:00:00.0000000Z,") && l.Contains(",VoidEmbraced,,,SaveTrace"));
        Assert.DoesNotContain(lines, l => l.Contains("AmbiguousLaunch"));
    }

    [Fact]
    public void PutCountFishingOverflowAndSnapshotChartKeyValidationBehaveSafely()
    {
        var state = new FamilyState { Revision = 9, SinceTick = 0, ThroughTick = 100 };
        state.Count("cnt", 3);
        var into = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        var ctx = new FamilyOutputContext(state, 100, into);

        OptionalTransitionRecording.PutCount(ctx, "cnt", WorkFamily.HarvestUnits);
        Assert.Equal(MetricKeys.Revision(WorkFamily.HarvestUnits), into[WorkFamily.HarvestUnits].Revision);

        OptionalTransitionRecording.PutCount(ctx, "cnt", "mod.owner.fam.metric");
        Assert.Equal(9, into["mod.owner.fam.metric"].Revision);

        var fishState = new FamilyState { Revision = FishingFamily.Family.Revision, SinceTick = 0, ThroughTick = 100 };
        fishState.Count("units", 5);
        fishState.Acc("units").Overflow = true;
        var fishOut = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        FishingFamily.Family.Build(new FamilyOutputContext(fishState, 100, fishOut));
        Assert.Equal(MetricCoverage.Partial, fishOut[FishingFamily.AcceptedUnitsByDef].Values[MetricEntry.Scalar].Coverage);

        var badKey = JsonSerializer.Deserialize<SnapshotChartKey>("{\"revision\":1,\"values\":[1.0],\"coverage\":[999]}")!;
        Assert.False(badKey.Fits(1));
        var goodKey = JsonSerializer.Deserialize<SnapshotChartKey>("{\"revision\":1,\"values\":[1.0],\"coverage\":[2]}")!;
        Assert.True(goodKey.Fits(1));

        Assert.True(CrossColonyCatalog.UsesThingDefBreakdown(MetricRegistry.Find(WorkFamily.HarvestByCrop)!));
        Assert.False(CrossColonyCatalog.UsesThingDefBreakdown(MetricRegistry.Find(MentalFamily.BreaksByDef)!));
    }
}
