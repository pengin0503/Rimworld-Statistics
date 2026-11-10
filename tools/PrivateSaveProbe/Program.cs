using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RimworldStatistics.Core;

namespace RimworldStatistics.PrivateSaveProbe;

/// <summary>
/// Isolated .NET runner for the two private original RimWorld save files.
/// No game assemblies, images or live game process are required.
/// Never modifies source files or prints file paths, hashes, names or identifiers.
/// </summary>
internal static class Program
{
    private sealed record Expected(
        string Label, string GameVersion, int ModCount, int Ticks, int Raids,
        int Deaths, int Population, int WealthPoints, bool HasOwnComponent,
        long QuestAccepted, long QuestSuccess, long QuestFailure, long QuestExpired,
        double WealthLatest, double WealthPeak, double WealthMean);

    private static readonly Expected Installed = new(
        "installed", "1.7.5000 rev1275", 183, 8_037_368, 12, 0, 8, 268, true,
        34, 11, 15, 17, 117528.0234375, 117741.890625, 63932.962303958724);

    private static readonly Expected Legacy = new(
        "legacy", "1.7.4995 rev309", 348, 12_818_036, 16, 2, 26, 428, false,
        68, 14, 42, 26, 268320.1875, 268320.1875, 93937.6574958017);

    private static void Require(bool condition, string reason)
    {
        if (!condition)
            throw new InvalidOperationException(reason);
    }

    private static string HashFile(string path)
    {
        using var input = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    private static long? Integer(MetricObservation observation, string key)
        => observation.Scalar(key, MetricKeys.Revision(key))?.IntegerValue;

    private static double? Real(MetricObservation observation, string key)
        => observation.Scalar(key, MetricKeys.Revision(key))?.RealValue;

    private static object ReadCase(string path, Expected expected)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new SaveReader().ReadDetailed(path);
        stopwatch.Stop();
        var o = result.Observation;
        Require(o.Status == ObservationStatus.Ok, expected.Label + ": reader status");
        Require(o.ErrorReason is null, expected.Label + ": reader error");
        Require(result.Metrics is not null, expected.Label + ": missing numeric metrics");
        Require(result.Checkpoint is null, expected.Label + ": unexpected checkpoint");
        Require(o.GameVersion == expected.GameVersion, expected.Label + ": game version");
        Require(o.ModCount == expected.ModCount, expected.Label + ": MOD count");
        Require(o.Values.TicksGame == expected.Ticks, expected.Label + ": ticks");
        Require(o.Values.NumRaidsEnemy == expected.Raids, expected.Label + ": raids");
        Require(o.Values.ColonistsKilled == expected.Deaths, expected.Label + ": deaths");
        Require(o.Values.GreatestPopulation == expected.Population, expected.Label + ": population");
        Require(o.Values.WealthPoints == expected.WealthPoints, expected.Label + ": wealth samples");
        Require((o.Identity.ModCampaignId is not null) == expected.HasOwnComponent,
            expected.Label + ": Statistics component presence");
        Require(o.Values.WealthLast == expected.WealthLatest, expected.Label + ": last wealth");
        Require(o.Values.WealthMax == expected.WealthPeak, expected.Label + ": peak wealth");
        var m = result.Metrics!;
        Require(m.Key == HashFile(path), expected.Label + ": full-file SHA-256 does not match streamed reader");
        var mean = Real(m, MetricKeys.WealthTotalMean);
        Require(mean.HasValue && Math.Abs(mean.Value - expected.WealthMean) <= 0.01,
            expected.Label + ": wealth mean");
        Require(Integer(m, MetricKeys.QuestAccepted) == expected.QuestAccepted,
            expected.Label + ": retained quests accepted");
        Require(Integer(m, MetricKeys.QuestSuccess) == expected.QuestSuccess,
            expected.Label + ": retained quests successful");
        Require(Integer(m, MetricKeys.QuestFailure) == expected.QuestFailure,
            expected.Label + ": retained quests failed");
        Require(Integer(m, MetricKeys.QuestExpired) == expected.QuestExpired,
            expected.Label + ": retained quests expired");
        Require(Integer(m, MetricKeys.ModsLatest) == expected.ModCount,
            expected.Label + ": captured MOD count");
        return new
        {
            caseId = expected.Label, readerPassed = true,
            version = expected.GameVersion, mods = expected.ModCount,
            wealthPoints = expected.WealthPoints,
            wealthLatest = expected.WealthLatest, wealthPeak = expected.WealthPeak,
            meanWealth = Math.Round(mean!.Value, 4),
            questAccepted = expected.QuestAccepted,
            questSuccess = expected.QuestSuccess,
            questFailure = expected.QuestFailure,
            questExpired = expected.QuestExpired,
            readMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1),
        };
    }

    private static object RunImport(string installedPath, string legacyPath)
    {
        var scratch = Path.Combine(Path.GetTempPath(), "rws-private-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var saveDirectory = Path.Combine(scratch, "saves");
            var profileDirectory = Path.Combine(scratch, "profile");
            Directory.CreateDirectory(saveDirectory);
            Directory.CreateDirectory(profileDirectory);
            File.Copy(installedPath, Path.Combine(saveDirectory, "case-a.rws"));
            File.Copy(legacyPath, Path.Combine(saveDirectory, "case-b.rws"));

            var session = new ProfileSession(new ProfileStore(profileDirectory), () => DateTime.UtcNow);
            var importer = new ImportEngine(new SaveReader());
            var watch = Stopwatch.StartNew();
            var first = importer.Run(saveDirectory, session, null, CancellationToken.None);
            watch.Stop();
            Require(first.Read == 2 && first.Failed == 0 && !first.Cancelled,
                "combined import: first scan");
            var count = session.GetView(new StatsOptions()).Stats.ColonyCount;
            Require(count == 2, "combined import: independent colonies");
            var second = importer.Run(saveDirectory, session, null, CancellationToken.None);
            Require(second.Read == 0 && second.Skipped == 2 && second.Failed == 0,
                "combined import: second scan must skip unchanged saves");
            session.Flush();
            var disk = new ProfileStore(profileDirectory).Load(DateTime.UtcNow).Profile;
            Require(disk.SaveObservations.Count == 2, "combined import: persisted save observations");
            Require(disk.MetricObservations.Count == 2, "combined import: persisted numeric observations");
            return new
            {
                firstRead = first.Read, firstFailed = first.Failed,
                colonies = count, numericObservations = disk.MetricObservations.Count,
                secondSkipped = second.Skipped,
                importMilliseconds = Math.Round(watch.Elapsed.TotalMilliseconds, 1),
            };
        }
        finally
        {
            if (Directory.Exists(scratch))
                Directory.Delete(scratch, recursive: true);
        }
    }

    private static byte[] FakeSave(string seed)
    {
        var floats = new byte[8];
        BitConverter.GetBytes(12f).CopyTo(floats, 0);
        BitConverter.GetBytes(13f).CopyTo(floats, 4);
        var records = Convert.ToBase64String(floats);
        var xml = "<savegame><meta><gameVersion>1.7.4985 rev1269</gameVersion>" +
            "<modIds><li>example.fake</li></modIds></meta><game>" +
            "<info><startingTile>100,0</startingTile><startingAndOptionalPawns><li>Fake1</li>" +
            "</startingAndOptionalPawns></info><tickManager><ticksGame>30000</ticksGame>" +
            "<gameStartAbsTick>0</gameStartAbsTick></tickManager>" +
            "<storyWatcher><statsRecord><numRaidsEnemy>1</numRaidsEnemy>" +
            "</statsRecord></storyWatcher><history><autoRecorderGroups><li><recorders>" +
            "<li><def>Wealth_Total</def><records>" + records +
            "</records></li></recorders></li></autoRecorderGroups></history>" +
            "<world><info><seedString>" + seed + "</seedString></info></world>" +
            "</game></savegame>";
        return Encoding.UTF8.GetBytes(xml);
    }

    private static int SelfTest()
    {
        var bytes = FakeSave("fixture-one");
        using var stream = new MemoryStream(bytes);
        var result = new SaveReader().ReadDetailed(stream, "fake.rws", bytes.Length, DateTime.UnixEpoch);
        Require(result.Observation.Status == ObservationStatus.Ok, "synthetic reader");
        Require(result.Metrics is not null, "synthetic metric observation");
        Require(result.Observation.Values.WealthPoints == 2, "synthetic wealth sample count");
        Require(result.Observation.Values.WealthLast == 13d, "synthetic last wealth");
        var scratch = Path.Combine(Path.GetTempPath(), "rws-private-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var src = Path.Combine(scratch, "originals");
            Directory.CreateDirectory(src);
            var a = Path.Combine(src, "a.rws");
            var b = Path.Combine(src, "b.rws");
            File.WriteAllBytes(a, bytes);
            File.WriteAllBytes(b, FakeSave("fixture-two"));
            var imported = RunImport(a, b);
            Console.WriteLine(JsonSerializer.Serialize(new { selfTest = "passed", import = imported }));
            return 0;
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--self-test")
                return SelfTest();
            if (args.Length == 1 && args[0] == "--five-self-test")
                return FiveCheckpointProbe.SelfTest();
            if (args.Length == 6 && args[0] == "--five")
                return FiveCheckpointProbe.Run(args.Skip(1).ToArray());
            if (args.Length != 4 || args[0] != "--installed" || args[2] != "--legacy")
            {
                Console.Error.WriteLine("Usage: PrivateSaveProbe --installed path-A --legacy path-B | --five older.rws 2110.rws 2113.rws 2117.rws later.rws | --five-self-test");
                return 2;
            }
            var installedPath = Path.GetFullPath(args[1]);
            var legacyPath = Path.GetFullPath(args[3]);
            Require(File.Exists(installedPath) && File.Exists(legacyPath), "missing input");
            Require(installedPath != legacyPath, "both inputs refer to the same path");
            var installedBefore = HashFile(installedPath);
            var legacyBefore = HashFile(legacyPath);
            var first = ReadCase(installedPath, Installed);
            var second = ReadCase(legacyPath, Legacy);
            var combined = RunImport(installedPath, legacyPath);
            Require(HashFile(installedPath) == installedBefore && HashFile(legacyPath) == legacyBefore,
                "source saves changed during verification");
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                status = "passed", originalFilesUnchanged = true,
                sourceHashesNeverPrinted = true,
                cases = new[] { first, second }, combinedImport = combined,
                gameRuntimeWasNotLaunched = true,
            }));
            return 0;
        }
        catch (Exception error)
        {
            // Exception messages may include original save paths: never print them.
            Console.Error.WriteLine("PrivateSaveProbe failed: " + error.GetType().Name);
            return 1;
        }
    }
}
