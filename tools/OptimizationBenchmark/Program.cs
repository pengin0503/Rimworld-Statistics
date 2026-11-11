// Standalone, game-independent research benchmark. Measurements of this runner only; never an in-game TPS claim.
// No threshold is enforced. Private saves and profiles are read-only and only anonymous counts and timings are printed.
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using RimworldStatistics.Core;

var mode = args[0];
static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
static string F(double v) => v.ToString("F2", CultureInfo.InvariantCulture);

if (mode == "real")
{
    var dir = args[1];
    var files = Directory.EnumerateFiles(dir).Where(f => ImportEngine.IsSaveFile(Path.GetFileName(f))).Order(StringComparer.Ordinal).ToArray();
    long totalBytes = files.Sum(f => new FileInfo(f).Length);
    Console.WriteLine($"files={files.Length} totalMB={F(totalBytes / 1e6)}");
    // warm the page cache + JIT
    var reader = new SaveReader();
    foreach (var f in files) File.ReadAllBytes(f);
    reader.ReadDetailed(files[0]);
    var skipPerFile = args.Length > 2;
    var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, IgnoreWhitespace = true, IgnoreComments = true, IgnoreProcessingInstructions = true, CheckCharacters = false };
    double tRaw = 0, tSha = 0, tXml = 0, tReader = 0; long aXml = 0, aReader = 0;
    var perFile = new List<(double mb, double raw, double sha, double xml, double rd, long alloc, string ver)>();
    foreach (var f in skipPerFile ? Array.Empty<string>() : files)
    {
        var size = new FileInfo(f).Length;
        var buf = new byte[1 << 16];
        var s = Stopwatch.GetTimestamp();
        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
            while (fs.Read(buf, 0, buf.Length) > 0) { }
        var raw = Ms(s);
        s = Stopwatch.GetTimestamp();
        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
        using (var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        { int n; while ((n = fs.Read(buf, 0, buf.Length)) > 0) h.AppendData(buf, 0, n); h.GetHashAndReset(); }
        var sha = Ms(s);
        var a0 = GC.GetAllocatedBytesForCurrentThread();
        s = Stopwatch.GetTimestamp();
        using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan))
        using (var xr = XmlReader.Create(fs, settings))
        { xr.MoveToContent(); xr.Skip(); }
        var xml = Ms(s); var ax = GC.GetAllocatedBytesForCurrentThread() - a0;
        a0 = GC.GetAllocatedBytesForCurrentThread();
        s = Stopwatch.GetTimestamp();
        var r = reader.ReadDetailed(f);
        var rd = Ms(s); var ar = GC.GetAllocatedBytesForCurrentThread() - a0;
        tRaw += raw; tSha += sha; tXml += xml; tReader += rd; aXml += ax; aReader += ar;
        var ver = HistoryMetricReader.MajorMinor(r.Observation.GameVersion) ?? "?";
        perFile.Add((size / 1e6, raw, sha, xml, rd, ar, ver));
    }
    if (!skipPerFile) Console.WriteLine($"TOTAL raw_read_ms={F(tRaw)} sha_seq_ms={F(tSha)} xml_skip_ms={F(tXml)} savereader_ms={F(tReader)} xml_alloc_MB={F(aXml / 1e6)} reader_alloc_MB={F(aReader / 1e6)}");
    foreach (var g in perFile.GroupBy(p => p.ver).OrderBy(g => g.Key))
        Console.WriteLine($"ver={g.Key} n={g.Count()} MB={F(g.Sum(x => x.mb))} raw={F(g.Sum(x => x.raw))} sha={F(g.Sum(x => x.sha))} xmlskip={F(g.Sum(x => x.xml))} reader={F(g.Sum(x => x.rd))} readerAllocMB={F(g.Sum(x => x.alloc) / 1e6)} ms_per_MB={F(g.Sum(x => x.rd) / g.Sum(x => x.mb))}");
    var sorted = perFile.Select(p => p.rd).Order().ToArray();
    if (!skipPerFile) Console.WriteLine($"reader p50={F(sorted[sorted.Length / 2])} p95={F(sorted[(int)(sorted.Length * 0.95)])} max={F(sorted[^1])}  max_alloc_MB={F(perFile.Max(p => p.alloc) / 1e6)}");

    // full import engine, cold then warm, with a real ProfileStore
    for (var round = 0; round < 2; round++)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "rws-bench-" + Guid.NewGuid().ToString("N"));
        try
        {
            var session = new ProfileSession(new ProfileStore(tmp), () => DateTime.UtcNow);
            var engine = new ImportEngine(new SaveReader());
            var a0 = GC.GetTotalAllocatedBytes(true);
            var s = Stopwatch.GetTimestamp();
            var res = engine.Run(dir, session, null, CancellationToken.None);
            session.Flush();
            var cold = Ms(s); var ac = GC.GetTotalAllocatedBytes(true) - a0;
            a0 = GC.GetTotalAllocatedBytes(true);
            s = Stopwatch.GetTimestamp();
            var res2 = engine.Run(dir, session, null, CancellationToken.None);
            session.Flush();
            var warm = Ms(s); var aw = GC.GetTotalAllocatedBytes(true) - a0;
            var json = session.Read(ProfileJson.Serialize);
            var counts = session.Read(p => (p.SaveObservations.Count, p.MetricObservations.Count, p.LiveCampaigns.Count));
            s = Stopwatch.GetTimestamp();
            var view = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
            var tv = Ms(s);
            Console.WriteLine($"IMPORT round={round} cold_ms={F(cold)} cold_alloc_MB={F(ac / 1e6)} warm_ms={F(warm)} warm_alloc_MB={F(aw / 1e6)} read={res.Read} skipped2={res2.Skipped} failed={res.Failed} profile_bytes={Encoding.UTF8.GetByteCount(json)} saveObs={counts.Item1} metricObs={counts.Item2} colonies={view.Colonies.Count} first_view_ms={F(tv)} overallRows={view.Numeric.Overall.Count}");
            if (round == 1)
            {
                // view rebuild cost on the real profile
                var times = new List<double>(); long alloc = 0;
                for (var i = 0; i < 15; i++)
                {
                    session.Mutate(_ => { }, SaveMode.None);
                    var b = GC.GetAllocatedBytesForCurrentThread();
                    s = Stopwatch.GetTimestamp();
                    session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
                    times.Add(Ms(s)); alloc = GC.GetAllocatedBytesForCurrentThread() - b;
                }
                times.Sort();
                Console.WriteLine($"VIEW real profile p50_ms={F(times[times.Count / 2])} alloc_MB={F(alloc / 1e6)}");
                // serialize + store
                times.Clear();
                for (var i = 0; i < 15; i++) { s = Stopwatch.GetTimestamp(); session.Read(ProfileJson.Serialize); times.Add(Ms(s)); }
                times.Sort();
                Console.WriteLine($"SERIALIZE real profile p50_ms={F(times[times.Count / 2])}");
                times.Clear();
                for (var i = 0; i < 15; i++) { s = Stopwatch.GetTimestamp(); ProfileJson.Deserialize(json); times.Add(Ms(s)); }
                times.Sort();
                Console.WriteLine($"DESERIALIZE real profile p50_ms={F(times[times.Count / 2])}");
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "rws-real-profile.json"), json);
            }
        }
        finally { for (var k = 0; k < 5; k++) { try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); break; } catch (IOException) { Thread.Sleep(200); } } }
    }
}

if (mode == "hash")
{
    var id = new ColonyIdentity { SeedString = "seed-abc", PersistentRandomValue = 123456, StartingTile = "221529,0", GameStartAbsTick = 0, FirstWealthBits = 0x45ABCDEF, StartingPawnIds = { "Thing_Human1", "Thing_Human2", "Thing_Human3" } };
    for (var i = 0; i < 20000; i++) id.FingerprintHash();
    var a0 = GC.GetAllocatedBytesForCurrentThread(); var s = Stopwatch.GetTimestamp();
    const int N = 200000;
    for (var i = 0; i < N; i++) id.FingerprintHash();
    Console.WriteLine($"FingerprintHash ns/op={F(Ms(s) * 1e6 / N)} bytes/op={(GC.GetAllocatedBytesForCurrentThread() - a0) / N}");
    // evidence combine
    var two = new[] { MetricEvidence.Verified, MetricEvidence.Experimental };
    a0 = GC.GetAllocatedBytesForCurrentThread(); s = Stopwatch.GetTimestamp();
    for (var i = 0; i < N; i++) MetricEvidencePolicy.Combine(two);
    Console.WriteLine($"EvidenceCombine ns/op={F(Ms(s) * 1e6 / N)} bytes/op={(GC.GetAllocatedBytesForCurrentThread() - a0) / N}");
    // closure-vs-direct recording pattern (same shape as GameFacts.Record)
    var rec = new Dictionary<string, FamilyState>(StringComparer.Ordinal) { ["food"] = new FamilyState() };
    var hits = new Dictionary<string, int>(StringComparer.Ordinal);
    FamilyState? Fam(string name) { var f = rec.GetValueOrDefault(name); if (f != null) hits[name] = hits.GetValueOrDefault(name) + 1; return f; }
    void Record(string family, Action<FamilyState> h) { if (Fam(family) is { } st) h(st); }
    foreach (var fam in new[] { "food", "absent" })
    {
        double n = 1.5; bool free = true;
        for (var i = 0; i < 20000; i++) { var nn = n + i; Record(fam, st => { st.Count("ate"); st.Observe("nut", nn); if (free) st.Count("free"); }); }
        a0 = GC.GetAllocatedBytesForCurrentThread(); s = Stopwatch.GetTimestamp();
        for (var i = 0; i < N; i++) { var nn = n + i; Record(fam, st => { st.Count("ate"); st.Observe("nut", nn); if (free) st.Count("free"); }); }
        var tA = Ms(s) * 1e6 / N; var bA = (GC.GetAllocatedBytesForCurrentThread() - a0) / N;
        a0 = GC.GetAllocatedBytesForCurrentThread(); s = Stopwatch.GetTimestamp();
        for (var i = 0; i < N; i++) { var nn = n + i; if (rec.GetValueOrDefault(fam) is { } st) { st.Count("ate"); st.Observe("nut", nn); if (free) st.Count("free"); } }
        var tB = Ms(s) * 1e6 / N; var bB = (GC.GetAllocatedBytesForCurrentThread() - a0) / N;
        var slot = fam == "food" ? rec["food"] : null;
        a0 = GC.GetAllocatedBytesForCurrentThread(); s = Stopwatch.GetTimestamp();
        for (var i = 0; i < N; i++) { var nn = n + i; if (slot is { } st) { st.Count("ate"); st.Observe("nut", nn); if (free) st.Count("free"); } }
        var tC = Ms(s) * 1e6 / N;
        Console.WriteLine($"RecordPattern family={fam} closure+hits ns={F(tA)} B={bA} | direct-dict ns={F(tB)} B={bB} | direct-slot ns={F(tC)}");
    }
}

if (mode == "scale")
{
    // N saves over C colonies. modVary: each save has its own ModSetHash (no SaveFile compaction).
    foreach (var (N, C, modVary) in new[] { (500, 50, false), (2000, 200, false), (500, 50, true), (2000, 200, true), (5000, 500, true) })
    {
        var p = new Profile();
        var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var s = Stopwatch.GetTimestamp();
        var tail = new List<double>();
        for (var i = 0; i < N; i++)
        {
            var c = i % C; var k = i / C;
            var ident = new ColonyIdentity { SeedString = "seed" + c, PersistentRandomValue = c, StartingTile = c + ",0", GameStartAbsTick = 0, FirstWealthBits = (uint)(1000 + c), StartingPawnIds = { "P" + c + "a", "P" + c + "b", "P" + c + "c" } };
            var name = $"col{c}-save{k}.rws"; var time = epoch.AddMinutes(i); long size = 1000000 + i;
            var so = new SaveObservation { FileName = name, FileSize = size, LastWriteUtc = time, ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok, GameVersion = "1.6.4566 rev607", Identity = ident };
            so.Values.TicksGame = 60000 * (k + 1); so.Values.WealthMax = 1000 + k; so.Values.WealthLast = 1000 + k; so.Values.WealthPoints = 10;
            var mo = new MetricObservation { Source = MetricObservationSource.SaveFile, FormatVersion = SaveReader.Version, Key = "hash" + i, Identity = ident.Clone(), GameVersion = so.GameVersion, SaveTick = so.Values.TicksGame, ObservedUtc = time, ModSetHash = modVary ? "m" + i : "m", ActiveDlcIds = new List<string>() };
            mo.FileAliases.Add(new MetricFileAlias { FileName = name, FileSize = size, LastWriteUtc = time });
            mo.Metrics[MetricKeys.ModsLatest] = MetricEntry.Of(1, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 10 });
            foreach (var d in MetricRegistry.Catalog.Where(d => d.Source == MetricObservationSource.SaveFile && d.Overall != OverallAggregation.Ranking).Take(40))
                mo.Metrics[d.StoredKey] = MetricEntry.Of(d.Revision, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, RealValue = i % 97 });
            var t = Stopwatch.GetTimestamp();
            ProfileMutations.UpsertMetricObservation(p, mo);
            ProfileMutations.AddSaveObservation(p, so);
            if (i >= N - 50) tail.Add(Ms(t));
        }
        var total = Ms(s); tail.Sort();
        var json = ProfileJson.Serialize(p);
        var vt = new List<double>(); long va = 0;
        for (var i = 0; i < 5; i++)
        {
            var b = GC.GetAllocatedBytesForCurrentThread(); var t = Stopwatch.GetTimestamp();
            var cols = CampaignMatcher.BuildColonies(p);
            var bc = Ms(t);
            var stats = StatsCalculator.Calculate(cols, new StatsOptions());
            var view = MetricCalculator.BuildView(p, cols, stats, MetricRegistry.Compose(p.ModMetricDescriptors, new HashSet<string>(MetricDisplayPolicy.AllDetailsVisibleUnits, StringComparer.Ordinal)));
            vt.Add(Ms(t)); va = GC.GetAllocatedBytesForCurrentThread() - b;
            if (i == 4) Console.Write($"[buildColonies_ms={F(bc)} colonies={cols.Count}] ");
        }
        vt.Sort();
        var st = Stopwatch.GetTimestamp(); for (var i = 0; i < 5; i++) ProfileJson.Serialize(p); var ser = Ms(st) / 5;
        Console.WriteLine($"SCALE N={N} C={C} modVary={modVary} mutate_total_ms={F(total)} tail_p50_ms={F(tail[tail.Count / 2])} saveObs={p.SaveObservations.Count} metricObs={p.MetricObservations.Count} json_MB={F(json.Length / 1e6)} view_p50_ms={F(vt[2])} view_alloc_MB={F(va / 1e6)} serialize_ms={F(ser)}");
    }
}

if (mode == "parallel")
{
    var dir = args[1];
    var files = Directory.EnumerateFiles(dir).Where(f => ImportEngine.IsSaveFile(Path.GetFileName(f))).Order(StringComparer.Ordinal).ToArray();
    foreach (var f in files) File.ReadAllBytes(f);
    new SaveReader().ReadDetailed(files[0]);
    foreach (var dop in new[] { 1, 2, 3, 4, 1, 2, 4 })
    {
        var peak0 = GC.GetTotalAllocatedBytes(true);
        var s = Stopwatch.GetTimestamp();
        var results = new SaveReadResult[files.Length];
        Parallel.For(0, files.Length, new ParallelOptions { MaxDegreeOfParallelism = dop }, i => results[i] = new SaveReader().ReadDetailed(files[i]));
        Console.WriteLine($"PARALLEL dop={dop} ms={F(Ms(s))} ok={results.Count(r => r.Metrics != null)} alloc_MB={F((GC.GetTotalAllocatedBytes(true) - peak0) / 1e6)}");
    }
}

if (mode == "store")
{
    var json = File.ReadAllText(args[1]);
    var profile = ProfileJson.Deserialize(json);
    foreach (var mult in new[] { 1, 10 })
    {
        if (mult > 1)
        {
            var extra = new List<MetricObservation>();
            for (var m = 1; m < mult; m++)
                foreach (var o in ProfileJson.Deserialize(json).MetricObservations) { o.Key += "-" + m; extra.Add(o); }
            profile.MetricObservations.AddRange(extra);
        }
        var tmp = Path.Combine(Path.GetTempPath(), "rws-store-" + Guid.NewGuid().ToString("N"));
        var store = new ProfileStore(tmp); store.Load(DateTime.UtcNow);
        var first = ProfileJson.Serialize(profile); store.SaveSerialized(first);
        var tImport = new List<double>(); var tStats = new List<double>();
        for (var i = 0; i < 9; i++)
        {
            profile.ImportState.LastScanUtc = DateTime.UtcNow.AddSeconds(i);
            var j = ProfileJson.Serialize(profile);
            var s = Stopwatch.GetTimestamp(); store.SaveSerialized(j); tImport.Add(Ms(s));
            profile.UserDecisions.ExcludedCampaignKeys = new List<string> { "k" + i };
            j = ProfileJson.Serialize(profile);
            s = Stopwatch.GetTimestamp(); store.SaveSerialized(j); tStats.Add(Ms(s));
        }
        tImport.Sort(); tStats.Sort();
        // pieces
        var cur = File.ReadAllText(Path.Combine(tmp, "profile.json"));
        var s2 = Stopwatch.GetTimestamp(); for (var i = 0; i < 9; i++) File.ReadAllText(Path.Combine(tmp, "profile.json")); var rd = Ms(s2) / 9;
        s2 = Stopwatch.GetTimestamp();
        for (var i = 0; i < 9; i++) { using var a = System.Text.Json.JsonDocument.Parse(cur); using var b = System.Text.Json.JsonDocument.Parse(first); _ = System.Text.Json.JsonElement.DeepEquals(a.RootElement, b.RootElement); }
        var cmp = Ms(s2) / 9;
        Console.WriteLine($"STORE x{mult} bytes={Encoding.UTF8.GetByteCount(cur)} commit_importStateOnly_p50_ms={F(tImport[4])} commit_statsChanged_p50_ms={F(tStats[4])} readback_ms={F(rd)} jsonCompare_ms={F(cmp)}");
        Directory.Delete(tmp, true);
    }
}

if (mode == "savepath")
{
    var keysPerBreakdown = int.Parse(args[1]);
    var saves = int.Parse(args[2]);
    var specs = LiveFamilies.All.Select(f => f.Spec).ToList();
    var acc = LiveMetricAccumulator.StartNew(specs, 0, new HashSet<string>(specs.Select(s => s.Name)));
    var rnd = new Random(1);
    void Events(int tick, int n)
    {
        var work = acc.Family(WorkFamily.Name)!; var details = acc.Family(WorkDetailsFamily.Name)!; var food = acc.Family(FoodFamily.Name)!; var life = acc.Family(LifetimeFamily.Name)!;
        for (var i = 0; i < n; i++)
        {
            var def = "ThingDef_" + rnd.Next(keysPerBreakdown); var w = "Thing_Human" + rnd.Next(20);
            var items = new[] { new CraftedThing { Def = def, Count = 1 + rnd.Next(5), Quality = "Normal", UnitMarketValue = 12.5 } };
            WorkFamily.Crafted(work, w, items, tick);
            WorkDetailsFamily.CraftedProducts(details, items);
            WorkDetailsFamily.MinedYield(details, def, 3);
            WorkFamily.Mined(work, w, def, 3);
            WorkFamily.Harvested(work, w, "Plant" + i, "Crop_" + rnd.Next(Math.Min(60, keysPerBreakdown)), 4, tick);
            WorkFamily.Sown(work, "Crop_" + rnd.Next(Math.Min(60, keysPerBreakdown)));
            FoodFamily.Ate(food, 0.9, true);
            LifetimeFamily.SkillChanged(life, w, "Skill" + rnd.Next(12), 3, 4);
        }
        // generic keyed load on every other family (stand-in for their own breakdowns)
        foreach (var spec in specs)
        {
            var st = acc.Family(spec.Name)!;
            for (var i = 0; i < n / 4 + 1; i++) { st.CountBy("bench.by", "K" + rnd.Next(keysPerBreakdown)); st.Count("bench.n"); st.Observe("bench.obs", rnd.NextDouble()); }
        }
    }
    var profile = new Profile();
    var ident = new ColonyIdentity { SeedString = "s", PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 5, ModCampaignId = "camp", StartingPawnIds = { "A", "B", "C" } };
    var snapDefs = SnapshotCatalog.Definitions.ToArray();
    var epoch = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    Events(0, 4000);
    Console.WriteLine("save,prepare_ms,prepare_alloc_KB,checkpoint_json_KB,toObservation_ms,mutate_ms,mutate_alloc_KB,profile_serialize_ms,profile_MB,metricObs");
    for (var save = 1; save <= saves; save++)
    {
        var tick = save * 60000;
        Events(tick, 40);
        var a0 = GC.GetAllocatedBytesForCurrentThread(); var t = Stopwatch.GetTimestamp();
        var draft = acc.PrepareSave(tick, epoch.AddMinutes(save), Guid.NewGuid().ToString("N"))!;
        var prep = Ms(t); var prepA = GC.GetAllocatedBytesForCurrentThread() - a0;
        acc.Commit(draft);
        t = Stopwatch.GetTimestamp();
        var numeric = CheckpointMetrics.ToObservation(draft.Payload!, draft.Json, ident, "1.7.4985 rev1269", new List<string>());
        var toObs = Ms(t);
        var snap = new MetricObservation { Source = MetricObservationSource.SaveSnapshot, Key = Guid.NewGuid().ToString("N"), Identity = ident.Clone(), FormatVersion = 1, SaveTick = tick, ObservedUtc = epoch.AddMinutes(save), ActiveDlcIds = new List<string>() };
        foreach (var d in snapDefs) snap.Metrics[d.StoredKey] = MetricEntry.Of(d.Revision, new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = save % 17 });
        a0 = GC.GetAllocatedBytesForCurrentThread(); t = Stopwatch.GetTimestamp();
        ProfileMutations.ApplyLiveObservation(profile, "camp", ident, new ObservedValues { TicksGame = tick }, new DisplayInfo(), epoch.AddMinutes(save), "1.7.4985 rev1269");
        ProfileMutations.UpsertMetricObservation(profile, numeric);
        ProfileMutations.UpsertMetricObservation(profile, snap);
        var mut = Ms(t); var mutA = GC.GetAllocatedBytesForCurrentThread() - a0;
        if (save is 1 or 2 or 10 or 100 or 300 or 1000 or 2000 or 3000 || save == saves)
        {
            t = Stopwatch.GetTimestamp(); var json = ProfileJson.Serialize(profile); var ser = Ms(t);
            Console.WriteLine($"{save},{F(prep)},{prepA / 1024},{draft.Json.Length / 1024},{F(toObs)},{F(mut)},{mutA / 1024},{F(ser)},{F(json.Length / 1e6)},{profile.MetricObservations.Count}");
        }
    }
    var tv = Stopwatch.GetTimestamp();
    var cols = CampaignMatcher.BuildColonies(profile);
    var view = MetricCalculator.BuildView(profile, cols, StatsCalculator.Calculate(cols, new StatsOptions()), MetricRegistry.Catalog);
    Console.WriteLine($"view_ms={F(Ms(tv))}");
    var tl = Stopwatch.GetTimestamp(); var prof2 = ProfileJson.Deserialize(ProfileJson.Serialize(profile)); Console.WriteLine($"roundtrip_ms={F(Ms(tl))}");
}

if (mode == "profile-report")
{
    // Anonymous composition of an existing profile.json: counts and serialized bytes only (no names, ids or values).
    var json = File.ReadAllText(args[1]);
    var p = ProfileJson.Deserialize(json);
    Console.WriteLine($"profile_bytes={Encoding.UTF8.GetByteCount(json)} saveObservations={p.SaveObservations.Count} liveCampaigns={p.LiveCampaigns.Count} endings={p.EndingEvents.Count} descriptors={p.ModMetricDescriptors.Count}");
    Console.WriteLine("source,compressed,conflicted,count,serialized_bytes,max_bytes");
    foreach (var g in p.MetricObservations.Where(o => !o.IsQuarantined)
        .GroupBy(o => (o.Source, o.Compressed, o.Conflicted)).OrderBy(g => g.Key.Source).ThenBy(g => g.Key.Compressed))
    {
        var sizes = g.Select(o => (long)System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(o, ProfileJson.Options).Length).ToArray();
        Console.WriteLine($"{g.Key.Source},{g.Key.Compressed},{g.Key.Conflicted},{sizes.Length},{sizes.Sum()},{sizes.Max()}");
    }
    Console.WriteLine($"quarantined={p.MetricObservations.Count(o => o.IsQuarantined)}");
}
