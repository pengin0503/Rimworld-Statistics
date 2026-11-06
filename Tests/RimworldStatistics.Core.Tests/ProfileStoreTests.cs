using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ProfileStoreTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);

    private static Profile Tagged(string tag)
    {
        var p = new Profile();
        p.SaveObservations.Add(new SaveObservation { FileName = tag, Status = ObservationStatus.Ok });
        return p;
    }

    private static string TagOf(string path) => ProfileJson.Deserialize(File.ReadAllText(path)).SaveObservations.Single().FileName;

    [Fact]
    public void Save_ThenLoad_RoundTrips()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(Path.Combine(dir.Path, "RimworldStatistics"));   // directory is created on save
        store.Save(Tagged("「保存」.rws"));
        var r = new ProfileStore(Path.Combine(dir.Path, "RimworldStatistics")).Load(Now);
        Assert.Equal((ProfileLoadSource.Primary, false, (string?)null), (r.Source, r.ReadOnly, r.CorruptFileRenamedTo));
        Assert.Equal("「保存」.rws", r.Profile.SaveObservations.Single().FileName);
        Assert.Equal(Path.Combine(dir.Path, "RimworldStatistics", "profile.json"), store.ProfilePath);
    }

    [Fact]
    public void Save_RotatesThreeBackups()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        for (var i = 1; i <= 4; i++)
            store.Save(Tagged("v" + i));
        Assert.Equal("v4", TagOf(dir.File("profile.json")));
        Assert.Equal("v3", TagOf(dir.File("profile.json.bak1")));
        Assert.Equal("v2", TagOf(dir.File("profile.json.bak2")));
        Assert.Equal("v1", TagOf(dir.File("profile.json.bak3")));
        Assert.False(File.Exists(dir.File("profile.json.bak4")));
        store.Save(Tagged("v5"));
        Assert.Equal("v2", TagOf(dir.File("profile.json.bak3")));
    }

    [Fact]
    public void Load_CorruptPrimary_FallsBackToBackup1()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Save(Tagged("good-old"));
        store.Save(Tagged("good-new"));
        File.WriteAllText(dir.File("profile.json"), "{ \"schemaVersion\": 1, \"saveObserv");   // torn write
        var r = new ProfileStore(dir.Path).Load(Now);
        Assert.Equal(ProfileLoadSource.Backup1, r.Source);
        Assert.False(r.ReadOnly);
        Assert.Equal("good-old", r.Profile.SaveObservations.Single().FileName);
        // The broken primary is set aside, so the next save does not rotate it into the backups.
        Assert.Equal("profile.json.corrupt-20261106T080000Z", r.CorruptFileRenamedTo);
        Assert.False(File.Exists(dir.File("profile.json")));
        var reopened = new ProfileStore(dir.Path);
        reopened.Load(Now);
        reopened.Save(Tagged("after"));
        Assert.Equal("good-old", TagOf(dir.File("profile.json.bak1")));
    }

    [Fact]
    public void Load_AllCorrupt_StartsEmptyAndRenamesPrimary()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("profile.json"), "not json at all");
        File.WriteAllText(dir.File("profile.json.bak1"), "null");
        File.WriteAllText(dir.File("profile.json.bak2"), "[1,2,3]");
        var r = new ProfileStore(dir.Path).Load(Now);
        Assert.Equal(ProfileLoadSource.Empty, r.Source);
        Assert.Empty(r.Profile.SaveObservations);
        Assert.Equal("profile.json.corrupt-20261106T080000Z", r.CorruptFileRenamedTo);
        Assert.Equal("not json at all", File.ReadAllText(dir.File("profile.json.corrupt-20261106T080000Z")));
        Assert.False(r.ReadOnly);
    }

    [Fact]
    public void Load_NoFile_IsEmptyPrimaryless()
    {
        using var dir = new TempDir();
        var r = new ProfileStore(Path.Combine(dir.Path, "absent")).Load(Now);
        Assert.Equal((ProfileLoadSource.Empty, (string?)null, false), (r.Source, r.CorruptFileRenamedTo, r.ReadOnly));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 2, "saveObservations": [ { "fileName": "future.rws", "status": "ok", "newField": { "x": 1 } } ] }""", "future.rws")]
    [InlineData("""{ "schemaVersion": 2, "endingEvents": [ { "type": "someFutureEnding" } ] }""", null)]   // not readable by this version at all
    public void Load_NewerSchema_IsReadOnlyAndSaveThrows(string json, string? visibleFile)
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("profile.json"), json);
        var store = new ProfileStore(dir.Path);
        var r = store.Load(Now);
        Assert.Equal((ProfileLoadSource.NewerSchema, true, (string?)null), (r.Source, r.ReadOnly, r.CorruptFileRenamedTo));
        Assert.Equal(visibleFile, r.Profile.SaveObservations.SingleOrDefault()?.FileName);
        Assert.Throws<InvalidOperationException>(() => store.Save(new Profile()));
        Assert.Throws<InvalidOperationException>(() => store.SaveSerialized("{}"));
        Assert.Equal(json, File.ReadAllText(dir.File("profile.json")));
        Assert.Equal(new[] { "profile.json" }, Directory.EnumerateFiles(dir.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void Load_UnreadablePrimary_IsReadOnly()
    {
        // A primary that exists but cannot be read (not a parse failure) must not be overwritten from a backup.
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Save(Tagged("backup"));
        store.Save(Tagged("primary"));
        File.Delete(dir.File("profile.json"));
        Directory.CreateDirectory(dir.File("profile.json"));   // reading it fails with an IO error
        var reopened = new ProfileStore(dir.Path);
        var r = reopened.Load(Now);
        Assert.True(r.ReadOnly);
        Assert.Null(r.CorruptFileRenamedTo);
        Assert.Equal("backup", r.Profile.SaveObservations.Single().FileName);
        Assert.Throws<InvalidOperationException>(() => reopened.Save(new Profile()));
        Assert.True(Directory.Exists(dir.File("profile.json")));
    }

    [Fact]
    public void Save_LeavesNoTmpFile()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Save(Tagged("a"));
        store.Save(Tagged("b"));
        Assert.Equal(new[] { "profile.json", "profile.json.bak1" }, Directory.EnumerateFiles(dir.Path).Select(Path.GetFileName).Order());
    }
}
