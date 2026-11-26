using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>S20 exclusions, S12 change names and S18 single-save re-checks at the engine level.</summary>
public sealed class ImportEngineSelectionTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Stamp = new(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);

    private static byte[] Save(string seed) => new SaveXmlBuilder().Minimal().World(seed: seed, prv: 1).Build();

    private static void Write(TempDir dir, string name, byte[] bytes, DateTime? time = null)
    {
        File.WriteAllBytes(dir.File(name), bytes);
        File.SetLastWriteTimeUtc(dir.File(name), time ?? Stamp);
    }

    private static ProfileSession NewSession(TempDir store) => new(new ProfileStore(store.Path), () => Now);

    private static ImportResult Run(TempDir saves, ProfileSession s, IReadOnlyList<string>? exclusions = null,
        ImportMode mode = ImportMode.Incremental)
        => new ImportEngine(new SaveReader()).Run(saves.Path, s, null, TestContext.Current.CancellationToken, mode, exclusions);

    private static ImportResult RunSingle(TempDir saves, ProfileSession s, string name,
        IReadOnlyList<string>? exclusions = null)
        => new ImportEngine(new SaveReader()).RunSingle(saves.Path, name, s, null, TestContext.Current.CancellationToken,
            exclusions);

    [Fact]
    public void Run_ExcludedFailingSave_IsNotReadAndNotCountedAsSkipped()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var good = Save("a");
        Write(saves, "a.rws", good);
        Write(saves, "bad.rws", good[..(good.Length / 2)]);   // torn save: would fail
        var s = NewSession(store);

        var withExclusions = Run(saves, s, new[] { "bad.rws" });
        Assert.Equal((1, 0, 0, 1), (withExclusions.Read, withExclusions.Skipped, withExclusions.Failed, withExclusions.Excluded));
        Assert.Equal(new[] { "bad.rws" }, withExclusions.ExcludedFiles);
        Assert.Equal(new[] { "a.rws", "bad.rws" }, withExclusions.PresentFiles);   // excluded is still on disk
        Assert.Equal(new[] { "a.rws" }, s.Read(p => p.SaveObservations.Select(o => o.FileName).Order(StringComparer.Ordinal).ToArray()));

        var without = Run(saves, s);
        Assert.Equal((1, 1, 1, 0), (without.Read, without.Skipped, without.Failed, without.Excluded));
        Assert.Equal(ObservationStatus.Error, s.Read(p => p.SaveObservations.Single(o => o.FileName == "bad.rws").Status));
    }

    [Fact]
    public void ForcedRecheck_RespectsExclusions()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var good = Save("a");
        Write(saves, "a.rws", good);
        Write(saves, "bad.rws", good[..(good.Length / 2)]);
        var s = NewSession(store);

        var result = Run(saves, s, new[] { "bad.rws" }, ImportMode.ForcedRecheck);
        Assert.Equal((1, 0, 0, 1, 1), (result.Read, result.Failed, result.Unchanged, result.Added, result.Excluded));
        Assert.Equal(new[] { "a.rws" }, s.Read(p => p.SaveObservations.Select(o => o.FileName).ToArray()));
    }

    [Fact]
    public void AddedFiles_FollowTheAddedCountAndStaySorted()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var good = Save("good");
        Write(saves, "a.rws", good);
        Write(saves, "b.rws", Save("b"));
        Write(saves, "c.rws", Save("c"));
        Write(saves, "bad.rws", good[..(good.Length / 2)]);
        var s = NewSession(store);

        var first = Run(saves, s);
        Assert.Equal((4, 1, 3), (first.Read, first.Failed, first.Added));
        Assert.Equal(new[] { "a.rws", "b.rws", "c.rws" }, first.AddedFiles);
        Assert.Empty(first.ReplacedFiles);

        Write(saves, "a.rws", Save("a2"), Stamp.AddHours(1));   // changed by time
        Write(saves, "e.rws", Save("e"));
        var second = Run(saves, s);
        Assert.Equal((3, 2, 1, 2), (second.Read, second.Skipped, second.Failed, second.Added));
        Assert.Equal(new[] { "a.rws", "e.rws" }, second.AddedFiles);
        Assert.Empty(second.ReplacedFiles);
    }

    [Fact]
    public void ForcedRecheck_ReportsAddedAndReplacedNames()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var first = Save("colony-a");
        var second = Save("colony-b");
        Assert.Equal(first.Length, second.Length);
        Write(saves, "a.rws", Save("a"));
        Write(saves, "b.rws", first);
        var s = NewSession(store);
        Run(saves, s);

        Write(saves, "b.rws", second);   // same name, size and time: only a forced re-check sees the change
        Write(saves, "c.rws", Save("c"));
        var result = Run(saves, s, mode: ImportMode.ForcedRecheck);
        Assert.Equal((3, 1, 0, 1, 1), (result.Read, result.Unchanged, result.Failed, result.Replaced, result.Added));
        Assert.Equal(new[] { "b.rws" }, result.ReplacedFiles);
        Assert.Equal(new[] { "c.rws" }, result.AddedFiles);
    }

    [Fact]
    public void NameLists_AreCappedAtOneHundredInOrdinalOrder()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 101; i++)
            Write(saves, $"s{i:000}.rws", Save("seed" + i));
        var s = NewSession(store);

        var added = Run(saves, s);
        Assert.Equal(101, added.Added);
        Assert.Equal(100, added.AddedFiles.Count);
        Assert.Equal("s000.rws", added.AddedFiles[0]);
        Assert.Equal("s099.rws", added.AddedFiles[^1]);
    }

    [Fact]
    public void ExcludedNameList_IsCappedAtOneHundredInOrdinalOrder()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        for (var i = 0; i < 101; i++)
            Write(saves, $"s{i:000}.rws", Save("seed" + i));
        var s = NewSession(store);

        var result = Run(saves, s, new[] { "*.rws" });
        Assert.Equal((0, 101), (result.Read, result.Excluded));
        Assert.Equal(100, result.ExcludedFiles.Count);
        Assert.Equal("s000.rws", result.ExcludedFiles[0]);
        Assert.Equal("s099.rws", result.ExcludedFiles[^1]);
        Assert.Equal(101, result.PresentFiles.Count);
    }

    [Fact]
    public void RunSingle_ReadsOnlyTheNamedChangedSave()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("colony-a"));
        Write(saves, "b.rws", Save("b"));
        var s = NewSession(store);
        Run(saves, s);

        Write(saves, "a.rws", Save("colony-b"));   // same name, size and time; forced semantics
        Write(saves, "b.rws", Save("b2"));
        var bHash = s.Read(p => p.SaveObservations.Single(o => o.FileName == "b.rws").ContentHash);
        var aHash = s.Read(p => p.SaveObservations.Single(o => o.FileName == "a.rws").ContentHash);

        var result = RunSingle(saves, s, "a.rws");
        Assert.Equal((ImportMode.ForcedRecheck, 1, 0, 0, 1), (result.Mode, result.Read, result.Unchanged, result.Failed, result.Replaced));
        Assert.Equal(new[] { "a.rws" }, result.ReplacedFiles);
        Assert.Equal(new[] { "a.rws", "b.rws" }, result.PresentFiles);
        Assert.NotEqual(aHash, s.Read(p => p.SaveObservations.Single(o => !o.Superseded && o.FileName == "a.rws").ContentHash));
        Assert.Equal(bHash, s.Read(p => p.SaveObservations.Single(o => o.FileName == "b.rws").ContentHash));
        Assert.DoesNotContain(s.Read(p => p.SaveObservations), o => o.Superseded && o.FileName == "b.rws");
    }

    [Fact]
    public void RunSingle_FailingSaveKeepsItsGoodRecord()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var good = Save("a");
        Write(saves, "a.rws", good);
        Write(saves, "b.rws", Save("b"));
        var s = NewSession(store);
        Run(saves, s);
        var beforeHash = s.Read(p => p.SaveObservations.Single(o => o.FileName == "a.rws").ContentHash);

        var damaged = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Encoding.UTF8.GetString(good).Replace("<game>", "<gam!>", StringComparison.Ordinal));
        Assert.Equal(good.Length, damaged.Length);
        Write(saves, "a.rws", damaged);

        var result = RunSingle(saves, s, "a.rws");
        Assert.Equal((1, 0, 0, 1, 1), (result.Read, result.Unchanged, result.Replaced, result.Failed, result.KeptAfterFailure));
        var record = s.Read(p => p.SaveObservations.Single(o => o.FileName == "a.rws"));
        Assert.Equal((false, ObservationStatus.Ok, beforeHash), (record.Superseded, record.Status, record.ContentHash));
        Assert.Equal(2, s.Read(p => p.SaveObservations.Count(o => !o.Superseded)));
    }

    [Fact]
    public void RunSingle_UnchangedSaveWritesNothing()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("a"));
        var s = NewSession(store);
        Run(saves, s);
        var revision = s.Revision;

        var result = RunSingle(saves, s, "a.rws");
        Assert.Equal((1, 1, 0), (result.Read, result.Unchanged, result.Replaced));
        Assert.Equal(1, s.Revision - revision);   // only the last-scan time
    }

    [Fact]
    public void RunSingle_MissingOrNonSaveFile_HasAllZeroCountsAndStillListsPresentSaves()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("a"));
        Write(saves, "notes.txt", Save("text"));
        var s = NewSession(store);

        foreach (var name in new[] { "missing.rws", "notes.txt" })
        {
            var result = RunSingle(saves, s, name);
            Assert.Equal((0, 0, 0, 0, 0), (result.Read, result.Skipped, result.Failed, result.Added, result.Excluded));
            Assert.Equal(new[] { "a.rws" }, result.PresentFiles);
        }
        Assert.Empty(s.Read(p => p.SaveObservations));
    }

    [Fact]
    public void RunSingle_ExcludedFileIsNotRead()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        Write(saves, "a.rws", Save("a"));
        var s = NewSession(store);

        var result = RunSingle(saves, s, "a.rws", new[] { "a.rws" });
        Assert.Equal((0, 0, 1), (result.Read, result.Failed, result.Excluded));
        Assert.Equal(new[] { "a.rws" }, result.ExcludedFiles);
        Assert.Equal(new[] { "a.rws" }, result.PresentFiles);
        Assert.Empty(s.Read(p => p.SaveObservations));
    }
}
