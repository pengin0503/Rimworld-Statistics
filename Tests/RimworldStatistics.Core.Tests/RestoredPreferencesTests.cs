using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Issue #325: the presentation settings of a restored backup reach the main thread whether or not the Data tab is drawn
/// again, and only after a restore that succeeded and asked for them.
/// </summary>
public sealed class RestoredPreferencesTests
{
    private const string Settings = """{"version":1,"groups":[{"id":"g1","name":"  Early colonies  ","colonyKeys":["c1","c1","c2"]}],"favoriteMetricKeys":["kills"]}""";

    [Fact]
    public async Task ARestoreThatSucceeded_HandsItsSettingsToTheMainThreadOnce_WithoutTheWindow()
    {
        using var backups = new TempDir();
        using var data = new TempDir();
        var backup = ProfileBackupTests.Make(backups, preferences: Settings);
        var handoff = new RestoredPreferences();

        // The restore runs on a pool thread; nothing polls the Data tab while it runs (the window was closed).
        await Task.Run(() => handoff.Offer(ProfileBackup.Restore(backup, data.Path), requested: true), TestContext.Current.CancellationToken);
        Assert.True(handoff.Pending);

        var applied = new List<string>();
        Assert.True(handoff.ApplyTo(applied.Add));   // the next frame of the main-thread hook
        Assert.False(handoff.ApplyTo(applied.Add));
        Assert.False(handoff.Pending);
        var json = Assert.Single(applied);
        var restored = ViewPreferences.Parse(json);
        Assert.Equal("Early colonies", Assert.Single(restored.Groups).Name);
        Assert.Equal(["c1", "c2"], restored.Groups[0].ColonyKeys);
        Assert.Equal(["kills"], restored.FavoriteMetricKeys);
        Assert.Equal(ViewPreferences.Parse(Settings).ToJson(), json);   // normalised as the window reads settings
    }

    [Fact]
    public void NothingIsHandedOver_WhenNotAskedFor_WhenTheBackupHasNone_OrWhenTheRestoreFailed()
    {
        using var backups = new TempDir();
        using var data = new TempDir();
        var handoff = new RestoredPreferences();

        handoff.Offer(ProfileBackup.Restore(ProfileBackupTests.Make(backups, preferences: Settings), data.Path), requested: false);
        handoff.Offer(ProfileBackup.Restore(ProfileBackupTests.Make(backups), data.Path), requested: true);
        Assert.False(handoff.Pending);

        // A restore that throws never reaches the offer: the settings in use stay as they are.
        var failing = ProfileBackupTests.Make(backups, preferences: Settings);
        Assert.Throws<IOException>(() =>
            handoff.Offer(ProfileBackup.Restore(failing, data.Path, () => throw new IOException("load failed")), requested: true));
        Assert.False(handoff.Pending);
        Assert.False(handoff.ApplyTo(_ => throw new InvalidOperationException("nothing to apply")));
    }

    [Fact]
    public void TheNewestRestoreWins()
    {
        var handoff = new RestoredPreferences();
        handoff.Offer(new RestoreResult("""{"favoriteMetricKeys":["first"]}"""), requested: true);
        handoff.Offer(new RestoreResult("""{"favoriteMetricKeys":["second"]}"""), requested: true);
        string? applied = null;
        handoff.ApplyTo(json => applied = json);
        Assert.Equal(["second"], ViewPreferences.Parse(applied).FavoriteMetricKeys);
    }
}
