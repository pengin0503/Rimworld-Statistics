using System.Security.Cryptography;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Opt-in acceptance of the per-colony history lines against the six privately supplied 1.7.5005 originals
/// (docs/research/2026-11-14-additional-investigation-ja.md §1). Set RWS_PRIVATE_SIX_SAVES_DIR to a folder holding
/// exactly those six .rws files. Saves are only read; nothing about their names, ids or contents is asserted or printed.
/// In game-tick order A..F they hold five checkpoints: no proven edge A→B, direct edges B→C→D, and E and F both
/// carry the same older checkpoint whose parent is D's.
/// </summary>
public sealed class PrivateSixSaveChartLineageAcceptanceTests
{
    [Fact]
    public void SuppliedSixOriginals_DrawOnlyProvenSaveLines()
    {
        var folder = Environment.GetEnvironmentVariable("RWS_PRIVATE_SIX_SAVES_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            Assert.Skip("Set RWS_PRIVATE_SIX_SAVES_DIR locally; original saves are never stored in CI.");
        var files = Directory.GetFiles(folder!, "*.rws").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(6, files.Length);
        var before = files.Select(Fingerprint).ToArray();

        using var directory = new TempDir();
        var session = new ProfileSession(new ProfileStore(directory.Path), () => DateTime.UtcNow,
            new FileCheckpointArchive(directory.File("archive")));
        new ImportEngine(new SaveReader()).Run(folder!, session, null, TestContext.Current.CancellationToken);
        var view = session.GetView(new StatsOptions());
        var colony = Assert.Single(view.Colonies, c => c.Counted);
        var ticks = session.Read(p => p.SaveObservations.Select(o => o.Values.TicksGame).Order().ToArray());
        Assert.Equal(6, ticks.Distinct().Count());

        var raids = new MetricDefinition
        {
            Key = BasicMetrics.Raids, StoredKey = BasicMetrics.Raids, LedgerIds = BasicMetrics.Raids,
            Category = MetricCategory.Colony, Unit = MetricUnit.Count, Provenance = MetricProvenance.SaveHistory,
            Overall = OverallAggregation.Sum, LabelKey = BasicMetrics.Raids, DescriptionKey = BasicMetrics.Raids,
        };
        foreach (var definition in new[] { raids, MetricRegistry.Find(MetricKeys.WealthItemsPeak)! })
        {
            var points = session.ReadChartHistory(view, colony.Key, definition, folder!).Points;
            Assert.Equal(ticks, points.Select(p => p.Tick));
            Assert.All(points, p => Assert.NotNull(p.Value));
            // A, then B unproven, B→C→D direct, D→E through E's checkpoint, and F from D, not from E.
            Assert.Equal(new[] { -1, -1, 1, 2, 3, 3 }, points.Select((p, i) => p.Predecessor(i)));
            Assert.Equal(new[] { false, true, false, false, false, false }, points.Select(p => p.Unlinked));
        }

        var checkpoint = session.ReadChartHistory(view, colony.Key, MetricRegistry.Find(ResearchFamily.Completed)!, folder!);
        Assert.Equal(MetricHistoryKind.CheckpointLineage, checkpoint.Kind);
        Assert.Equal(4, checkpoint.Points.Count);
        Assert.Equal(ticks[1..4], checkpoint.Points.Take(3).Select(p => p.Tick));
        Assert.True(checkpoint.Points[3].Tick > ticks[3] && checkpoint.Points[3].Tick < ticks[4]);
        Assert.Equal(new int?[] { null, null, null, ticks[5] }, checkpoint.Points.Select(p => p.SavedTick));

        Assert.Equal(before, files.Select(Fingerprint).ToArray());
    }

    private static (long Length, DateTime Utc, string Sha) Fingerprint(string path)
    {
        var info = new FileInfo(path);
        using var file = File.OpenRead(path);
        return (info.Length, info.LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(file)));
    }
}
