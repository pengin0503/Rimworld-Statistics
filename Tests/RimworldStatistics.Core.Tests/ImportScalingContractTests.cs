using System.Text.Json;
using RimworldStatistics.Core.Tests.Common;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

[Trait("Category", "Performance")]
public sealed class ImportScalingContractTests
{
    private static readonly DateTime Epoch = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private static ColonyIdentity MakeIdentity(string campaignId, int index, int extraPawnCount = 3)
    {
        var id = new ColonyIdentity
        {
            SeedString = "seed-" + index,
            PersistentRandomValue = index + 1,
            StartingTile = $"{100 + index},0",
            GameStartAbsTick = 0,
            FirstWealthBits = BitConverter.SingleToUInt32Bits(5000f + index),
            ModCampaignId = campaignId,
        };
        for (var i = 0; i < extraPawnCount; i++)
            id.StartingPawnIds.Add($"Thing_Human_{index}_{i}");
        return id;
    }

    private static (SaveObservation So, MetricObservation Cp, MetricObservation Sf) MakeSaveTuple(
        int step, ColonyIdentity ident, string? parentKey, string fileName, long fileSize, DateTime utc, string modSetHash)
    {
        var cpKey = $"{ident.ModCampaignId}-cp-{step:D5}";
        var tick = step * 10000;
        var so = new SaveObservation
        {
            FileName = fileName,
            FileSize = fileSize,
            LastWriteUtc = utc,
            ReaderVersion = SaveReader.Version,
            Status = ObservationStatus.Ok,
            GameVersion = "1.7.5000 rev1275",
            Identity = ident.Clone(),
            Values = new ObservedValues { TicksGame = tick, WealthMax = 1000 + step, WealthLast = 1000 + step, WealthPoints = 10, RealPlayTime = step * 5 },
        };
        var alias = new MetricFileAlias { FileName = fileName, FileSize = fileSize, LastWriteUtc = utc };
        var cp = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            FormatVersion = 1,
            Key = cpKey,
            ParentKey = parentKey,
            PayloadHash = cpKey + "-hash",
            Identity = ident.Clone(),
            SaveTick = tick,
            ObservedUtc = utc,
            FileAliases = { alias },
        };
        cp.Metrics[ResearchFamily.Completed] = MetricEntry.Of(1, new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = step });

        var sf = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile,
            FormatVersion = SaveReader.Version,
            Key = $"sf-{cpKey}",
            Identity = ident.Clone(),
            SaveTick = tick,
            ObservedUtc = utc,
            ModSetHash = modSetHash,
            FileAliases = { new MetricFileAlias { FileName = fileName, FileSize = fileSize, LastWriteUtc = utc } },
        };
        sf.Metrics[MetricKeys.ModsLatest] = MetricEntry.Of(1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 1 });
        return (so, cp, sf);
    }

    [Fact]
    public void Import_LinearAllocationAcrossLargeCheckpointAndSaveFileBatches()
    {
        long Run(int saves)
        {
            var items = new (SaveObservation So, MetricObservation Cp, MetricObservation Sf)[saves];
            var ident = MakeIdentity("camp-scale", 1);
            string? parent = null;
            for (var i = 0; i < saves; i++)
            {
                var step = i + 1;
                items[i] = MakeSaveTuple(step, ident, parent, $"save-{step:D5}.rws", 500_000 + step, Epoch.AddMinutes(step), "mods-v1");
                parent = items[i].Cp.Key;
            }
            var profile = new Profile();
            return Allocations.OnThisThread(() =>
            {
                for (var i = 0; i < items.Length; i++)
                {
                    var (so, cp, sf) = items[i];
                    ProfileMutations.UpsertMetricObservation(profile, cp);
                    ProfileMutations.UpsertMetricObservation(profile, sf);
                    ProfileMutations.AddSaveObservation(profile, so);
                }
            });
        }

        Run(20); // Warm up JIT
        var small = Run(250);
        var large = Run(1000);
        var ratio = (double)large / Math.Max(1, small);
        TestContext.Current.SendDiagnosticMessage($"Import 250 saves: {small / 1024} KiB; 1000 saves: {large / 1024} KiB (ratio {ratio:F2}x)");
        Assert.InRange(ratio, 0.5, 8.0);
    }

    [Fact]
    public void BuildColonies_LinearAllocationAcrossManyColoniesWithLedger()
    {
        long Run(int colonies)
        {
            var p = new Profile();
            for (var c = 0; c < colonies; c++)
            {
                var ident = MakeIdentity($"camp-{c:D4}", c);
                var (so, cp, sf) = MakeSaveTuple(1, ident, null, $"col-{c:D4}.rws", 100_000 + c, Epoch.AddMinutes(c), "mods");
                ProfileMutations.UpsertMetricObservation(p, cp);
                ProfileMutations.UpsertMetricObservation(p, sf);
                ProfileMutations.AddSaveObservation(p, so);
            }
            // Populate ColonyKeys ledger first, then measure steady-state BuildColonies allocation
            CampaignMatcher.BuildColonies(p);
            return Allocations.OnThisThread(() => CampaignMatcher.BuildColonies(p));
        }

        Run(10); // Warm up
        var small = Run(50);
        var large = Run(200);
        var ratio = (double)large / Math.Max(1, small);
        TestContext.Current.SendDiagnosticMessage($"BuildColonies 50: {small / 1024} KiB; 200: {large / 1024} KiB (ratio {ratio:F2}x)");
        Assert.InRange(ratio, 0.5, 8.0);
    }

    [Fact]
    public void LargeMixedHistory_PreservesLineageBranchesConflictsDuplicatesAndReimportIdempotency()
    {
        using var plainDir = new TempDir();
        using var tieredDir = new TempDir();
        var archive = new FileCheckpointArchive(tieredDir.File("archive"));
        var direct = new Profile();
        var session = new ProfileSession(new ProfileStore(tieredDir.Path), () => Epoch, archive);

        var modMetric = TestShapes.CounterMetric("quests_done", breakdown: true);
        ProfileMutations.RememberModMetrics(direct, [modMetric]);
        session.Mutate(p => ProfileMutations.RememberModMetrics(p, [modMetric]), SaveMode.None);

        var mainIdent = MakeIdentity("camp-main", 1);
        var altIdent = MakeIdentity("camp-alt", 2);
        // Same fingerprint hash as camp-main, disjoint starting pawns => must get separate fp:#2 key
        var collidingIdent = MakeIdentity("camp-collide", 1, extraPawnCount: 0);
        collidingIdent.StartingPawnIds.Add("Thing_Human_Other_99");

        var batch = new List<(SaveObservation So, MetricObservation Cp, MetricObservation Sf)>();
        string? mainParent = null;
        for (var i = 1; i <= 250; i++)
        {
            var t = MakeSaveTuple(i, mainIdent, mainParent, $"main-{i:D4}.rws", 200_000 + i, Epoch.AddMinutes(i), "m1");
            t.Cp.Metrics[modMetric.StableKey] = MetricEntry.Of(1, new MetricValue { IntegerValue = i * 2, Coverage = MetricCoverage.Complete });
            t.Cp.EvidenceByAcquisition = new(StringComparer.Ordinal) { [modMetric.AcquisitionId] = MetricEvidence.Verified };
            if (i == 50)
                t.Cp.Metrics["future.unknown.metric"] = MetricEntry.Of(9, new MetricValue { IntegerValue = 777, Coverage = MetricCoverage.Complete });
            batch.Add(t);
            mainParent = t.Cp.Key;
        }

        // Branch + rewind from step 120 (added before later arrivals or after)
        var branchParent = $"{mainIdent.ModCampaignId}-cp-{120:D5}";
        var branch1 = MakeSaveTuple(121, mainIdent, branchParent, "branch-1.rws", 250_001, Epoch.AddMinutes(500), "m1");
        branch1.Cp = CloneWithKeyAndTick(branch1.Cp, "camp-main-branch-121", branchParent, 120 * 10000 + 5000);
        var branch2 = MakeSaveTuple(122, mainIdent, branch1.Cp.Key, "branch-2.rws", 250_002, Epoch.AddMinutes(501), "m1");
        branch2.Cp = CloneWithKeyAndTick(branch2.Cp, "camp-main-branch-122", branch1.Cp.Key, 120 * 10000 + 8000);
        batch.Add(branch1);
        batch.Add(branch2);

        // Duplicate file alias of main step 100
        var dup100 = MakeSaveTuple(100, mainIdent, $"{mainIdent.ModCampaignId}-cp-{99:D5}", "main-0100-copy.rws", 200_100, Epoch.AddMinutes(100), "m1");
        dup100.Cp.Metrics[modMetric.StableKey] = MetricEntry.Of(1, new MetricValue { IntegerValue = 200, Coverage = MetricCoverage.Complete });
        dup100.Sf.Key = batch[99].Sf.Key; // same file content SHA-256

        // Out-of-order parent/child for altIdent
        var altRoot = MakeSaveTuple(1, altIdent, null, "alt-1.rws", 300_001, Epoch.AddMinutes(600), "m1");
        var altChild = MakeSaveTuple(2, altIdent, altRoot.Cp.Key, "alt-2.rws", 300_002, Epoch.AddMinutes(601), "m1");
        // Colliding fingerprint colony
        var colItem = MakeSaveTuple(1, collidingIdent, null, "collide-1.rws", 400_001, Epoch.AddMinutes(700), "m1");

        // Alt colony also overwrites Autosave-alt.rws with a strictly dominating save (non-colliding fingerprint -> must prune!)
        var autoAlt1 = MakeSaveTuple(3, altIdent, altChild.Cp.Key, "Autosave-alt.rws", 310_001, Epoch.AddMinutes(602), "m1");
        var autoAlt2 = MakeSaveTuple(4, altIdent, autoAlt1.Cp.Key, "Autosave-alt.rws", 310_002, Epoch.AddMinutes(603), "m1");

        // Autosave-1.rws overwritten by alt colony, then main colony (colliding fingerprint suppresses pruning!), then non-dominating rewind
        var autoAlt = MakeSaveTuple(3, altIdent, altChild.Cp.Key, "Autosave-1.rws", 300_003, Epoch.AddMinutes(604), "m1");
        var autoMain1 = MakeSaveTuple(251, mainIdent, mainParent, "Autosave-1.rws", 300_004, Epoch.AddMinutes(605), "m1");
        var autoMain2 = MakeSaveTuple(252, mainIdent, autoMain1.Cp.Key, "Autosave-1.rws", 300_005, Epoch.AddMinutes(606), "m1");
        var autoMainRewind = MakeSaveTuple(240, mainIdent, mainParent, "Autosave-1.rws", 300_006, Epoch.AddMinutes(607), "m1");
        autoMainRewind.Cp = CloneWithKeyAndTick(autoMainRewind.Cp, "camp-main-cp-rewind", mainParent, 240 * 10000);

        // Apply to both direct and session; put altChild BEFORE altRoot to test out-of-order compression
        var ordered = new List<(SaveObservation So, MetricObservation Cp, MetricObservation Sf)>();
        ordered.AddRange(batch);
        ordered.Add(dup100);
        ordered.Add(altChild);
        ordered.Add(altRoot);
        ordered.Add(colItem);
        ordered.Add(autoAlt1);
        ordered.Add(autoAlt2);
        ordered.Add(autoAlt);
        ordered.Add(autoMain1);
        ordered.Add(autoMain2);
        ordered.Add(autoMainRewind);

        void ApplyOne(Profile p, (SaveObservation So, MetricObservation Cp, MetricObservation Sf) item)
        {
            var jsonCp = JsonSerializer.Serialize(item.Cp, ProfileJson.CompactOptions);
            var jsonSf = JsonSerializer.Serialize(item.Sf, ProfileJson.CompactOptions);
            var jsonSo = JsonSerializer.Serialize(item.So, ProfileJson.CompactOptions);
            ProfileMutations.UpsertMetricObservation(p, JsonSerializer.Deserialize<MetricObservation>(jsonCp, ProfileJson.CompactOptions)!);
            ProfileMutations.UpsertMetricObservation(p, JsonSerializer.Deserialize<MetricObservation>(jsonSf, ProfileJson.CompactOptions)!);
            ProfileMutations.AddSaveObservation(p, JsonSerializer.Deserialize<SaveObservation>(jsonSo, ProfileJson.CompactOptions)!);
        }

        foreach (var item in ordered)
        {
            ApplyOne(direct, item);
            session.Mutate(p => ApplyOne(p, item), SaveMode.None);
        }
        // Re-importing the current on-disk state (latest content per filename) must be strictly idempotent
        var beforeDirect = ProfileJson.Serialize(direct);
        var currentByFile = ordered
            .GroupBy(x => x.So.FileName, StringComparer.Ordinal)
            .Select(g => g.Last())
            .ToList();
        foreach (var item in currentByFile)
            ApplyOne(direct, item);
        Assert.Equal(beforeDirect, ProfileJson.Serialize(direct));

        // Conflict on latest autosave (autoMain2) must restore compression on autoMain1 and still show identical values
        var conflict = MakeSaveTuple(252, mainIdent, autoMain1.Cp.Key, "Autosave-1.rws", 300_005, Epoch.AddMinutes(606), "m1");
        conflict.Cp.PayloadHash = "CONFLICTING-PAYLOAD";
        ApplyOne(direct, conflict);
        session.Mutate(p => ApplyOne(p, conflict), SaveMode.None);
        session.Flush();

        // Check out-of-order compression on altRoot
        var directAltRoot = direct.MetricObservations.Single(o => o.Key == altRoot.Cp.Key);
        Assert.True(directAltRoot.Compressed);
        // Check step 50 retained its future.unknown.metric despite archiving in session
        var sessionStep50 = session.Read(p => p.MetricObservations.Single(o => o.Key == $"{mainIdent.ModCampaignId}-cp-00050"));
        Assert.Contains("future.unknown.metric", sessionStep50.Metrics.Keys);
        // Check conflicting colony got #2 key
        var directCols = CampaignMatcher.BuildColonies(direct);
        Assert.Equal(3, directCols.Count);
        Assert.Contains(directCols, c => c.Key.Contains('#'));
        // Non-colliding alt colony's dominated Autosave-alt.rws is pruned; colliding main colony's autoMain1 is safely retained; alt on Autosave-1.rws is kept superseded
        Assert.DoesNotContain(direct.SaveObservations, o => o.Values.TicksGame == 3 * 10000 && o.FileName == "Autosave-alt.rws");
        Assert.Contains(direct.SaveObservations, o => o.Superseded && o.Values.TicksGame == 251 * 10000 && o.FileName == "Autosave-1.rws");
        Assert.Contains(direct.SaveObservations, o => o.Superseded && o.Identity.ModCampaignId == "camp-alt" && o.FileName == "Autosave-1.rws");

        // Plain direct Profile vs Archived Session views must agree on all counts & lineages
        var directStats = StatsCalculator.Calculate(directCols, new StatsOptions());
        var directNumeric = MetricCalculator.BuildView(direct, directCols, directStats,
            MetricRegistry.Compose(direct.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits));
        var sessionView = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);

        Assert.Equal(directCols.Count, sessionView.Colonies.Count);
        Assert.Equal(
            directNumeric.Overall.Select(r => (r.Definition.Key, r.Value.Number, r.Value.Coverage)),
            sessionView.Numeric.Overall.Select(r => (r.Definition.Key, r.Value.Number, r.Value.Coverage)));
    }

    [Fact]
    public void SaveFileCompaction_RespectsCheckpointAliasesModSetUnknownMetricsAndLargePawnSets()
    {
        var p = new Profile();
        // > 16 pawns with duplicates to exercise SameStringSet fallback path
        var ident1 = MakeIdentity("camp-sf", 1, extraPawnCount: 20);
        ident1.StartingPawnIds.Add(ident1.StartingPawnIds[0]);
        var ident1Reordered = MakeIdentity("camp-sf", 1, extraPawnCount: 20);
        ident1Reordered.StartingPawnIds.Reverse();
        var identDiffPawn = MakeIdentity("camp-sf", 1, extraPawnCount: 20);
        identDiffPawn.StartingPawnIds[^1] = "Thing_Human_Different";

        static MetricObservation MakeSf(string key, ColonyIdentity id, int tick, string file, string modHash, long modCount)
        {
            var sf = new MetricObservation
            {
                Source = MetricObservationSource.SaveFile,
                FormatVersion = SaveReader.Version,
                Key = key,
                Identity = id.Clone(),
                SaveTick = tick,
                ObservedUtc = Epoch.AddMinutes(tick / 1000),
                ModSetHash = modHash,
                FileAliases = { new MetricFileAlias { FileName = file, FileSize = 1000 + tick, LastWriteUtc = Epoch.AddMinutes(tick / 1000) } },
            };
            sf.Metrics[MetricKeys.ModsLatest] = MetricEntry.Of(1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = modCount });
            return sf;
        }

        var sfOld = MakeSf("sf-old", ident1, 1000, "old.rws", "mods-a", 5);
        var sfProtectedByCp = MakeSf("sf-cp-linked", ident1, 2000, "cp-linked.rws", "mods-a", 5);
        var cpLink = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            FormatVersion = 1,
            Key = "cp-link",
            PayloadHash = "cp-link-hash",
            Identity = ident1.Clone(),
            SaveTick = 2000,
            ObservedUtc = Epoch.AddMinutes(2),
            FileAliases = { new MetricFileAlias { FileName = "cp-linked.rws", FileSize = 3000, LastWriteUtc = Epoch.AddMinutes(2) } },
        };
        var sfUnknown = MakeSf("sf-unknown", ident1, 1500, "unknown.rws", "mods-a", 5);
        sfUnknown.Metrics["future.unknown.savefile"] = MetricEntry.Of(1, new MetricValue { IntegerValue = 9 });
        var sfDiffModCount = MakeSf("sf-modcount", ident1, 1600, "modcount.rws", "mods-a", 6);
        var sfDiffPawns = MakeSf("sf-diffpawn", identDiffPawn, 1700, "diffpawn.rws", "mods-a", 5);

        ProfileMutations.UpsertMetricObservation(p, sfOld);
        ProfileMutations.UpsertMetricObservation(p, cpLink);
        ProfileMutations.UpsertMetricObservation(p, sfProtectedByCp);
        ProfileMutations.UpsertMetricObservation(p, sfUnknown);
        ProfileMutations.UpsertMetricObservation(p, sfDiffModCount);
        ProfileMutations.UpsertMetricObservation(p, sfDiffPawns);

        // Newer equivalent save compacts sfOld (same set modulo duplicates/order) but preserves checkpoint-linked, unknown, different ModCount, different pawns
        var sfNewer = MakeSf("sf-newer", ident1Reordered, 5000, "newer.rws", "mods-a", 5);
        ProfileMutations.UpsertMetricObservation(p, sfNewer);

        // Checkpoint-linked newer SaveFile must NEVER compact unprotected older saves either
        var cp2 = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            FormatVersion = 1,
            Key = "cp-link-2",
            ParentKey = "cp-link",
            PayloadHash = "cp-link-2-hash",
            Identity = ident1.Clone(),
            SaveTick = 6000,
            ObservedUtc = Epoch.AddMinutes(6),
            FileAliases = { new MetricFileAlias { FileName = "cp-linked-2.rws", FileSize = 7000, LastWriteUtc = Epoch.AddMinutes(6) } },
        };
        var sfCp2 = MakeSf("sf-cp-linked-2", ident1, 6000, "cp-linked-2.rws", "mods-a", 5);
        ProfileMutations.UpsertMetricObservation(p, cp2);
        ProfileMutations.UpsertMetricObservation(p, sfCp2);

        var remainingSfKeys = p.MetricObservations
            .Where(o => o.Source == MetricObservationSource.SaveFile)
            .Select(o => o.Key)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("sf-old", remainingSfKeys);
        Assert.Contains("sf-newer", remainingSfKeys);
        Assert.Contains("sf-cp-linked", remainingSfKeys);
        Assert.Contains("sf-cp-linked-2", remainingSfKeys);
        Assert.Contains("sf-unknown", remainingSfKeys);
        Assert.Contains("sf-modcount", remainingSfKeys);
        Assert.Contains("sf-diffpawn", remainingSfKeys);
    }

    [Fact]
    public void CampaignMatcher_PreservesKeyOrderingCollidingFingerprintsAndLargePawnSets()
    {
        var p = new Profile();
        for (var i = 0; i < 120; i++)
        {
            var id = MakeIdentity($"camp-{i:D3}", i, extraPawnCount: i == 0 ? 20 : 3);
            var (_, cp, sf) = MakeSaveTuple(1, id, null, $"c-{i:D3}.rws", 10_000 + i, Epoch.AddMinutes(i), "m");
            var (so, _, _) = MakeSaveTuple(1, id, null, $"c-{i:D3}.rws", 10_000 + i, Epoch.AddMinutes(i), "m");
            ProfileMutations.UpsertMetricObservation(p, cp);
            ProfileMutations.UpsertMetricObservation(p, sf);
            ProfileMutations.AddSaveObservation(p, so);
        }

        // Add two more colonies sharing colony 0's fingerprint hash with disjoint large pawn sets => must get #2 and #3
        var col1 = MakeIdentity("camp-col-2", 0, extraPawnCount: 18);
        for (var k = 0; k < col1.StartingPawnIds.Count; k++)
            col1.StartingPawnIds[k] = $"OtherA_{k}";
        var col2 = MakeIdentity("camp-col-3", 0, extraPawnCount: 18);
        for (var k = 0; k < col2.StartingPawnIds.Count; k++)
            col2.StartingPawnIds[k] = $"OtherB_{k}";
        foreach (var id in new[] { col1, col2 })
        {
            var (so, cp, sf) = MakeSaveTuple(1, id, null, $"{id.ModCampaignId}.rws", 20_000, Epoch.AddMinutes(500), "m");
            ProfileMutations.UpsertMetricObservation(p, cp);
            ProfileMutations.UpsertMetricObservation(p, sf);
            ProfileMutations.AddSaveObservation(p, so);
        }

        var firstCols = CampaignMatcher.BuildColonies(p);
        Assert.Equal(122, firstCols.Count);
        var baseHash = firstCols.First(c => c.CampaignIds().Contains("camp-000")).FingerprintHash!;
        var colliding = firstCols.Where(c => c.FingerprintHash == baseHash).Select(c => c.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { $"fp:{baseHash}", $"fp:{baseHash}#2", $"fp:{baseHash}#3" }, colliding);

        p.UserDecisions.ExcludedCampaignKeys.Add($"fp:{baseHash}#2");
        var secondCols = CampaignMatcher.BuildColonies(p);
        Assert.Equal(122, p.ColonyKeys.Count);
        Assert.Equal(
            firstCols.Select(c => (c.Key, Count: c.Observations.Count)).OrderBy(x => x.Key, StringComparer.Ordinal),
            secondCols.Select(c => (c.Key, Count: c.Observations.Count)).OrderBy(x => x.Key, StringComparer.Ordinal));
        Assert.True(secondCols.Single(c => c.Key == $"fp:{baseHash}#2").Excluded);
    }

    [Fact]
    public void ImportEngine_RealSaveDirectory_ColdAndWarmReimportWithArchiveIsIdempotent()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var archive = new FileCheckpointArchive(store.File("archive"));
        var specs = new[] { new FamilySpec("research", 1) };
        var acc = LiveMetricAccumulator.StartNew(specs, 0);
        const string campId = "c0ffee00c0ffee00c0ffee00c0ffee01";

        byte[]? tenthBytes = null;
        for (var i = 1; i <= 40; i++)
        {
            acc.Family("research")!.Count("finished");
            var draft = acc.PrepareSave(i * 1000, Epoch.AddMinutes(i), $"cp-{i:D3}")!;
            acc.Commit(draft);
            var bytes = new SaveXmlBuilder()
                .Minimal()
                .Tick(i * 1000)
                .Component(SaveReader.ComponentClassName,
                    new System.Xml.Linq.XElement("campaignId", campId),
                    new System.Xml.Linq.XElement("dataVersion", 2),
                    new System.Xml.Linq.XElement("numericStatistics", draft.Json))
                .Build();
            File.WriteAllBytes(Path.Combine(saves.Path, $"save-{i:D3}.rws"), bytes);
            if (i == 10)
                tenthBytes = bytes;
        }
        File.WriteAllBytes(Path.Combine(saves.Path, "save-010-copy.rws"), tenthBytes!);

        var engine = new ImportEngine(new SaveReader());
        var session = new ProfileSession(new ProfileStore(store.Path), () => Epoch, archive);
        var cold = engine.Run(saves.Path, session, null, CancellationToken.None);
        session.Flush();
        Assert.Equal((41, 0, 0), (cold.Read, cold.Skipped, cold.Failed));
        var scan1 = session.Read(p => p.ImportState.LastScanUtc);
        var viewAfterCold = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        var jsonAfterCold = ProfileJson.Serialize(session.Read(p => p));

        var warm = engine.Run(saves.Path, session, null, CancellationToken.None);
        session.Flush();
        Assert.Equal((0, 41, 0), (warm.Read, warm.Skipped, warm.Failed));
        var scan2 = session.Read(p => p.ImportState.LastScanUtc);
        Assert.True(scan2 >= scan1);
        session.Mutate(p => p.ImportState.LastScanUtc = scan1, SaveMode.None);
        Assert.Equal(jsonAfterCold, ProfileJson.Serialize(session.Read(p => p)));
        var viewAfterWarm = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        Assert.Equal(
            viewAfterCold.Numeric.Overall.Select(r => (r.Definition.Key, r.Value.Number, r.Value.Coverage)),
            viewAfterWarm.Numeric.Overall.Select(r => (r.Definition.Key, r.Value.Number, r.Value.Coverage)));
    }

    private static MetricObservation CloneWithKeyAndTick(MetricObservation src, string key, string? parentKey, int tick)
    {
        var clone = JsonSerializer.Deserialize<MetricObservation>(
            JsonSerializer.Serialize(src, ProfileJson.CompactOptions), ProfileJson.CompactOptions)!;
        clone.Key = key;
        clone.ParentKey = parentKey;
        clone.PayloadHash = key + "-hash";
        clone.SaveTick = tick;
        return clone;
    }
}
