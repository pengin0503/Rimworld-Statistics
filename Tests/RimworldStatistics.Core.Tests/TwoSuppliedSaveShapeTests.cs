using System.Diagnostics;
using System.Security.Cryptography;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Privacy-safe synthetic regression cases based on the structural *shapes* of two voluntarily
/// supplied RimWorld 1.7 saves. Never commit the originals, player identifiers, map data or
/// actual history timelines. The optional real-file test reads copies locally only.
/// </summary>
public sealed class TwoSuppliedSaveShapeTests
{
    private sealed record Shape(
        string Version, int ModCount, int Ticks, int Raids, int? Deaths,
        int GreatestPopulation, int Points, int QuestCount, bool HasStatisticsComponent);

    private static readonly Shape WithStatistics = new(
        "1.7.5000 rev1275", 183, 8_037_368, 12, null, 8, 268, 59, true);

    private static readonly Shape BeforeStatistics = new(
        "1.7.4995 rev309", 348, 12_818_036, 16, 2, 26, 428, 102, false);

    private static readonly string[] DlcIds =
    {
        "ludeon.rimworld.royalty", "ludeon.rimworld.ideology",
        "ludeon.rimworld.biotech", "ludeon.rimworld.odyssey",
    };

    private static byte[] Synthetic(Shape shape)
    {
        // Preserve the reader-visible RimWorld structure, version, mod count,
        // vanilla counters, recorder cadence and optional GameComponent shape,
        // but generate *new* mod IDs, starting identity, quest data and timelines.
        var ids = Enumerable.Range(0, shape.ModCount).Select(i => "example.sanitized.mod" + i).ToArray();
        for (var i = 0; i < DlcIds.Length; i++)
            ids[i] = DlcIds[i];

        var values = Enumerable.Range(0, shape.Points).Select(i => 10_000f + 25f * i).ToArray();
        var pawns = Enumerable.Range(0, shape.Points).Select(i => (float)(3 + i % 7)).ToArray();
        var b = new SaveXmlBuilder()
            .MetaMods(shape.Version, ids)
            .Info(realPlay: 1_000, tile: shape.HasStatisticsComponent ? "100" : "200", pawns: "Thing_Test1")
            .Tick(ticksGame: shape.Ticks, start: shape.HasStatisticsComponent ? 70_000 : 1_570_000)
            .World(seed: shape.HasStatisticsComponent ? "public-fixture-a" : "public-fixture-b", prv: 42)
            .Stats(raids: shape.Raids, killed: shape.Deaths, greatestPop: shape.GreatestPopulation)
            .WealthTotalDeflate(values)
            .Recorder("Wealth_Items", values.Select(x => x * 0.6f).ToArray(), deflate: true)
            .Recorder("Wealth_Buildings", values.Select(x => x * 0.3f).ToArray(), deflate: true)
            .Recorder("Wealth_Pawns", values.Select(x => x * 0.1f).ToArray(), deflate: true)
            .Recorder("FreeColonists", pawns, deflate: true)
            .Recorder("Prisoners", Enumerable.Range(0, shape.Points).Select(i => (float)(i % 3)).ToArray(), deflate: true)
            .Recorder("ColonistMood", Enumerable.Range(0, shape.Points).Select(i => (float)(55 + i % 25)).ToArray(), deflate: true)
            .Maps(50_000).Unknown(25_000);

        for (var i = 1; i <= shape.QuestCount; i++)
            b.QuestFull(i, "Synthetic_TestQuest", acceptanceTick: i * 100,
                ended: i % 3 == 0, outcome: i % 3 == 0 ? "Success" : null,
                cleanupTick: i % 3 == 0 ? i * 100 + 1000 : -1);

        if (shape.HasStatisticsComponent)
            b.Component(SaveReader.ComponentClassName,
                new XElement("campaignId", "abcdefabcdefabcdefabcdefabcdefab"),
                new XElement("lineageEndings"));

        return b.Build();
    }

    private static SaveReadResult Read(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        return new SaveReader().ReadDetailed(input, "anonymous.rws", bytes.Length, DateTime.UnixEpoch);
    }

    private static void AssertShape(SaveReadResult result, Shape shape)
    {
        var obs = result.Observation;
        Assert.Equal(ObservationStatus.Ok, obs.Status);
        Assert.Null(obs.ErrorReason);
        Assert.Equal(shape.Version, obs.GameVersion);
        Assert.Equal(shape.ModCount, obs.ModCount);
        Assert.Equal(shape.Ticks, obs.Values.TicksGame);
        Assert.Equal(shape.Raids, obs.Values.NumRaidsEnemy);
        Assert.Equal(shape.Deaths ?? 0, obs.Values.ColonistsKilled);
        Assert.Equal(shape.GreatestPopulation, obs.Values.GreatestPopulation);
        Assert.Equal(shape.Points, obs.Values.WealthPoints);
        Assert.Equal(shape.HasStatisticsComponent, obs.Identity.ModCampaignId is not null);
        Assert.Null(result.Checkpoint); // A campaignId by itself is NOT a numeric checkpoint.
        Assert.NotNull(result.Metrics);
        Assert.Equal(MetricObservationSource.SaveFile, result.Metrics.Source);
        Assert.Equal(shape.Ticks, result.Metrics.SaveTick);
        Assert.Equal(DlcIds.Order(StringComparer.Ordinal), result.Metrics.ActiveDlcIds);
        Assert.Equal((long)shape.ModCount,
            result.Metrics.Scalar(MetricKeys.ModsLatest, MetricKeys.Revision(MetricKeys.ModsLatest))?.IntegerValue);
        foreach (var key in new[]
        {
            MetricKeys.WealthTotalMean, MetricKeys.WealthItemsLatest, MetricKeys.WealthBuildingsLatest,
            MetricKeys.WealthPawnsLatest, MetricKeys.FreeColonistsMean,
            MetricKeys.PrisonersPeak, MetricKeys.MoodMean, MetricKeys.QuestAccepted,
        })
            Assert.True(result.Metrics.Metrics.ContainsKey(key), "Missing history/quest metric: " + key);
    }

    [Fact]
    public void AnonymousShapes_PreserveOldAndPostInstallSaveReading()
    {
        AssertShape(Read(Synthetic(WithStatistics)), WithStatistics);
        AssertShape(Read(Synthetic(BeforeStatistics)), BeforeStatistics);
    }

    [Fact]
    public void AnonymousShapes_ImportBothThenSkipUnchanged()
    {
        using var saves = new TempDir();
        using var profile = new TempDir();
        File.WriteAllBytes(saves.File("a.rws"), Synthetic(WithStatistics));
        File.WriteAllBytes(saves.File("b.rws"), Synthetic(BeforeStatistics));
        var session = new ProfileSession(new ProfileStore(profile.Path), () => DateTime.UtcNow);
        var reader = new ImportEngine(new SaveReader());
        var first = reader.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Assert.Equal((2, 0, 0), (first.Read, first.Skipped, first.Failed));
        Assert.Equal(2, session.GetView(new StatsOptions()).Stats.ColonyCount);
        var second = reader.Run(saves.Path, session, null, TestContext.Current.CancellationToken);
        Assert.Equal((0, 2, 0), (second.Read, second.Skipped, second.Failed));
        session.Flush();
        Assert.Equal(2, new ProfileStore(profile.Path).Load(DateTime.UtcNow).Profile.SaveObservations.Count);
    }

    [Fact]
    public void ActualSuppliedSaves_ImportAndStayByteIdentical_WhenLocallyAvailable()
    {
        // These files intentionally never enter the repository or a public GitHub Actions runner.
        var installed = Environment.GetEnvironmentVariable("RWSTATS_CASE_INSTALLED_SAVE");
        var legacy = Environment.GetEnvironmentVariable("RWSTATS_CASE_LEGACY_SAVE");
        if (string.IsNullOrWhiteSpace(installed) || string.IsNullOrWhiteSpace(legacy))
            Assert.Skip("Set RWSTATS_CASE_INSTALLED_SAVE and RWSTATS_CASE_LEGACY_SAVE locally to test private originals.");

        Assert.True(File.Exists(installed), "Installed-case file is missing.");
        Assert.True(File.Exists(legacy), "Legacy-case file is missing.");
        static string Hash(string path)
        {
            using var f = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(f));
        }
        var installedHash = Hash(installed);
        var legacyHash = Hash(legacy);
        var timer = Stopwatch.StartNew();
        var a = new SaveReader().ReadDetailed(installed);
        var b = new SaveReader().ReadDetailed(legacy);
        timer.Stop();

        Assert.Equal(ObservationStatus.Ok, a.Observation.Status);
        Assert.Equal(ObservationStatus.Ok, b.Observation.Status);
        Assert.NotNull(a.Metrics);
        Assert.NotNull(b.Metrics);
        Assert.Equal(WithStatistics.Version, a.Observation.GameVersion);
        Assert.Equal(BeforeStatistics.Version, b.Observation.GameVersion);
        Assert.Equal((WithStatistics.ModCount, WithStatistics.Ticks, WithStatistics.Points),
            (a.Observation.ModCount, a.Observation.Values.TicksGame, a.Observation.Values.WealthPoints));
        Assert.Equal((BeforeStatistics.ModCount, BeforeStatistics.Ticks, BeforeStatistics.Points),
            (b.Observation.ModCount, b.Observation.Values.TicksGame, b.Observation.Values.WealthPoints));
        Assert.Equal(WithStatistics.Raids, a.Observation.Values.NumRaidsEnemy);
        Assert.Equal(BeforeStatistics.Raids, b.Observation.Values.NumRaidsEnemy);
        Assert.NotNull(a.Observation.Identity.ModCampaignId);
        Assert.Null(b.Observation.Identity.ModCampaignId);
        Assert.Null(a.Checkpoint);
        Assert.Null(b.Checkpoint);
        Assert.Equal(installedHash, Hash(installed));
        Assert.Equal(legacyHash, Hash(legacy));
        TestContext.Current.SendDiagnosticMessage(
            $"Private two-save read: {timer.Elapsed.TotalMilliseconds:F0}ms; both unchanged; no names/IDs logged.");
    }
}
