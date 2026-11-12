using System.IO.Compression;
using System.Text;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ProfileBackupTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void EmptyProfileCanBeBackedUpWithCompleteManifest()
    {
        using var dir = new TempDir();
        var archive = Directory.CreateDirectory(dir.File("archive"));
        File.WriteAllText(Path.Combine(archive.FullName, "campaign.jsonl"), "archived");
        var backup = Make(dir, archive.FullName, "{}");
        var validation = ProfileBackup.Validate(backup, Profile.CurrentSchemaVersion);
        Assert.True(validation.Ok);
        Assert.Equal(0, validation.Manifest!.Colonies);
        Assert.Equal(new[] { "profile.json", "checkpoint-archive/campaign.jsonl", "preferences.json" }, validation.Manifest.Files.Select(f => f.Path));
        Assert.All(validation.Manifest.Files, f => Assert.Matches("^[0-9A-F]{64}$", f.Sha256));
    }

    [Fact]
    public void ExistingFileIsNeverOverwritten()
    {
        using var dir = new TempDir();
        var first = Make(dir);
        var second = Make(dir);
        Assert.NotEqual(first, second);
        Assert.Equal(2, ProfileBackup.List(dir.Path).Count);
    }

    [Fact]
    public void TamperedEntryIsRejected()
    {
        using var dir = new TempDir();
        var backup = Make(dir);
        using (var zip = ZipFile.Open(backup, ZipArchiveMode.Update))
        {
            zip.GetEntry("profile.json")!.Delete();
            using var stream = zip.CreateEntry("profile.json").Open();
            stream.Write("{}"u8);
        }
        Assert.Equal(BackupProblem.HashMismatch, ProfileBackup.Validate(backup, 2).Problem);
    }

    [Theory]
    [InlineData(false, BackupProblem.NoManifest)]
    [InlineData(true, BackupProblem.Unreadable)]
    public void InvalidZipAppearsInList(bool truncate, BackupProblem expected)
    {
        using var dir = new TempDir();
        var path = dir.File("other.zip");
        if (truncate)
            File.WriteAllText(path, "broken");
        else
            using (ZipFile.Open(path, ZipArchiveMode.Create)) { }
        Assert.Equal(expected, Assert.Single(ProfileBackup.List(dir.Path)).Validation.Problem);
    }

    [Fact]
    public void NewerProfileIsRejectedByOlderReader()
    {
        using var dir = new TempDir();
        var backup = Make(dir, schema: 3);
        Assert.Equal(BackupProblem.NewerSchema, ProfileBackup.Validate(backup, 2).Problem);
    }

    [Fact]
    public void FailedValidationLeavesNoZip()
    {
        using var dir = new TempDir();
        Assert.Throws<InvalidDataException>(() => ProfileBackup.Create(new("invalid"u8.ToArray(), null, null), dir.Path, new(Now, "test", "")));
        Assert.Empty(Directory.GetFileSystemEntries(dir.Path));
    }

    internal static string Make(TempDir dir, string? archive = null, string? preferences = null, int schema = 2) =>
        ProfileBackup.Create(new(Encoding.UTF8.GetBytes(ProfileJson.Serialize(new Profile { SchemaVersion = schema })), archive, preferences), dir.Path, new(Now, "test", "")).Path;
}
