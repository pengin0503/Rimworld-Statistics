using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using RimworldStatistics.Core;

namespace RimworldStatistics.PrivateSaveProbe;

/// <summary>
/// Portable, standalone .NET acceptance for five unmodified private .rws saves.
/// The binary is built on GitHub without original saves and run against them
/// outside GitHub. Error output never contains file paths, IDs or raw hashes.
/// </summary>
internal static class FiveCheckpointProbe
{
    private sealed record Expectation(int Tick, int Raids, int Deaths);
    private static readonly Expectation[] RealExpected =
    {
        new(10_037_861, 15, 1),
        new(10_336_998, 16, 1),
        new(10_381_998, 16, 1),
        new(10_426_998, 16, 1),
        new(10_482_325, 16, 1),
    };

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed record OriginalState(long Length, DateTime LastWriteUtc, byte[] Hash);
    private static byte[] HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return SHA256.HashData(stream);
    }

    private static OriginalState Snapshot(string path)
    {
        var info = new FileInfo(path);
        return new OriginalState(info.Length, info.LastWriteTimeUtc, HashFile(path));
    }

    private static void AssertUnchanged(string path, OriginalState original)
    {
        var info = new FileInfo(path);
        Require(info.Exists && info.Length == original.Length &&
                info.LastWriteTimeUtc == original.LastWriteUtc,
            "input file metadata changed");
        Require(original.Hash.AsSpan().SequenceEqual(HashFile(path)), "input file content changed");
    }

    private static CheckpointPayload ReadEmbeddedPayload(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
        });
        CheckpointPayload? payload = null;
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "numericStatistics")
                continue;
            Require(payload is null, "multiple embedded statistics payloads");
            var raw = reader.ReadElementContentAsString();
            var result = CheckpointJson.Parse(raw);
            Require(result.Status == CheckpointParseStatus.Ok && result.Payload is not null,
                "embedded checkpoint payload unreadable");
            payload = result.Payload;
        }
        Require(payload is not null, "embedded checkpoint missing");
        return payload!;
    }

    private static object RunCore(string[] originals, Expectation[] expected, int modCount,
        bool expectTwentyContinuousFamilies)
    {
        Require(originals.Length == 5 && expected.Length == 5, "requires five saves");
        Require(originals.All(File.Exists), "input save missing");
        var paths = originals.Select(Path.GetFullPath).ToArray();
        Require(paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 5,
            "duplicate input paths");

        var before = paths.Select(Snapshot).ToArray();
        var reader = new SaveReader();
        var readTimer = Stopwatch.StartNew();
        var reads = paths.Select(reader.ReadDetailed).ToArray();
        readTimer.Stop();

        var identity = reads[0].Observation.Identity;
        var campaign = identity.ModCampaignId;
        var fingerprint = identity.FingerprintHash();
        var modDigest = reads[0].Metrics?.ModSetHash;
        Require(!string.IsNullOrWhiteSpace(campaign) && !string.IsNullOrWhiteSpace(fingerprint)
                && modDigest is { Length: 64 }, "missing colony/mod evidence");

        var savedDlc = reads[0].Metrics?.ActiveDlcIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cpKeys = new HashSet<string>(StringComparer.Ordinal);
        var payloads = new List<CheckpointPayload>(5);
        for (var i = 0; i < reads.Length; i++)
        {
            var a = reads[i];
            var v = a.Observation.Values;
            Require(a.Observation.Status == ObservationStatus.Ok, "SaveReader rejected one input");
            Require(a.Observation.GameVersion == "1.7.5000 rev1275", "game version mismatch");
            Require(a.Observation.ModCount == modCount, "MOD count mismatch");
            Require(v.TicksGame == expected[i].Tick, "saved tick mismatch");
            Require(v.NumRaidsEnemy == expected[i].Raids &&
                    v.ColonistsKilled == expected[i].Deaths, "vanilla counter mismatch");
            Require(a.Checkpoint is not null && a.Metrics is not null,
                "missing numeric/embedded checkpoint evidence");
            var cp = a.Checkpoint!;
            var metrics = a.Metrics!;
            Require(cp.Source == MetricObservationSource.Checkpoint
                    && metrics.Source == MetricObservationSource.SaveFile, "wrong source type");
            Require(cp.SaveTick == expected[i].Tick &&
                    metrics.SaveTick == expected[i].Tick, "checkpoint tick mismatch");
            Require(cpKeys.Add(cp.Key), "duplicate checkpoint ID");
            Require(cp.PayloadHash is { Length: 64 }, "missing checkpoint payload hash");
            Require(metrics.ModSetHash == modDigest, "MOD load-order digest changed");
            Require(savedDlc is not null && metrics.ActiveDlcIds is not null &&
                    savedDlc.SetEquals(metrics.ActiveDlcIds), "saved DLC disagreement");
            Require(a.Observation.Identity.ModCampaignId == campaign
                    && a.Observation.Identity.FingerprintHash() == fingerprint,
                "campaign or fingerprint mismatch");
            Require(a.Observation.Identity.StartingPawnIds.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(identity.StartingPawnIds), "starting pawn identifiers differ");
            Require(metrics.Key == Convert.ToHexStringLower(before[i].Hash),
                "streamed SaveReader file digest mismatch");

            var payload = ReadEmbeddedPayload(paths[i]);
            Require(payload.FormatVersion == 1 &&
                    payload.CheckpointId == cp.Key &&
                    payload.ParentCheckpointId == cp.ParentKey &&
                    payload.SaveTick == expected[i].Tick, "checkpoint JSON/header mismatch");
            if (expectTwentyContinuousFamilies)
            {
                Require(payload.Families.Count == 20, "family count mismatch");
                Require(payload.Families.Values.All(f =>
                    !f.IsQuarantined && f.Continuous &&
                    f.Evidence == MetricEvidence.Experimental &&
                    f.ThroughTick == expected[i].Tick),
                    "family parse, coverage or capture continuity mismatch");
            }
            payloads.Add(payload);
        }

        // This is stronger than shared campaign ID, clock order or equal MODs.
        Require(payloads[1].CheckpointId == payloads[2].ParentCheckpointId
                && payloads[2].CheckpointId == payloads[3].ParentCheckpointId,
            "missing directly proven middle ancestry");
        Require(payloads[0].CheckpointId != payloads[1].ParentCheckpointId &&
                payloads[3].CheckpointId != payloads[4].ParentCheckpointId,
            "unexpected outer direct ancestry");

        var profile = new Profile();
        foreach (var a in reads)
        {
            ProfileMutations.UpsertMetricObservation(profile, a.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, a.Metrics!);
        }
        Require(profile.MetricObservations.Count == 10, "observation retention lost");
        var intervals = HistoricalCounterIntervals.Evaluate(profile)
            .OrderBy(v => v.StartTick).ToArray();
        Require(intervals.Length == 2, "unexpected number of valid direct-parent intervals");
        for (var i = 0; i < intervals.Length; i++)
        {
            Require(intervals[i].StartTick == expected[i + 1].Tick
                && intervals[i].EndTick == expected[i + 2].Tick,
                "unexpected interval boundaries");
            Require(intervals[i].RaidsIncrease == 0
                && intervals[i].ColonistDeathsIncrease == 0
                && intervals[i].Coverage == MetricCoverage.Partial,
                "counter interval wrongly treated as event/lifetime total");
        }

        foreach (var a in reads)
        {
            ProfileMutations.UpsertMetricObservation(profile, a.Checkpoint!);
            ProfileMutations.UpsertMetricObservation(profile, a.Metrics!);
        }
        Require(profile.MetricObservations.Count == 10 &&
                HistoricalCounterIntervals.Evaluate(profile).Count == 2,
            "duplicate observations after repeated scan");

        // Exercise ImportEngine and durable ProfileStore on disposable copies,
        // never the originals or a real player's profile.
        var scratch = Path.Combine(Path.GetTempPath(), "rws-five-acceptance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        double coldMs, warmMs;
        try
        {
            var saves = Path.Combine(scratch, "saves");
            var profiles = Path.Combine(scratch, "profiles");
            Directory.CreateDirectory(saves);
            Directory.CreateDirectory(profiles);
            for (var i = 0; i < paths.Length; i++)
                File.Copy(paths[i], Path.Combine(saves, "case-" + i + ".rws"));
            var session = new ProfileSession(new ProfileStore(profiles), () => DateTime.UtcNow);
            var importer = new ImportEngine(new SaveReader());

            var clock = Stopwatch.StartNew();
            var first = importer.Run(saves, session, null, CancellationToken.None);
            coldMs = clock.Elapsed.TotalMilliseconds;
            Require(first.Read == 5 && first.Failed == 0 && !first.Cancelled,
                "cold five-save import failed");
            clock.Restart();
            var again = importer.Run(saves, session, null, CancellationToken.None);
            warmMs = clock.Elapsed.TotalMilliseconds;
            Require(again.Read == 0 && again.Skipped == 5 && again.Failed == 0,
                "unchanged save archive was not skipped on repeat scan");
            session.Flush();

            var persisted = new ProfileStore(profiles).Load(DateTime.UtcNow).Profile;
            Require(persisted.MetricObservations.Count == 10, "persisted observations lost");
            Require(HistoricalCounterIntervals.Evaluate(persisted).Count == 2,
                "persisted interval ancestry changed");
        }
        finally
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }

        for (var i = 0; i < paths.Length; i++)
            AssertUnchanged(paths[i], before[i]);

        return new
        {
            status = "passed",
            originalFilesUnchanged = true,
            sourceHashesNeverPrinted = true,
            saveCount = 5,
            checkpointCount = 5,
            compatibleSavedModCount = modCount,
            provenDirectParentIntervals = 2,
            unprovenOuterPairs = 2,
            verifiedRaidAndDeathIncreases = 0,
            retainedObservations = profile.MetricObservations.Count,
            directReadMilliseconds = Math.Round(readTimer.Elapsed.TotalMilliseconds, 1),
            coldImportMilliseconds = Math.Round(coldMs, 1),
            warmImportMilliseconds = Math.Round(warmMs, 1),
            gameRuntimeWasNotLaunched = true,
        };
    }

    private static byte[] SyntheticSave(int tick, int raids, string id, string parentId)
    {
        var checkpoint = new CheckpointPayload
        {
            CheckpointId = id, ParentCheckpointId = parentId, SaveTick = tick,
            SavedUtc = DateTime.UnixEpoch,
            Families = new Dictionary<string, FamilyState>(StringComparer.Ordinal)
            {
                ["work"] = new FamilyState
                {
                    SinceTick = 1, ThroughTick = tick, Continuous = true,
                    Evidence = MetricEvidence.Experimental,
                    Metrics = new Dictionary<string, Accumulator>(StringComparer.Ordinal)
                    {
                        ["jobs"] = new Accumulator { N = tick / 100 },
                    },
                },
            },
        };

        var values = new byte[8];
        BitConverter.GetBytes(1000f).CopyTo(values, 0);
        BitConverter.GetBytes(1100f).CopyTo(values, 4);
        var xml = new XElement("savegame",
            new XElement("meta",
                new XElement("gameVersion", "1.7.5000 rev1275"),
                new XElement("modIds", new XElement("li", "ludeon.rimworld"),
                    new XElement("li", "ludeon.rimworld.royalty"),
                    new XElement("li", "test.fixture.mod"))),
            new XElement("game",
                new XElement("info",
                    new XElement("startingTile", "100,0"),
                    new XElement("startingAndOptionalPawns", new XElement("li", "FixturePawn"))),
                new XElement("tickManager",
                    new XElement("ticksGame", tick),
                    new XElement("gameStartAbsTick", 0)),
                new XElement("storyWatcher",
                    new XElement("statsRecord",
                        new XElement("numRaidsEnemy", raids),
                        new XElement("colonistsKilled", 1))),
                new XElement("history",
                    new XElement("autoRecorderGroups",
                        new XElement("li", new XElement("recorders",
                            new XElement("li", new XElement("def", "Wealth_Total"),
                                new XElement("records", Convert.ToBase64String(values))))))),
                new XElement("world", new XElement("info",
                    new XElement("seedString", "fixture-seed"),
                    new XElement("persistentRandomValue", 1))),
                new XElement("components", new XElement("li",
                    new XAttribute("Class", SaveReader.ComponentClassName),
                    new XElement("campaignId", "fixture-campaign"),
                    new XElement("numericStatistics", CheckpointJson.Serialize(checkpoint))))));
        return Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
    }

    public static int SelfTest()
    {
        var root = Path.Combine(Path.GetTempPath(), "rws-five-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ticks = new[] { 60000, 120000, 180000, 240000, 300000 };
            var raids = new[] { 2, 3, 3, 3, 3 };
            var ids = new[] { "synthetic-a", "synthetic-b", "synthetic-c", "synthetic-d", "synthetic-e" };
            var parents = new[] { "missing-a", "missing-b", ids[1], ids[2], "missing-e" };
            var paths = new string[5];
            for (var i = 0; i < 5; i++)
            {
                paths[i] = Path.Combine(root, "fixture-" + i + ".rws");
                File.WriteAllBytes(paths[i], SyntheticSave(ticks[i], raids[i], ids[i], parents[i]));
            }
            var expectations = ticks.Select((tick, i) => new Expectation(tick, raids[i], 1)).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                selfTest = "passed",
                result = RunCore(paths, expectations, 3, false),
            }));
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("FiveCheckpointProbe self-test failed: " + e.GetType().Name);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    public static int Run(string[] files)
    {
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(RunCore(files, RealExpected, 192, true)));
            return 0;
        }
        catch (Exception e)
        {
            // Never print exceptions that may contain absolute private paths.
            Console.Error.WriteLine("FiveCheckpointProbe failed: " + e.GetType().Name);
            return 1;
        }
    }
}
