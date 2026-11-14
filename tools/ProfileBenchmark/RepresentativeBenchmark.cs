using System.Diagnostics;
using System.Globalization;
using RimworldStatistics.Core;

/// <summary>
/// Issue #195 measurements on profiles shaped like imported real ones (a read-only import of six private 1.7 saves gave
/// ~51 KB per checkpoint tip and ~8.8 KB per save-file observation). Synthetic data only; machine-dependent timings,
/// no thresholds. "Locked" is the serialization a background write does while it holds the profile; "wait" is how long a
/// game-thread change waits while background writes run (each 5 ms) with a blocking or a non-blocking call.
/// </summary>
internal static class RepresentativeBenchmark
{
    private static readonly DateTime Epoch = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    public static void Run(double mib)
    {
        var profile = Build((long)(mib * 1024 * 1024));
        var directory = Directory.CreateTempSubdirectory("rws-profile-representative-");
        try
        {
            var store = new ProfileStore(directory.FullName);
            _ = store.Load(Epoch);
            store.SaveSerialized(ProfileJson.SerializeToUtf8Bytes(profile));
            var bytes = new FileInfo(store.ProfilePath).Length;
            var loaded = profile;

            var serialize = new List<double>();
            var serializeAlloc = new List<long>();
            var statistics = new List<double>();
            var importOnly = new List<double>();
            for (var i = 0; i < 16; i++)
            {
                Settle();
                var allocated = GC.GetAllocatedBytesForCurrentThread();
                var start = Stopwatch.GetTimestamp();
                loaded.SaveObservations[^1].Values.NumRaidsEnemy++;
                var json = ProfileJson.SerializeToUtf8Bytes(loaded);
                var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                var used = GC.GetAllocatedBytesForCurrentThread() - allocated;
                Settle();
                start = Stopwatch.GetTimestamp();
                store.SaveSerialized(json);   // a statistics change: compared, written, three backups rotated
                var commit = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                loaded.ImportState.LastScanUtc = Epoch.AddSeconds(i + 1);
                json = ProfileJson.SerializeToUtf8Bytes(loaded);
                Settle();
                start = Stopwatch.GetTimestamp();
                store.SaveSerialized(json);   // an import-state-only change: no backup rotation
                var scan = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                if (i < 4)
                    continue;
                serialize.Add(elapsed);
                serializeAlloc.Add(used);
                statistics.Add(commit);
                importOnly.Add(scan);
            }
            if (!File.Exists(store.ProfilePath + ".bak3"))
                throw new InvalidOperationException("Statistics commits did not rotate three backups.");

            var blocking = Contend(directory.FullName, mib, nonBlocking: false);
            var nonBlocking = Contend(directory.FullName, mib, nonBlocking: true);
            Console.WriteLine(string.Join(",",
                mib.ToString("0.##", CultureInfo.InvariantCulture), bytes, profile.LiveCampaigns.Count,
                F(Percentile(serialize, 0.5)), F(Percentile(serialize, 0.95)), serializeAlloc.Order().ElementAt(serializeAlloc.Count / 2),
                F(Percentile(statistics, 0.5)), F(Percentile(statistics, 0.95)), F(Percentile(importOnly, 0.5)),
                bytes, bytes,
                F(Percentile(blocking, 0.95)), F(blocking.Max()), F(Percentile(nonBlocking, 0.95)), F(nonBlocking.Max())));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
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
