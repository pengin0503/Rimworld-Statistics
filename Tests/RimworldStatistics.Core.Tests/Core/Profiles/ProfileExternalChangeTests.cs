using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ProfileExternalChangeTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);

    private static Profile Tagged(string tag)
    {
        var profile = new Profile();
        profile.SaveObservations.Add(new SaveObservation { FileName = tag, Status = ObservationStatus.Ok });
        return profile;
    }

    private static ProfileStore Loaded(TempDir dir)
    {
        var store = new ProfileStore(dir.Path);
        store.Save(Tagged("original"));
        var reopened = new ProfileStore(dir.Path);
        Assert.Equal(ProfileLoadSource.Primary, reopened.Load(Now).Source);
        return reopened;
    }

    [Fact]
    public void UnknownBeforeAnyLoad()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        Assert.Equal(ProfileExternalChange.Unknown, store.CheckExternalChange());
    }

    [Fact]
    public void UnchangedAfterLoad()
    {
        using var dir = new TempDir();
        var store = Loaded(dir);
        Assert.Equal(ProfileExternalChange.Unchanged, store.CheckExternalChange());
        Assert.Equal(ProfileExternalChange.Unchanged, store.CheckExternalChange());
    }

    [Fact]
    public void UnchangedAfterThisStoreWrites()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Save(Tagged("written"));
        Assert.Equal(ProfileExternalChange.Unchanged, store.CheckExternalChange());
    }

    [Fact]
    public void ChangedAfterAnExternalWriteWithTheSameLength()
    {
        using var dir = new TempDir();
        var store = Loaded(dir);
        var bytes = File.ReadAllBytes(store.ProfilePath);
        var at = bytes.AsSpan().IndexOf("original"u8);
        Assert.True(at >= 0);
        bytes[at] = (byte)'O';   // same size, one byte different
        File.WriteAllBytes(store.ProfilePath, bytes);
        Assert.Equal(ProfileExternalChange.ChangedExternally, store.CheckExternalChange());
    }

    [Fact]
    public void ChangedAfterAnExternalWriteWithAnotherLength()
    {
        using var dir = new TempDir();
        var store = Loaded(dir);
        File.AppendAllText(store.ProfilePath, " ");
        Assert.Equal(ProfileExternalChange.ChangedExternally, store.CheckExternalChange());
    }

    [Fact]
    public void MissingAfterTheFileIsDeleted()
    {
        using var dir = new TempDir();
        var store = Loaded(dir);
        File.Delete(store.ProfilePath);
        Assert.Equal(ProfileExternalChange.Missing, store.CheckExternalChange());
    }

    [Fact]
    public void UnchangedWhileAFileThatDidNotExistAtLoadStillDoesNotExist()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        Assert.Equal(ProfileLoadSource.Empty, store.Load(Now).Source);
        Assert.Equal(ProfileExternalChange.Unchanged, store.CheckExternalChange());
    }

    [Fact]
    public void ChangedWhenAFileAppearsAfterAnEmptyLoad()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Load(Now);
        File.WriteAllText(store.ProfilePath, ProfileJson.Serialize(Tagged("appeared")));
        Assert.Equal(ProfileExternalChange.ChangedExternally, store.CheckExternalChange());
    }

    [Fact]
    public void UnknownWhenThePrimaryCannotBeInspected()
    {
        using var dir = new TempDir();
        var store = Loaded(dir);
        File.Delete(store.ProfilePath);
        Directory.CreateDirectory(store.ProfilePath);   // opening it for reading fails with an IO error
        Assert.Equal(ProfileExternalChange.Unknown, store.CheckExternalChange());
    }

    [Fact]
    public void SessionPassesTheStoreStateThrough()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Save(Tagged("session"));
        var session = new ProfileSession(store, () => Now);
        Assert.Equal(ProfileExternalChange.Unchanged, session.CheckExternalChange());

        File.AppendAllText(store.ProfilePath, " ");
        Assert.Equal(ProfileExternalChange.ChangedExternally, session.CheckExternalChange());
    }
}
