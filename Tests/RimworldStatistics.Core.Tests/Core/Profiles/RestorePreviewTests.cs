using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class RestorePreviewTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    private static string MakeBackup(TempDir dir, Profile profile) =>
        ProfileBackup.Create(new(ProfileJson.SerializeToUtf8Bytes(profile), null, null), dir.Path,
            new(Now, "2.1.0", "backup label")).Path;

    [Fact]
    public void ValidBackupPreviewsBackupAndCurrentCountsWithoutWriting()
    {
        using var dir = new TempDir();
        var backupProfile = new Profile();
        backupProfile.SaveObservations.Add(TestProfileBuilders.SaveObs("backup.rws", "A", 1));
        backupProfile.MetricObservations.Add(TestProfileBuilders.SaveFileMetrics("ma", "A", 1));
        backupProfile.MetricObservations.Add(TestProfileBuilders.SaveFileMetrics("mb", "A", 1));
        var backup = MakeBackup(dir, backupProfile);

        var current = new Profile();
        current.SaveObservations.Add(TestProfileBuilders.SaveObs("current-1.rws", "B", 1));
        current.SaveObservations.Add(TestProfileBuilders.SaveObs("current-2.rws", "C", 1));
        current.MetricObservations.Add(TestProfileBuilders.SaveFileMetrics("mc", "B", 1));
        var currentBytes = ProfileJson.SerializeToUtf8Bytes(current);

        var before = Snapshot(dir);
        var preview = ProfileBackup.Preview(backup, currentBytes);
        var after = Snapshot(dir);

        Assert.True(preview.Ok);
        Assert.Equal(BackupProblem.None, preview.Problem);
        Assert.Equal((1, 3), (preview.BackupColonies, preview.BackupObservations));
        Assert.Equal((2, 3), (preview.CurrentColonies, preview.CurrentObservations));
        Assert.Equal("backup label", preview.Manifest!.Label);
        Assert.Equal(BackupKinds.Manual, preview.Kind);
        Assert.Equal(1, preview.FileCount);
        Assert.Equal((long)ProfileJson.SerializeToUtf8Bytes(backupProfile).Length, preview.TotalUncompressedBytes);
        Assert.Equal(before, after);
    }

    [Fact]
    public void InvalidBackupPreviewsTheProblemWithoutThrowing()
    {
        using var dir = new TempDir();
        var path = dir.File("broken.zip");
        File.WriteAllText(path, "not a zip");

        var preview = ProfileBackup.Preview(path, null);

        Assert.False(preview.Ok);
        Assert.Equal(BackupProblem.Unreadable, preview.Problem);
        Assert.Null(preview.Manifest);
        Assert.Null(preview.CurrentColonies);
        Assert.Null(preview.CurrentObservations);
    }

    [Fact]
    public void UnreadableCurrentProfileStillShowsTheBackupSide()
    {
        using var dir = new TempDir();
        var backupProfile = new Profile();
        backupProfile.SaveObservations.Add(TestProfileBuilders.SaveObs("backup.rws", "A", 1));
        var backup = MakeBackup(dir, backupProfile);

        var preview = ProfileBackup.Preview(backup, "not json"u8.ToArray());

        Assert.True(preview.Ok);
        Assert.Equal((1, 1), (preview.BackupColonies, preview.BackupObservations));
        Assert.Null(preview.CurrentColonies);
        Assert.Null(preview.CurrentObservations);
    }

    [Fact]
    public void PayloadOverTheUncompressedCapIsTooLarge()
    {
        using var dir = new TempDir();
        var profile = new Profile();
        profile.SaveObservations.Add(TestProfileBuilders.SaveObs("big.rws", "A", 1));
        var backup = MakeBackup(dir, profile);
        var validation = ProfileBackup.Validate(backup, Profile.CurrentSchemaVersion);
        Assert.True(validation.Ok);
        var payloadBytes = validation.Manifest!.Files.Sum(file => file.Size);
        Assert.True(payloadBytes > 1);

        // A reduced cap exercises the same size check without materializing 2 GiB in the test.
        Assert.Equal(BackupProblem.TooLarge,
            ProfileBackup.Validate(backup, Profile.CurrentSchemaVersion, payloadBytes - 1).Problem);
        Assert.True(ProfileBackup.Validate(backup, Profile.CurrentSchemaVersion, payloadBytes).Ok);
        Assert.Equal(ProfileBackup.MaxTotalUncompressedBytes, 2L * 1024 * 1024 * 1024);
        Assert.Equal(BackupProblem.TooLarge,
            ProfileBackup.Validate(backup, Profile.CurrentSchemaVersion, 1).Problem);
    }

    private static (string Path, long Length, DateTime Written)[] Snapshot(TempDir dir) =>
        Directory.EnumerateFiles(dir.Path, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(path => (path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path)))
            .ToArray();
}
