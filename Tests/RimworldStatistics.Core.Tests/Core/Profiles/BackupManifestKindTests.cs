using System.Text.Json;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class BackupManifestKindTests
{
    private static readonly DateTime Now = new(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ManifestWrittenByV20WithoutKindDeserializesAsManual()
    {
        // The exact shape v2.0 wrote: no "kind" property.
        var oldJson = JsonSerializer.Serialize(new
        {
            format = 1,
            createdUtc = Now,
            modVersion = "2.0.0",
            profileSchemaVersion = 2,
            label = "old",
            colonies = 1,
            observations = 2,
            files = Array.Empty<BackupFileEntry>(),
        }, ProfileJson.Options);

        var manifest = JsonSerializer.Deserialize<BackupManifest>(oldJson, ProfileJson.Options);

        Assert.NotNull(manifest);
        Assert.Equal(BackupKinds.Manual, manifest!.Kind);
        Assert.Equal("old", manifest.Label);
        Assert.Equal(1, manifest.Colonies);
        Assert.Equal(2, manifest.Observations);
    }

    [Fact]
    public void NewManifestKeepsKindForNewReadersAndIsIgnoredByOldOnes()
    {
        var manifest = new BackupManifest(1, Now, "2.1.0", 2, "memo", 3, 4, [], BackupKinds.PreRestore);
        var json = JsonSerializer.Serialize(manifest, ProfileJson.Options);

        Assert.Contains("\"kind\": \"pre-restore\"", json);
        Assert.Equal(BackupKinds.PreRestore,
            JsonSerializer.Deserialize<BackupManifest>(json, ProfileJson.Options)!.Kind);

        // A v2.0 reader had this constructor shape and simply ignores the extra property.
        var legacy = JsonSerializer.Deserialize<LegacyManifest>(json, ProfileJson.Options);
        Assert.NotNull(legacy);
        Assert.Equal((1, "2.1.0", "memo", 3, 4), (legacy!.Format, legacy.ModVersion, legacy.Label, legacy.Colonies, legacy.Observations));
    }

    [Fact]
    public void CreateWritesLabelAndKindIntoTheManifest()
    {
        using var dir = new TempDir();
        var path = ProfileBackupTests.Make(dir, created: Now, kind: BackupKinds.PreRestore, label: "Before restore");

        var validation = ProfileBackup.Validate(path, Profile.CurrentSchemaVersion);

        Assert.True(validation.Ok);
        Assert.Equal(BackupKinds.PreRestore, validation.Manifest!.Kind);
        Assert.Equal("Before restore", validation.Manifest.Label);
    }

    private sealed record LegacyManifest(
        int Format, DateTime CreatedUtc, string ModVersion, int ProfileSchemaVersion,
        string Label, int Colonies, int Observations, List<BackupFileEntry> Files);
}
