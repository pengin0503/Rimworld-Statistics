using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>Issue #310: a forced re-check reads every save again, writes nothing for unchanged ones, and applies other content.</summary>
public sealed class ForcedRecheckTests
{
    private static readonly DateTime Now = new(2026, 11, 17, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Stamp = new(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    private static byte[] Save(string seed, int ticks = 60000)
        => new SaveXmlBuilder().Minimal().Tick(ticks, 0).World(seed: seed, prv: 1).Build();

    private static void Write(TempDir dir, string name, byte[] bytes)
    {
        File.WriteAllBytes(dir.File(name), bytes);
        File.SetLastWriteTimeUtc(dir.File(name), Stamp);   // the same time every time: only the content differs
    }

    private static ProfileSession NewSession(TempDir store) => new(new ProfileStore(store.Path), () => Now);

    private static ImportResult Run(TempDir saves, ProfileSession s, ImportMode mode = ImportMode.Incremental,
        IProgress<ImportProgress>? progress = null, CancellationToken? ct = null)
        => new ImportEngine(new SaveReader()).Run(saves.Path, s, progress, ct ?? TestContext.Current.CancellationToken, mode);

    private static ImportResult Recheck(TempDir saves, ProfileSession s, IProgress<ImportProgress>? progress = null,
        CancellationToken? ct = null)
        => Run(saves, s, ImportMode.ForcedRecheck, progress, ct);

    /// <summary>The profile as it would be written, without the time of the last scan (every completed scan sets it).</summary>
    private static string Records(ProfileSession s) => s.Read(p =>
    {
        var node = JsonNode.Parse(ProfileJson.Serialize(p))!.AsObject();
        node.Remove("importState");
        return node.ToJsonString();
    });

    private static Dictionary<string, string> FileHashes(TempDir dir) => Directory.EnumerateFiles(dir.Path)
        .ToDictionary(f => Path.GetFileName(f), f => Hash(File.ReadAllBytes(f)));

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private sealed class SyncProgress(Action<ImportProgress> onReport) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => onReport(value);
    }

    [Fact]
    public void Reader_RecordsTheHashOfEveryByteOfACompleteRead()
    {
        var bytes = Save("a");
        var ok = new SaveReader().ReadDetailed(new MemoryStream(bytes), "a.rws", bytes.Length, Stamp);
        Assert.Equal(ObservationStatus.Ok, ok.Observation.Status);
        Assert.Equal(Hash(bytes), ok.Observation.ContentHash);
        Assert.Equal(ok.Metrics!.Key, ok.Observation.ContentHash);

        var torn = bytes[..(bytes.Length / 2)];
        var failed = new SaveReader().ReadDetailed(new MemoryStream(torn), "a.rws", torn.Length, Stamp);
        Assert.Equal(ObservationStatus.Error, failed.Observation.Status);
        Assert.Null(failed.Observation.ContentHash);
    }

    [Fact]
    public void UnchangedSaves_AreReadAgainButNothingIsWrittenAndTheOrdinaryScanStillSkipsThem()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("a"));
        Write(saves, "b.rws", Save("b"));
        Write(saves, "Copy of a.rws", Save("a"));
        var s = NewSession(store);
        Assert.Equal(3, Run(saves, s).Read);
        var before = Records(s);
        var files = FileHashes(saves);
        var revision = s.Revision;

        var r = Recheck(saves, s);
        Assert.Equal(1, s.Revision - revision);   // only the time of the scan; no save's records were touched
        Assert.Equal((ImportMode.ForcedRecheck, 3, 3, 0, 0, 0, 0, false),
            (r.Mode, r.Read, r.Unchanged, r.Replaced, r.Added, r.Failed, r.Skipped, r.Cancelled));
        Assert.Equal(before, Records(s));
        Assert.Equal(files, FileHashes(saves));   // read only

        var ordinary = Run(saves, s);
        Assert.Equal((ImportMode.Incremental, 0, 3), (ordinary.Mode, ordinary.Read, ordinary.Skipped));
    }

    [Fact]
    public void OtherContentUnderTheSameNameSizeAndTime_IsRecordedAndTheEarlierRecordStaysAsHistory()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var first = Save("colony-a");
        var second = Save("colony-b");
        Assert.Equal(first.Length, second.Length);
        Write(saves, "Autosave-1.rws", first);
        var s = NewSession(store);
        Run(saves, s);

        Write(saves, "Autosave-1.rws", second);   // the slot now holds another colony, under the same name, size and time
        Assert.Equal((0, 1), (Run(saves, s).Read, Run(saves, s).Skipped));   // what the ordinary scan cannot see

        var r = Recheck(saves, s);
        Assert.Equal((1, 0, 1, 0, 0), (r.Read, r.Unchanged, r.Replaced, r.Added, r.Failed));
        var records = s.Read(p => p.SaveObservations.ToList());
        Assert.Equal(2, records.Count);
        Assert.Equal(Hash(second), records.Single(o => !o.Superseded).ContentHash);
        Assert.Equal(Hash(first), records.Single(o => o.Superseded).ContentHash);
        Assert.Equal(2, s.GetView(new StatsOptions()).Stats.ColonyCount);
        Assert.Contains(s.Read(p => p.MetricObservations.Select(m => m.Key).ToList()), k => k == Hash(second));
        Assert.Equal(Hash(second), FileHashes(saves)["Autosave-1.rws"]);

        // Checked again, the slot matches its new record: nothing more is written, and the ordinary scan skips it.
        var after = Records(s);
        var again = Recheck(saves, s);
        Assert.Equal((1, 1, 0), (again.Read, again.Unchanged, again.Replaced));
        Assert.Equal(after, Records(s));
        Assert.Equal((0, 1), (Run(saves, s).Read, Run(saves, s).Skipped));
    }

    [Fact]
    public void ALaterSaveOfTheSameColony_ReplacesTheRecordItMakesRedundant()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var earlier = Save("colony", 60000);
        var later = Save("colony", 70000);
        Assert.Equal(earlier.Length, later.Length);
        Write(saves, "a.rws", earlier);
        var s = NewSession(store);
        Run(saves, s);
        Write(saves, "a.rws", later);

        var r = Recheck(saves, s);
        Assert.Equal(1, r.Replaced);
        // As for an overwritten save: the earlier record changes no total, so it is not kept.
        var record = s.Read(p => p.SaveObservations.Single());
        Assert.Equal((false, 70000, Hash(later)), (record.Superseded, record.Values.TicksGame, record.ContentHash));
        Assert.Equal(1, s.GetView(new StatsOptions()).Stats.ColonyCount);
    }

    [Fact]
    public void RecordsMadeBeforeContentHashes_AreComparedByTheirNumericObservationOrByTheRecordItself()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("a"));
        Write(saves, "b.rws", Save("b"));
        var s = NewSession(store);
        Run(saves, s);
        // As an earlier version left them: no content hash, and b's numeric observation compacted away.
        s.Mutate(p =>
        {
            foreach (var o in p.SaveObservations)
                o.ContentHash = null;
            p.MetricObservations.RemoveAll(m => m.Source == MetricObservationSource.SaveFile
                && m.FileAliases.Any(a => a.FileName == "b.rws"));
        }, SaveMode.None);
        var before = Records(s);

        var unchanged = Recheck(saves, s);
        Assert.Equal((2, 2, 0), (unchanged.Read, unchanged.Unchanged, unchanged.Replaced));
        Assert.Equal(before, Records(s));

        Write(saves, "a.rws", Save("c"));   // proved by a's numeric observation
        Write(saves, "b.rws", Save("d"));   // proved by b's record itself
        var changed = Recheck(saves, s);
        Assert.Equal((2, 0, 2), (changed.Read, changed.Unchanged, changed.Replaced));
        Assert.Equal(new[] { Hash(Save("c")), Hash(Save("d")) },
            s.Read(p => p.SaveObservations.Where(o => !o.Superseded).OrderBy(o => o.FileName).Select(o => o.ContentHash).ToArray()));
        Assert.Equal(4, s.GetView(new StatsOptions()).Stats.ColonyCount);
    }

    [Fact]
    public void AFailedReRead_KeepsTheGoodRecord()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var good = Save("a");
        Write(saves, "a.rws", good);
        Write(saves, "b.rws", Save("b"));
        var s = NewSession(store);
        Run(saves, s);
        var before = Records(s);

        // The same name, size and time, but bytes that no longer parse.
        var damaged = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(good).Replace("<game>", "<gam!>", StringComparison.Ordinal));
        Assert.Equal(good.Length, damaged.Length);
        Write(saves, "a.rws", damaged);

        var r = Recheck(saves, s);
        Assert.Equal((2, 1, 0, 1, 1), (r.Read, r.Unchanged, r.Replaced, r.Failed, r.KeptAfterFailure));
        Assert.Equal(before, Records(s));
        Assert.Equal(0, s.Read(p => p.SaveObservations.Count(o => !o.Superseded && o.Status != ObservationStatus.Ok)));
    }

    [Fact]
    public void ACancelledReCheckOrASaveDeletedDuringIt_LosesNothing()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 5; i++)
            Write(saves, $"s{i}.rws", Save("seed" + i));
        var s = NewSession(store);
        Run(saves, s);
        var before = Records(s);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var cancelled = Recheck(saves, s, new SyncProgress(p => { if (p.Done == 2) cts.Cancel(); }), cts.Token);
        Assert.True(cancelled.Cancelled);
        Assert.Equal((2, 2), (cancelled.Read, cancelled.Unchanged));
        Assert.Equal(before, Records(s));

        var deleted = Recheck(saves, s, new SyncProgress(p =>
        {
            if (p.CurrentFile == "s3.rws")
                File.Delete(saves.File("s4.rws"));
        }));
        Assert.Equal((4, 4), (deleted.Read, deleted.Unchanged));
        Assert.DoesNotContain("s4.rws", deleted.PresentFiles);
        Assert.Equal(before, Records(s));   // the record of the deleted save stays
    }

    [Fact]
    public void RunStatus_KeepsTheModeOfTheLatestScan()
    {
        var status = new ImportRunStatus();
        var forced = status.Begin(ImportMode.ForcedRecheck);
        Assert.Equal((ImportRunPhase.Running, ImportMode.ForcedRecheck), (status.Current.Phase, status.Current.Mode));
        status.Complete(forced, new ImportResult { Mode = ImportMode.ForcedRecheck, PresentFiles = [] });
        Assert.Equal((ImportRunPhase.Completed, ImportMode.ForcedRecheck), (status.Current.Phase, status.Current.Mode));
        var failing = status.Begin(ImportMode.ForcedRecheck);
        status.Fail(failing, new IOException("gone"));
        Assert.Equal((ImportRunPhase.Failed, ImportMode.ForcedRecheck), (status.Current.Phase, status.Current.Mode));
        status.Begin();
        Assert.Equal(ImportMode.Incremental, status.Current.Mode);
    }
}
