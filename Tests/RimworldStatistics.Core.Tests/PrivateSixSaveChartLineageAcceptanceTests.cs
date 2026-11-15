using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
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
    private static readonly MetricDefinition Raids = new()
    {
        Key = BasicMetrics.Raids, StoredKey = BasicMetrics.Raids, LedgerIds = BasicMetrics.Raids,
        Category = MetricCategory.Colony, Unit = MetricUnit.Count, Provenance = MetricProvenance.SaveHistory,
        Overall = OverallAggregation.Sum, LabelKey = BasicMetrics.Raids, DescriptionKey = BasicMetrics.Raids,
    };

    private static readonly MetricDefinition SnapshotMetric = MetricRegistry.Find(ResearchStateSnapshot.Finished)!;

    [Fact]
    public void SuppliedSixOriginals_DrawOnlyProvenSaveLines()
    {
        var files = Originals();
        var before = files.Select(Fingerprint).ToArray();

        var folder = Path.GetDirectoryName(files[0])!;
        using var directory = new TempDir();
        var session = Import(directory, folder);
        var view = session.GetView(new StatsOptions());
        var colony = Assert.Single(view.Colonies, c => c.Counted);
        var ticks = session.Read(p => p.SaveObservations.Select(o => o.Values.TicksGame).Order().ToArray());
        Assert.Equal(6, ticks.Distinct().Count());

        foreach (var definition in new[] { Raids, MetricRegistry.Find(MetricKeys.WealthItemsPeak)! })
        {
            var points = session.ReadChartHistory(view, colony.Key, definition, folder).Points;
            Assert.Equal(ticks, points.Select(p => p.Tick));
            Assert.All(points, p => Assert.NotNull(p.Value));
            // A, then B unproven, B→C→D direct, D→E through E's checkpoint, and F from D, not from E.
            Assert.Equal(new[] { -1, -1, 1, 2, 3, 3 }, points.Select((p, i) => p.Predecessor(i)));
            Assert.Equal(new[] { false, true, false, false, false, false }, points.Select(p => p.Unlinked));
            // F skips E, which only carried the same older checkpoint: E may be F's ancestor, so this is no proven branch.
            Assert.Equal(3, points[5].ContinuesFrom);
            Assert.All(points, p => Assert.False(p.SkipsOnlyOtherBranches));
        }

        var checkpoint = session.ReadChartHistory(view, colony.Key, MetricRegistry.Find(ResearchFamily.Completed)!, folder);
        Assert.Equal(MetricHistoryKind.CheckpointLineage, checkpoint.Kind);
        Assert.Equal(4, checkpoint.Points.Count);
        Assert.Equal(ticks[1..4], checkpoint.Points.Take(3).Select(p => p.Tick));
        Assert.True(checkpoint.Points[3].Tick > ticks[3] && checkpoint.Points[3].Tick < ticks[4]);
        Assert.Equal(new int?[] { null, null, null, ticks[5] }, checkpoint.Points.Select(p => p.SavedTick));

        Assert.Equal(before, files.Select(Fingerprint).ToArray());
    }

    /// <summary>
    /// Game-side snapshots recorded before checkpoint keys were stored, as the six saves would have produced them: the
    /// snapshot chart must place each save where its imported file does, including E and F, which carried an older
    /// checkpoint. The profile is re-read from disk first, so the file write times have made a round trip.
    /// </summary>
    [Fact]
    public void SuppliedSixOriginals_OlderGameSideSnapshotsLinkExactlyLikeTheirFiles()
    {
        var files = Originals();
        var before = files.Select(Fingerprint).ToArray();

        using var directory = new TempDir();
        var imported = Import(directory, Path.GetDirectoryName(files[0])!);
        var saves = imported.Read(p => p.MetricObservations.Where(o => o.Source == MetricObservationSource.SaveFile)
            .Select(o => (o.SaveTick, Identity: o.Identity.Clone(), Files: o.FileAliases.Select(a => a.FileName).ToArray())).ToArray());
        imported.Mutate(p =>
        {
            foreach (var path in files)
            {
                var save = saves.Single(s => s.Files.Contains(Path.GetFileName(path)));
                p.MetricObservations.Add(new MetricObservation
                {
                    Source = MetricObservationSource.SaveSnapshot, FormatVersion = 1, Key = "legacy-" + save.SaveTick,
                    Identity = save.Identity, SaveTick = save.SaveTick, ObservedUtc = new FileInfo(path).LastWriteTimeUtc,
                    Metrics = { [SnapshotMetric.StoredKey] = MetricEntry.Of(SnapshotMetric.Revision,
                        new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 1 }) },
                });
            }
        }, SaveMode.Immediate);
        imported.Flush();
        imported.Close();

        var session = new ProfileSession(new ProfileStore(directory.Path), () => DateTime.UtcNow,
            new FileCheckpointArchive(directory.File("archive")));
        var view = session.GetView(new StatsOptions());
        var colony = Assert.Single(view.Colonies, c => c.Counted);
        var fromFiles = session.ReadChartHistory(view, colony.Key, Raids, directory.Path).Points;
        var fromSnapshots = session.ReadChartHistory(view, colony.Key, SnapshotMetric, directory.Path).Points;

        Assert.Equal(fromFiles.Select(p => p.Tick), fromSnapshots.Select(p => p.Tick));
        Assert.Equal(new[] { -1, -1, 1, 2, 3, 3 }, fromSnapshots.Select((p, i) => p.Predecessor(i)));
        Assert.Equal(fromFiles.Select(p => p.Unlinked), fromSnapshots.Select(p => p.Unlinked));
        Assert.Equal(fromFiles.Select(p => p.SkipsOnlyOtherBranches), fromSnapshots.Select(p => p.SkipsOnlyOtherBranches));

        Assert.Equal(before, files.Select(Fingerprint).ToArray());
    }

    /// <summary>
    /// What this version writes instead: E and F made in one session loaded from the older checkpoint, with no family
    /// recording. Copies of A–D and of E and F carrying those checkpoints (everything else in the saves unchanged) are
    /// imported; F now continues E, and the statistics after the older checkpoint are drawn as partial, not as complete.
    /// </summary>
    [Fact]
    public void SuppliedSixOriginals_SavesThisVersionWouldWriteJoinTheLaterSession()
    {
        var files = Originals();
        var before = files.Select(Fingerprint).ToArray();

        using var originals = new TempDir();
        var ticks = Import(originals, Path.GetDirectoryName(files[0])!)
            .Read(p => p.SaveObservations.ToDictionary(o => o.FileName, o => o.Values.TicksGame));
        var ordered = files.OrderBy(f => ticks[Path.GetFileName(f)]).ToArray();
        var (e, f) = (ordered[4], ordered[5]);
        var carried = ReadNumericStatistics(e);
        Assert.Equal(carried, ReadNumericStatistics(f));
        var parent = CheckpointJson.Parse(carried).Payload!;

        var accumulator = LiveMetricAccumulator.Resume(carried, Array.Empty<FamilySpec>(), ticks[Path.GetFileName(e)]);
        var atE = accumulator.PrepareSave(ticks[Path.GetFileName(e)], DateTime.UtcNow, "acceptance-e")!;
        accumulator.Commit(atE);
        var atF = accumulator.PrepareSave(ticks[Path.GetFileName(f)], DateTime.UtcNow, "acceptance-f")!;
        Assert.Equal((true, parent.CheckpointId), (atE.IsNew, atE.Payload!.ParentCheckpointId));
        Assert.Equal((true, "acceptance-e"), (atF.IsNew, atF.Payload!.ParentCheckpointId));

        using var copies = new TempDir();
        foreach (var path in ordered[..4])
            File.Copy(path, copies.File(Path.GetFileName(path)));
        WriteWithNumericStatistics(e, copies.File(Path.GetFileName(e)), carried, atE.Json);
        WriteWithNumericStatistics(f, copies.File(Path.GetFileName(f)), carried, atF.Json);

        using var directory = new TempDir();
        var session = Import(directory, copies.Path);
        var view = session.GetView(new StatsOptions());
        var colony = Assert.Single(view.Colonies, c => c.Counted);
        var definitions = new[] { Raids, MetricRegistry.Find(MetricKeys.WealthItemsPeak)! };
        // Files only: the older checkpoint is now in none of them (its own save is not supplied), so E's parent is
        // unknown, like B's. F still continues E directly.
        foreach (var definition in definitions)
        {
            var points = session.ReadChartHistory(view, colony.Key, definition, copies.Path).Points;
            Assert.Equal(new[] { -1, -1, 1, 2, -1, 4 }, points.Select((p, i) => p.Predecessor(i)));
            Assert.Equal(new[] { false, true, false, false, true, false }, points.Select(p => p.Unlinked));
        }

        // In the game, the checkpoint a save is loaded from is recorded into the profile (LoadedGame): then E continues D.
        var identity = session.Read(p => p.MetricObservations.First(o => o.Source == MetricObservationSource.SaveFile
            && o.FileAliases.Any(a => a.FileName == Path.GetFileName(e))).Identity.Clone());
        session.Mutate(p => ProfileMutations.UpsertMetricObservation(p,
            CheckpointMetrics.ToObservation(parent, carried, identity, "1.7.5005", null)), SaveMode.Immediate);
        foreach (var definition in definitions)
        {
            var points = session.ReadChartHistory(view, colony.Key, definition, copies.Path).Points;
            Assert.Equal(6, points.Count);
            Assert.Equal(new[] { -1, -1, 1, 2, 3, 4 }, points.Select((p, i) => p.Predecessor(i)));
            Assert.Equal(new[] { false, true, false, false, false, false }, points.Select(p => p.Unlinked));
            Assert.All(points, p => Assert.Equal(-1, p.ContinuesFrom));
        }

        // The older checkpoint and the two new ones, each once: totals copied unchanged after the older checkpoint's tick.
        var lineage = session.ReadChartHistory(view, colony.Key, MetricRegistry.Find(ResearchFamily.Completed)!, copies.Path).Points;
        Assert.Equal(6, lineage.Count);
        Assert.Equal(parent.SaveTick, lineage[3].Tick);
        Assert.Equal(new[] { ticks[Path.GetFileName(e)], ticks[Path.GetFileName(f)] }, lineage.Skip(4).Select(p => p.Tick));
        Assert.All(lineage, p => Assert.Null(p.SavedTick));
        Assert.NotNull(lineage[3].Value);
        Assert.Equal(lineage[3].Value!.Number, lineage[5].Value!.Number);
        Assert.Equal(new[] { MetricCoverage.Partial, MetricCoverage.Partial }, lineage.Skip(4).Select(p => p.Value!.Coverage));

        Assert.Equal(before, files.Select(Fingerprint).ToArray());
    }

    private static string[] Originals()
    {
        var folder = Environment.GetEnvironmentVariable("RWS_PRIVATE_SIX_SAVES_DIR");
        if (string.IsNullOrWhiteSpace(folder))
            Assert.Skip("Set RWS_PRIVATE_SIX_SAVES_DIR locally; original saves are never stored in CI.");
        var files = Directory.GetFiles(folder!, "*.rws").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(6, files.Length);
        return files;
    }

    private static ProfileSession Import(TempDir profile, string saves)
    {
        var session = new ProfileSession(new ProfileStore(profile.Path), () => DateTime.UtcNow,
            new FileCheckpointArchive(profile.File("archive")));
        new ImportEngine(new SaveReader()).Run(saves, session, null, TestContext.Current.CancellationToken);
        return session;
    }

    private static string ReadNumericStatistics(string path)
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit });
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "numericStatistics")
                return reader.ReadElementContentAsString();
        throw new InvalidDataException("no numericStatistics element");
    }

    /// <summary>A copy of <paramref name="source"/> whose only numericStatistics element holds <paramref name="json"/> instead.</summary>
    private static void WriteWithNumericStatistics(string source, string target, string expected, string json)
    {
        var bytes = File.ReadAllBytes(source);
        var bom = bytes is [0xEF, 0xBB, 0xBF, ..];
        var encoding = new UTF8Encoding(bom);
        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        var open = text.IndexOf("<numericStatistics>", StringComparison.Ordinal);
        var close = text.IndexOf("</numericStatistics>", open, StringComparison.Ordinal);
        Assert.True(open >= 0 && close > open);
        Assert.Equal(-1, text.IndexOf("<numericStatistics>", close, StringComparison.Ordinal));
        var start = open + "<numericStatistics>".Length;
        Assert.Equal(expected, XmlUnescape(text[start..close]));
        File.WriteAllText(target, string.Concat(text.AsSpan(0, start), SecurityElement.Escape(json), text.AsSpan(close)), encoding);
        Assert.Equal(json, ReadNumericStatistics(target));
    }

    private static string XmlUnescape(string escaped)
    {
        using var reader = XmlReader.Create(new StringReader("<x>" + escaped + "</x>"));
        reader.MoveToContent();
        return reader.ReadElementContentAsString();
    }

    private static (long Length, DateTime Utc, string Sha) Fingerprint(string path)
    {
        var info = new FileInfo(path);
        using var file = File.OpenRead(path);
        return (info.Length, info.LastWriteTimeUtc, Convert.ToHexString(SHA256.HashData(file)));
    }
}
