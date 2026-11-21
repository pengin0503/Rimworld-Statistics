using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class AuditBoundaryAndConcurrencyTests
{
    [Fact]
    public void MetricCalculator_BuildView_HandlesManyColoniesAndBreakdownsWithinBounds()
    {
        var profile = new Profile();
        for (var c = 0; c < 120; c++)
        {
            var cid = $"colony-{c:D3}";
            for (var s = 1; s <= 10; s++)
            {
                var tick = s * 60_000;
                var saveName = $"{cid}-{s}.rws";
                var key = $"hash-{cid}-{s}";
                var saveObs = TestProfileBuilders.SaveObs(saveName, cid, tick, wealthLast: 10_000 + s * 1_000, colonistsKilled: s);
                profile.SaveObservations.Add(saveObs);

                var metricObs = TestProfileBuilders.SaveFileMetrics(key, cid, tick);
                metricObs.FileAliases.Add(new MetricFileAlias
                {
                    FileName = saveName,
                    FileSize = saveObs.FileSize,
                    LastWriteUtc = saveObs.LastWriteUtc,
                });
                var entry = new MetricEntry { Revision = 1 };
                entry.Values[MetricEntry.Scalar] = new MetricValue { IntegerValue = s * 10, Coverage = MetricCoverage.Complete };
                entry.Values[$"cat_{c % 8}"] = new MetricValue { IntegerValue = s * 5, Coverage = MetricCoverage.Complete };
                metricObs.Metrics["vanilla.stats.colonists_killed"] = entry;
                profile.MetricObservations.Add(metricObs);
            }
        }

        var sw = Stopwatch.StartNew();
        var view = TestProfileBuilders.BuildView(profile);
        sw.Stop();

        Assert.Equal(120, view.Colonies.Count);
        Assert.Equal(120, view.Stats.Colonies.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"BuildView took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void RestoreBranchFallbackParents_LinksLargeBranchedCheckpointsInLinearTime()
    {
        var profile = new Profile();
        var id = TestProfileBuilders.Identity("large-branch");
        const string metricKey = "vanilla.stats.colonists_killed";

        var baseObs = TestProfileBuilders.SaveObs("root.rws", "large-branch", 10_000);
        baseObs.Identity = id;
        profile.SaveObservations.Add(baseObs);

        // Main trunk + 3 branches of 100 checkpoints each where parent references use file/lineage fallback.
        string? prevKey = null;
        for (var b = 0; b < 3; b++)
        {
            var parent = b == 0 ? null : "cp-0-009";
            for (var i = 0; i < 100; i++)
            {
                var key = $"cp-{b}-{i:D3}";
                var tick = (i + 1) * 10_000 + b * 100;
                var cp = TestProfileBuilders.Checkpoint(key, parent, "large-branch", tick);
                cp.Identity = id;
                cp.Metrics[metricKey] = MetricEntry.Of(1, new MetricValue
                {
                    IntegerValue = i + 1,
                    Coverage = MetricCoverage.Complete,
                    SinceTick = 0,
                    ThroughTick = tick,
                });
                profile.MetricObservations.Add(cp);
                parent = key;
                _ = prevKey;
                prevKey = key;
            }
        }

        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => DateTime.UtcNow);
        try
        {
            session.Mutate(p =>
            {
                foreach (var s in profile.SaveObservations) p.SaveObservations.Add(s);
                foreach (var m in profile.MetricObservations) p.MetricObservations.Add(m);
            }, SaveMode.None);

            var view = session.GetView(new StatsOptions { IncludeDevMode = true },
                MetricRegistry.ImplementedCatalog.Select(d => d.Acquisition).OfType<string>().ToHashSet(StringComparer.Ordinal),
                ViewScope.All);

            Assert.NotEmpty(view.Colonies);
            Assert.Equal(300, session.Read(p => p.MetricObservations.Count(o => o.Source == MetricObservationSource.Checkpoint)));
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public void RawInflate_Enforces64MiBLimitAndRejectsCorruptDeflateStreams()
    {
        // Truncated/corrupt deflate header inside zlib framing (0x78, 0x9C + garbage).
        Assert.ThrowsAny<InvalidDataException>(() => RawInflate.Inflate([0x78, 0x9C, 0xFF, 0xFF, 0x00, 0x00]));

        // Over-64MiB compressed zero stream triggers the uncompressed cap.
        var bigPayload = new byte[(64 * 1024 * 1024) + 4096];
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x01);
        using (var deflate = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(bigPayload, 0, bigPayload.Length);
        var compressed = ms.ToArray();

        Assert.Throws<InvalidDataException>(() => RawInflate.Inflate(compressed));
    }

    [Fact]
    public void ProfileStore_SchemaVersionOfSpan_HandlesWhitespaceMissingNestedAndCorruptInputs()
    {
        Assert.Equal(2, ProfileStore.SchemaVersionOf(" { \"meta\": { \"schemaVersion\": 99 }, \"schemaVersion\": 2 } "u8));
        Assert.Equal(1, ProfileStore.SchemaVersionOf("{\"saveObservations\":[]}"u8));
        Assert.Null(ProfileStore.SchemaVersionOf("{\"schemaVersion\":\"2\"}"u8));
        Assert.Null(ProfileStore.SchemaVersionOf("[] "u8));
        Assert.Null(ProfileStore.SchemaVersionOf("{\"schemaVersion\": 2"u8));

        using var dir = new TempDir();
        var validJson = ProfileJson.SerializeToUtf8Bytes(new Profile { SchemaVersion = Profile.CurrentSchemaVersion });
        File.WriteAllBytes(dir.File("profile.json.bak1"), validJson);
        File.WriteAllBytes(dir.File("profile.json"), "{corrupt"u8.ToArray());

        var store = new ProfileStore(dir.Path);
        var loaded = store.Load(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(ProfileLoadSource.Backup1, loaded.Source);
        Assert.NotNull(loaded.CorruptFileRenamedTo);
    }

    [Fact]
    public async Task StatisticsPrecomputer_DisposeAndStopDuringConcurrentSessionMutations_DoesNotHangOrLeak()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => DateTime.UtcNow);
        try
        {
            session.Mutate(p =>
            {
                p.SaveObservations.Add(TestProfileBuilders.SaveObs("s1.rws", "c1", 60_000));
                p.MetricObservations.Add(TestProfileBuilders.SaveFileMetrics("h1", "c1", 60_000));
            }, SaveMode.None);

            using var precomputer = new StatisticsPrecomputer();
            var target = new PrecomputeTarget(session, session.Revision, true, null);
            _ = precomputer.Poll(target, busy: false, TimeSpan.FromSeconds(0));
            _ = precomputer.Poll(target, busy: false, TimeSpan.FromSeconds(3));

            var mutator = Task.Run(() =>
            {
                for (var i = 0; i < 10; i++)
                {
                    session.ApplyCacheLimits(new CacheLimits(4, 1024, 2, 2));
                    session.Mutate(_ => { }, SaveMode.None);
                }
            }, TestContext.Current.CancellationToken);

            await mutator;
            precomputer.Stop();
            precomputer.Dispose();
            var faulted = precomputer.Poll(target, busy: false, TimeSpan.FromSeconds(10));
            Assert.Null(faulted);
        }
        finally
        {
            session.Close();
        }
    }

    [Fact]
    public async Task RecorderChartCache_RecoversAfterHashMismatchFailureOnSubsequentValidRead()
    {
        using var dir = new TempDir();
        var savePath = dir.File("chart.rws");
        var bytes = new SaveXmlBuilder().Minimal().Tick(ticksGame: 60_000, start: 0).WealthTotalDeflate([100f, 150f, 200f]).Build();
        File.WriteAllBytes(savePath, bytes);
        var info = new FileInfo(savePath);
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        var alias = new MetricFileAlias
        {
            FileName = info.Name,
            FileSize = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
        };

        var cache = new RecorderChartCache(16, 1024);
        var t1 = Task.Run(() => cache.Read(savePath, alias, new string('0', 64), HistoryMetricReader.WealthTotal, false), TestContext.Current.CancellationToken);
        var t2 = Task.Run(() => cache.Read(savePath, alias, new string('0', 64), HistoryMetricReader.WealthTotal, false), TestContext.Current.CancellationToken);
        var results = await Task.WhenAll(t1, t2);
        Assert.All(results, Assert.Empty);

        var recovered = cache.Read(savePath, alias, actualHash, HistoryMetricReader.WealthTotal, false);
        Assert.NotEmpty(recovered);
    }

    [Fact]
    public void StatisticsExport_WithNullHistorySeries_SucceedsForNullAndProvidedProfile()
    {
        var profile = new Profile
        {
            SaveObservations = { TestProfileBuilders.SaveObs("c1.rws", "c1", 60_000) },
        };
        var cp = TestProfileBuilders.Checkpoint("cp-1", null, "c1", 60_000);
        cp.Metrics["vanilla.stats.colonists_killed"] = MetricEntry.Of(1, new MetricValue
        {
            IntegerValue = 2,
            Coverage = MetricCoverage.Complete,
            SinceTick = 0,
            ThroughTick = 60_000,
        });
        profile.MetricObservations.Add(cp);

        var view = TestProfileBuilders.BuildView(profile);
        var labels = MetricRegistry.ImplementedCatalog.ToDictionary(d => d.Key, d => d.Key, StringComparer.Ordinal);
        var colonyNames = view.Stats.Colonies.ToDictionary(c => c.Key, c => c.FactionName ?? c.Key, StringComparer.Ordinal);
        var text = new ExportText(labels, colonyNames, new Dictionary<NumericRow, string>(), "English", "All", "2.0.0");

        using var outDir1 = new TempDir();
        var summaryWithoutProfile = StatisticsExport.Write(view, text, _ => UnitRecordingState.Recording, outDir1.Path,
            TestContext.Current.CancellationToken, historySeries: null);
        Assert.Equal(StatisticsExport.TotalFileCount, Directory.GetFiles(summaryWithoutProfile.Directory).Length);

        using var outDir2 = new TempDir();
        var summaryWithProfile = StatisticsExport.Write(view, text, _ => UnitRecordingState.Recording, outDir2.Path,
            TestContext.Current.CancellationToken, historySeries: null, profile: profile);
        Assert.True(summaryWithProfile.Rows["colony_metric_history.csv"] >= 1);
    }

    [Fact]
    public void SaveReader_IsolatesOptionalNumericCorruptionWhilePreservingStrictTicksGameOverflowFailure()
    {
        using var dir = new TempDir();
        var reader = new SaveReader();
        var partialCorruptPath = dir.File("partial_corrupt.rws");
        var xmlOk = """
            <?xml version="1.0" encoding="utf-8"?>
            <savegame>
              <meta><gameVersion>1.7.5011 rev24</gameVersion></meta>
              <game>
                <info>
                  <realPlayTimeInteracting>NaN</realPlayTimeInteracting>
                  <permadeathMode>notabool</permadeathMode>
                  <startingTile>12</startingTile>
                </info>
                <tickManager>
                  <ticksGame>60000</ticksGame>
                  <gameStartAbsTick>notanint</gameStartAbsTick>
                </tickManager>
                <storyWatcher>
                  <statsRecord>
                    <numRaidsEnemy>4</numRaidsEnemy>
                    <colonistsKilled>corrupt_value</colonistsKilled>
                  </statsRecord>
                </storyWatcher>
              </game>
            </savegame>
            """;
        File.WriteAllText(partialCorruptPath, xmlOk, Encoding.UTF8);
        var resultOk = reader.ReadDetailed(partialCorruptPath);
        Assert.NotEqual(ObservationStatus.Error, resultOk.Observation.Status);
        Assert.Equal(60_000, resultOk.Observation.Values.TicksGame);
        Assert.Equal(4, resultOk.Observation.Values.NumRaidsEnemy);
        Assert.Equal(0, resultOk.Observation.Values.ColonistsKilled);
        Assert.Equal(0, resultOk.Observation.Values.RealPlayTime);

        var overflowTicksPath = dir.File("overflow_ticks.rws");
        File.WriteAllText(overflowTicksPath, xmlOk.Replace("60000", "99999999999999999999999999999"), Encoding.UTF8);
        var resultBad = reader.ReadDetailed(overflowTicksPath);
        Assert.Equal(ObservationStatus.Error, resultBad.Observation.Status);
    }

    [Fact]
    public void CoreMathAndUtcTime_HandleNaNClampingRelativeToleranceAndLocalTimeNormalization()
    {
        var profile = new Profile
        {
            SaveObservations = { TestProfileBuilders.SaveObs("nan.rws", "c-nan", 60_000) },
        };
        var obs = TestProfileBuilders.SaveFileMetrics("h-nan", "c-nan", 60_000);
        obs.Metrics[MetricKeys.WealthTotalNet] = MetricEntry.Of(1, new MetricValue
        {
            RealValue = double.NaN,
            Coverage = MetricCoverage.Complete,
        });
        profile.MetricObservations.Add(obs);

        var view = TestProfileBuilders.BuildView(profile);
        var overall = MetricLookup.Overall(view, MetricKeys.WealthTotalNet);
        Assert.NotNull(overall);
        Assert.True(overall!.Value.Number is null || double.IsFinite(overall.Value.Number.Value));

        Assert.Equal(int.MaxValue, UtcTime.ClampToInt32((long)int.MaxValue + 1000L));
        Assert.Equal(long.MaxValue, UtcTime.ClampToInt64((double)long.MaxValue + 1e15));
        Assert.Equal(0L, UtcTime.ClampToInt64(double.NaN));
        Assert.True(UtcTime.RelativeEquals(1_000_000_000.0, 1_000_000_000.5));
        Assert.False(UtcTime.RelativeEquals(10.0, 10.5));

        var localTime = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Local);
        Assert.Equal(localTime.ToUniversalTime(), UtcTime.ToUtc(localTime));
        Assert.EndsWith(" UTC", UtcTime.FormatIso(localTime));
        Assert.EndsWith("Z", UtcTime.FormatCompactStamp(localTime));
    }
}
