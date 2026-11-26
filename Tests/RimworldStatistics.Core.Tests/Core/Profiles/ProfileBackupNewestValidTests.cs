using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ProfileBackupNewestValidTests
{
    private static readonly DateTime Now = new(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PicksTheNewestValidBackup()
    {
        using var dir = new TempDir();
        var oldest = ProfileBackupTests.Make(dir, created: Now.AddHours(-2));
        var newest = ProfileBackupTests.Make(dir, created: Now);
        var middle = ProfileBackupTests.Make(dir, created: Now.AddHours(-1));

        var picked = ProfileBackup.NewestValid(dir.Path);
        Assert.NotNull(picked);
        Assert.Equal(newest, picked!.Path);
        Assert.True(picked.Validation.Ok);
        Assert.True(File.Exists(oldest));
        Assert.True(File.Exists(middle));
    }

    [Fact]
    public void SkipsInvalidBackupsEvenWhenTheyAreNewest()
    {
        using var dir = new TempDir();
        var valid = ProfileBackupTests.Make(dir, created: Now.AddDays(-1));
        File.WriteAllText(dir.File("rws-backup-newest.zip"), "not a zip");

        var picked = ProfileBackup.NewestValid(dir.Path);
        Assert.NotNull(picked);
        Assert.Equal(valid, picked!.Path);
    }

    [Fact]
    public void NullWhenNoBackupIsValid()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("rws-backup-broken.zip"), "not a zip");
        Assert.Null(ProfileBackup.NewestValid(dir.Path));
        Assert.DoesNotContain(ProfileBackup.List(dir.Path), info => info.Validation.Ok);
    }

    [Fact]
    public void NullWhenTheDirectoryDoesNotExist()
    {
        using var dir = new TempDir();
        Assert.Null(ProfileBackup.NewestValid(Path.Combine(dir.Path, "absent")));
    }

    [Fact]
    public void TheListingOverloadUsesListOrdering()
    {
        using var dir = new TempDir();
        var oldest = ProfileBackupTests.Make(dir, created: Now.AddHours(-2));
        var newest = ProfileBackupTests.Make(dir, created: Now);
        File.WriteAllText(dir.File("rws-backup-newest.zip"), "not a zip");
        var listed = ProfileBackup.List(dir.Path);

        var picked = ProfileBackup.NewestValid(listed);
        Assert.NotNull(picked);
        Assert.Equal(newest, picked!.Path);
        Assert.NotEqual(oldest, picked.Path);
    }
}
