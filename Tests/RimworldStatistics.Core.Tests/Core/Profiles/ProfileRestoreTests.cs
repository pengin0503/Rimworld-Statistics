using System.IO.Compression;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ProfileRestoreTests
{
    [Fact]
    public void RestoreBringsBackExactBytesAndRemovesNewerArchives()
    {
        using var root = new TempDir();
        using var backups = new TempDir();
        var archive = Directory.CreateDirectory(root.File("checkpoint-archive")).FullName;
        File.WriteAllText(Path.Combine(archive, "first.jsonl"), "original");
        var backup = ProfileBackupTests.Make(backups, archive, "{}");
        var primary = root.File("profile.json");
        File.WriteAllText(primary, "new");
        File.WriteAllText(primary + ".bak1", "backup");
        File.WriteAllText(Path.Combine(archive, "first.jsonl"), "new");
        File.WriteAllText(Path.Combine(archive, "new.jsonl"), "new");

        Assert.Equal("{}", ProfileBackup.Restore(backup, root.Path).Preferences);
        using var zip = ZipFile.OpenRead(backup);
        using var expected = zip.GetEntry("profile.json")!.Open();
        using var bytes = new MemoryStream();
        expected.CopyTo(bytes);
        Assert.Equal(bytes.ToArray(), File.ReadAllBytes(primary));
        Assert.False(File.Exists(primary + ".bak1"));
        Assert.False(File.Exists(Path.Combine(archive, "new.jsonl")));
        Assert.Equal("original", File.ReadAllText(Path.Combine(archive, "first.jsonl")));
        Assert.Equal(ProfileLoadSource.Primary, new ProfileStore(root.Path).Load(DateTime.UtcNow).Source);
    }

    [Fact]
    public void FailureDuringMoveRollsBack()
    {
        using var root = new TempDir();
        using var backups = new TempDir();
        var backup = ProfileBackupTests.Make(backups);
        var primary = root.File("profile.json");
        File.WriteAllText(primary, "original");
        File.WriteAllText(primary + ".previous", "previous");
        var archive = Directory.CreateDirectory(root.File("checkpoint-archive")).FullName;
        File.WriteAllText(Path.Combine(archive, "old.jsonl"), "old");
        Assert.Throws<IOException>(() => ProfileBackup.Restore(backup, root.Path, () => throw new IOException("load failed")));
        Assert.Equal("original", File.ReadAllText(primary));
        Assert.Equal("previous", File.ReadAllText(primary + ".previous"));
        Assert.Equal("old", File.ReadAllText(Path.Combine(archive, "old.jsonl")));
    }

    [Fact]
    public void InvalidBackupAndWriterLockLeaveCurrentFilesUntouched()
    {
        using var root = new TempDir();
        using var backups = new TempDir();
        var backup = ProfileBackupTests.Make(backups);
        var primary = root.File("profile.json");
        File.WriteAllText(primary, "original");
        using (File.Open(primary + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            Assert.Throws<IOException>(() => ProfileBackup.Restore(backup, root.Path));
        File.WriteAllText(backup, "broken");
        Assert.Throws<InvalidDataException>(() => ProfileBackup.Restore(backup, root.Path));
        Assert.Equal("original", File.ReadAllText(primary));
    }
}
