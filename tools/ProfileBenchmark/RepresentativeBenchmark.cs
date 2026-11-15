using System.Diagnostics;
using System.Globalization;
using RimworldStatistics.Core;

/// <summary>
/// Issue #195 measurements on profiles shaped like imported real ones (a read-only import of six private 1.7 saves gave
/// ~51 KB per checkpoint tip and ~8.8 KB per save-file observation). Synthetic data only; machine-dependent timings,
/// no thresholds. Every write goes through <see cref="ProfileSession"/> (its real path, public API only, so the same
/// code measures an older build): "hold" is how long another thread could not get the profile during the write, found
/// by a second thread that keeps trying; "wait" is how long a game-thread change waits while background writes run
/// (each 5 ms) with a blocking or a non-blocking call.
/// </summary>
internal static class RepresentativeBenchmark
{
    private static readonly DateTime Epoch = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private const int Writes = 12;

    public const string Header = "target_mib,profile_bytes,colonies,write,writes,wall_median_ms,wall_p95_ms,cpu_median_ms,"
        + "alloc_median_bytes,gen2_collections,hold_median_ms,hold_max_ms,read_bytes_per_write,written_bytes_per_write,"
        + "backup_rotations,blocking_wait_p95_ms,blocking_wait_max_ms,nonblocking_wait_p95_ms,nonblocking_wait_max_ms";

    /// <summary>The three kinds of change a write follows. Each writes the whole profile.</summary>
    private static readonly (string Name, Action<Profile, int> Change)[] Kinds =
    [
        // A save recorded or a file imported: something is added.
        ("statistics_added", (p, i) => p.SaveObservations.Add(new SaveObservation
        {
            FileName = $"benchmark-added-{i:D4}.rws", FileSize = 20_000_000 + i, LastWriteUtc = Epoch.AddDays(1).AddMinutes(i),
            GameVersion = "1.7.5005 rev1082", ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok,
            Values = new ObservedValues { TicksGame = 2_400_000 + i, NumRaidsEnemy = 18 },
        })),
        // One counter goes up without the file growing (e.g. 18 -> 19 raids).
        ("statistics_same_size", (p, i) =>
        {
            var values = p.SaveObservations[i % 8].Values;
            values.NumRaidsEnemy = values.NumRaidsEnemy % 10 == 9 ? values.NumRaidsEnemy - 1 : values.NumRaidsEnemy + 1;
        }),
        // Only the scan time: no backup rotation.
        ("import_state_only", (p, i) => p.ImportState.LastScanUtc = Epoch.AddSeconds(i + 1)),
    ];

    public static void Run(double mib)
    {
        var profile = Build((long)(mib * 1024 * 1024));
        var directory = Directory.CreateTempSubdirectory("rws-profile-representative-");
        try
        {
            var store = new ProfileStore(directory.FullName);
            _ = store.Load(Epoch);
            store.Save(profile);
            var bytes = new FileInfo(store.ProfilePath).Length;
            // Each of these opens and closes its own session on the file, so they run before the measured one opens.
            var blocking = Contend(directory.FullName, mib, nonBlocking: false);
            var nonBlocking = Contend(directory.FullName, mib, nonBlocking: true);
            var session = new ProfileSession(new ProfileStore(directory.FullName), () => Epoch);
            var k = 0;
            for (var i = 0; i < 4; i++)   // warm up both buffers and the JIT on every path
                foreach (var (_, change) in Kinds)
                    session.Mutate(p => change(p, k++), SaveMode.Immediate);
            foreach (var (name, change) in Kinds)
            {
                var (wall, cpu, alloc, gen2, rotations) = MeasureWrites(session, store.ProfilePath, change, ref k);
                var hold = MeasureHold(session, change, ref k);
                if (session.LastSaveError is { } error)
                    throw new InvalidOperationException("A write failed.", error);
                var size = new FileInfo(store.ProfilePath).Length;
                Console.WriteLine(string.Join(",",
                    mib.ToString("0.##", CultureInfo.InvariantCulture), bytes, profile.LiveCampaigns.Count, name, Writes,
                    F(Percentile(wall, 0.5)), F(Percentile(wall, 0.95)), F(Percentile(cpu, 0.5)),
                    alloc.Order().ElementAt(alloc.Count / 2), gen2, F(Percentile(hold, 0.5)), F(hold.Max()),
                    size, size, rotations,
                    F(Percentile(blocking, 0.95)), F(blocking.Max()), F(Percentile(nonBlocking, 0.95)), F(nonBlocking.Max())));
            }
            session.Close();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>Writes on this thread (Immediate): wall and CPU time, allocations of all threads, gen2 collections.</summary>
    private static (List<double> Wall, List<double> Cpu, List<long> Alloc, int Gen2, int Rotations) MeasureWrites(
        ProfileSession session, string path, Action<Profile, int> change, ref int k)
    {
        var wall = new List<double>();
        var cpu = new List<double>();
        var alloc = new List<long>();
        var rotations = 0;
        Settle();
        var gen2 = GC.CollectionCount(2);
        var process = Process.GetCurrentProcess();
        for (var i = 0; i < Writes; i++)
        {
            var n = k++;
            var bak1 = File.GetLastWriteTimeUtc(path + ".bak1");
            process.Refresh();
            var cpu0 = process.TotalProcessorTime;
            var alloc0 = GC.GetTotalAllocatedBytes(precise: true);
            var start = Stopwatch.GetTimestamp();
            session.Mutate(p => change(p, n), SaveMode.Immediate);
            wall.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            alloc.Add(GC.GetTotalAllocatedBytes(precise: true) - alloc0);
            process.Refresh();
            cpu.Add((process.TotalProcessorTime - cpu0).TotalMilliseconds);
            if (File.GetLastWriteTimeUtc(path + ".bak1") != bak1)
                rotations++;
        }
        return (wall, cpu, alloc, GC.CollectionCount(2) - gen2, rotations);
    }

    /// <summary>The longest time, per write, that another thread trying again and again could not get the profile.</summary>
    private static List<double> MeasureHold(ProfileSession session, Action<Profile, int> change, ref int k)
    {
        var holds = new List<double>();
        for (var i = 0; i < Writes; i++)
        {
            var n = k++;
            var running = 1;
            var longest = 0L;
            var probe = new Thread(() =>
            {
                long? since = null;
                while (Volatile.Read(ref running) == 1)
                {
                    var now = Stopwatch.GetTimestamp();
                    if (session.TryRead(_ => 0, out _))
                    {
                        if (since is { } s)
                            longest = Math.Max(longest, now - s);
                        since = null;
                    }
                    else
                    {
                        since ??= now;
                    }
                }
            });
            probe.Start();
            Thread.Sleep(2);
            session.Mutate(p => change(p, n), SaveMode.Immediate);
            Volatile.Write(ref running, 0);
            probe.Join();
            holds.Add(longest * 1000.0 / Stopwatch.Frequency);
        }
        return holds;
    }

    private static List<double> Contend(string directory, double mib, bool nonBlocking)
    {
        Settle();
        var session = new ProfileSession(new ProfileStore(directory), () => Epoch);
        var stop = DateTime.UtcNow.AddSeconds(mib < 1 ? 3 : 8);
        var writer = new Thread(() =>
        {
            var k = 0;
            while (DateTime.UtcNow < stop)
            {
                session.Mutate(p => p.ImportState.LastScanUtc = Epoch.AddSeconds(++k), SaveMode.Background);
                Thread.Sleep(5);
            }
        });
        writer.Start();
        var waits = new List<double>();
        while (DateTime.UtcNow < stop)
        {
            var start = Stopwatch.GetTimestamp();
            if (nonBlocking)
                session.MutateWithoutBlocking(p => p.SaveObservations[^1].Values.ColonistsKilled++);
            else
                session.Mutate(p => p.SaveObservations[^1].Values.ColonistsKilled++, SaveMode.Background);
            waits.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            Thread.Sleep(20);
        }
        writer.Join();
        session.Close();
        if (session.LastQueuedMutationError is { } error)
            throw new InvalidOperationException("A queued change failed.", error);
        return waits;
    }

    private static Profile Build(long targetBytes)
    {
        var saveDefinitions = MetricRegistry.BaseCatalog.Where(d => d.Source == MetricObservationSource.SaveFile).ToArray();
        var liveDefinitions = LiveCatalog.Definitions.ToArray();
        var profile = new Profile();
        for (var colony = 0; ; colony++)
        {
            AddColony(profile, colony, saveDefinitions, liveDefinitions);
            if (ProfileJson.SerializeToUtf8Bytes(profile).LongLength >= targetBytes)
                return profile;
        }
    }

    private static void AddColony(Profile profile, int colony, MetricDefinition[] saveDefinitions, MetricDefinition[] liveDefinitions)
    {
        var campaign = "benchmark-campaign-" + colony.ToString("D4", CultureInfo.InvariantCulture);
        var identity = new ColonyIdentity
        {
            ModCampaignId = campaign, SeedString = "benchmark-seed-" + colony, StartingTile = colony + ",0",
            PersistentRandomValue = colony + 1, FirstWealthBits = (uint)(170 + colony),
            StartingPawnIds = { "Thing_Human" + (1000 + colony), "Thing_Human" + (2000 + colony) },
        };
        string? parent = null;
        for (var save = 0; save < 8; save++)
        {
            var tick = 300000 * (save + 1);
            var file = $"benchmark-colony-{colony:D4}-{save}.rws";
            var utc = Epoch.AddHours(colony).AddMinutes(save);
            var alias = new MetricFileAlias { FileName = file, FileSize = 20_000_000 + save, LastWriteUtc = utc };
            profile.SaveObservations.Add(new SaveObservation
            {
                FileName = file, FileSize = alias.FileSize, LastWriteUtc = utc, GameVersion = "1.7.5005 rev1082",
                ReaderVersion = SaveReader.Version, Status = ObservationStatus.Ok, Identity = identity.Clone(), ModCount = 191,
                Values = new ObservedValues { TicksGame = tick, NumRaidsEnemy = save, WealthLast = 1000 + save * 100, WealthMax = 1000 + save * 100, WealthPoints = 10 },
                Display = new DisplayInfo { FactionName = "Benchmark colony " + colony, WorldName = "Benchmark world" },
            });
            var observation = new MetricObservation
            {
                Source = MetricObservationSource.SaveFile, FormatVersion = SaveReader.Version, Identity = identity.Clone(),
                Key = ((long)colony * 100 + save).ToString("x16", CultureInfo.InvariantCulture).PadLeft(64, '0'),
                GameVersion = "1.7.5005 rev1082", SaveTick = tick, ObservedUtc = utc, FileAliases = { alias },
                ActiveDlcIds = ["ludeon.rimworld.biotech", "ludeon.rimworld.ideology", "ludeon.rimworld.royalty"],
                ModSetHash = new string('a', 64),
            };
            foreach (var definition in saveDefinitions)
                observation.Metrics[definition.StoredKey] = Entry(definition, save * 3 + 1, 3);
            profile.MetricObservations.Add(observation);

            var key = $"benchmark-checkpoint-{colony:D4}-{save}";
            var checkpoint = new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint, FormatVersion = 1, Key = key, ParentKey = parent,
                PayloadHash = new string('b', 64), Identity = identity.Clone(), GameVersion = "1.7.5005 rev1082", SaveTick = tick,
                ObservedUtc = utc, CheckpointMetadata = CheckpointMetadataEvidence.Confirmed, ModSetHash = new string('a', 64),
                FileAliases = { new MetricFileAlias { FileName = file, FileSize = alias.FileSize, LastWriteUtc = utc } },
            };
            if (save == 7)
                foreach (var definition in liveDefinitions)
                    checkpoint.Metrics[definition.StoredKey] = Entry(definition, save * 7 + 2, 12);
            else
            {
                checkpoint.Compressed = true;
                checkpoint.ArchivedMetrics = true;
            }
            profile.MetricObservations.Add(checkpoint);
            parent = key;
        }
        ProfileMutations.ApplyLiveObservation(profile, campaign, identity, new ObservedValues { TicksGame = 300000 * 8 },
            new DisplayInfo { FactionName = "Benchmark colony " + colony }, Epoch.AddHours(colony));
    }

    private static MetricEntry Entry(MetricDefinition definition, long number, int breakdowns)
    {
        var entry = MetricEntry.Of(definition.Revision, new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = number });
        if (definition.HasBreakdown)
            for (var b = 0; b < breakdowns; b++)
                entry.Values["Benchmark_Def_" + b.ToString(CultureInfo.InvariantCulture)] =
                    new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = number + b };
        return entry;
    }

    private static void Settle()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static double Percentile(List<double> samples, double fraction)
    {
        var sorted = samples.Order().ToArray();
        return sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * fraction) - 1)];
    }

    private static string F(double n) => n.ToString("F2", CultureInfo.InvariantCulture);
}
