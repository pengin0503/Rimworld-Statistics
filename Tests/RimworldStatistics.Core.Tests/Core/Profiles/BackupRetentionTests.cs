using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class BackupRetentionTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    private static string Make(TempDir dir, DateTime created, string kind = BackupKinds.Manual, string label = "") =>
        ProfileBackupTests.Make(dir, created: created, kind: kind, label: label);

    [Fact]
    public void OverCountDeletesOldestValidBackupsAndKeepsTheNewest()
    {
        using var dir = new TempDir();
        var paths = Enumerable.Range(0, 5).Select(i => Make(dir, Now.AddMinutes(i))).ToArray();

        var deleted = BackupRetention.Apply(dir.Path, maxCount: 3, maxTotalBytes: long.MaxValue);

        Assert.Equal(paths.Take(2), deleted);
        var remaining = ProfileBackup.List(dir.Path);
        Assert.Equal(3, remaining.Count);
        Assert.Equal(paths[4], remaining[0].Path);
        Assert.DoesNotContain(paths[0], remaining.Select(info => info.Path));
        Assert.DoesNotContain(paths[1], remaining.Select(info => info.Path));
    }

    [Fact]
    public void OverBytesDeletesOldestValidBackups()
    {
        using var dir = new TempDir();
        var paths = Enumerable.Range(0, 4).Select(i => Make(dir, Now.AddMinutes(i))).ToArray();
        var total = paths.Sum(path => new FileInfo(path).Length);

        var deleted = BackupRetention.Apply(dir.Path, maxCount: 100, maxTotalBytes: total - new FileInfo(paths[0]).Length);

        Assert.Equal(new[] { paths[0] }, deleted);
        Assert.Equal(3, ProfileBackup.List(dir.Path).Count);
    }

    [Fact]
    public void NewestPreRestoreBackupSurvivesEvenWhenOlderThanOtherBackups()
    {
        using var dir = new TempDir();
        var oldestManual = Make(dir, Now, BackupKinds.Manual);
        var oldPreRestore = Make(dir, Now.AddMinutes(1), BackupKinds.PreRestore);
        var newestPreRestore = Make(dir, Now.AddMinutes(2), BackupKinds.PreRestore);
        var newestManual = Make(dir, Now.AddMinutes(3), BackupKinds.Manual);

        var deleted = BackupRetention.Apply(dir.Path, maxCount: 1, maxTotalBytes: long.MaxValue);

        Assert.Equal(new[] { oldestManual, oldPreRestore }, deleted);
        var remaining = ProfileBackup.List(dir.Path).Select(info => info.Path).ToArray();
        Assert.Equal(new[] { newestManual, newestPreRestore }.Order(StringComparer.Ordinal), remaining.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void InvalidBackupsAreNeverDeletedAndDoNotCountTowardTheCaps()
    {
        using var dir = new TempDir();
        var oldest = Make(dir, Now);
        var newest = Make(dir, Now.AddMinutes(1));
        var broken = dir.File("broken.zip");
        File.WriteAllText(broken, "not a zip");

        var deleted = BackupRetention.Apply(dir.Path, maxCount: 1, maxTotalBytes: long.MaxValue);

        Assert.Equal(new[] { oldest }, deleted);
        Assert.True(File.Exists(broken));
        Assert.Equal(2, ProfileBackup.List(dir.Path).Count);   // the kept valid backup and the untouched invalid one
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void NewestBackupAndExplicitlyProtectedPathsAreNeverDeleted()
    {
        using var dir = new TempDir();
        var protectedManual = Make(dir, Now);
        var newest = Make(dir, Now.AddMinutes(1));

        var deleted = BackupRetention.Apply(dir.Path, maxCount: 0, maxTotalBytes: 0,
            protectedPaths: [protectedManual]);

        Assert.Empty(deleted);
        Assert.True(File.Exists(protectedManual));
        Assert.True(File.Exists(newest));
        Assert.Equal(2, ProfileBackup.List(dir.Path).Count);
    }
}
