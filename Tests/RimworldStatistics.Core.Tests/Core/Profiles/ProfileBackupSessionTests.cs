using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class ProfileBackupSessionTests
{
    [Fact]
    public void ClosedSessionNeverWritesAgain()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        var session = new ProfileSession(store, () => DateTime.UtcNow);
        session.Mutate(p => p.ImportState.LastScanUtc = DateTime.UnixEpoch, SaveMode.None);
        session.Close();
        var bytes = File.ReadAllBytes(store.ProfilePath);
        session.Mutate(p => p.ImportState.LastScanUtc = DateTime.UtcNow, SaveMode.Immediate);
        session.Flush();
        Assert.Equal(bytes, File.ReadAllBytes(store.ProfilePath));
    }

    [Fact]
    public void WritesPausedSeesFlushedProfile()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        var session = new ProfileSession(store, () => DateTime.UtcNow);
        session.Mutate(p => p.ImportState.LastScanUtc = DateTime.UnixEpoch, SaveMode.None);
        var scan = session.WithWritesPaused(() => ProfileJson.Deserialize(File.ReadAllText(store.ProfilePath)).ImportState.LastScanUtc);
        Assert.Equal(DateTime.UnixEpoch, scan);
    }

    [Fact]
    public void BackgroundWriteWaitsForPause()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        var session = new ProfileSession(store, () => DateTime.UtcNow);
        session.Mutate(p => p.ImportState.LastScanUtc = DateTime.UnixEpoch, SaveMode.Immediate);
        session.WithWritesPaused(() =>
        {
            session.Mutate(p => p.ImportState.LastScanUtc = DateTime.UnixEpoch.AddDays(1), SaveMode.Background);
            Assert.Equal(DateTime.UnixEpoch, ProfileJson.Deserialize(File.ReadAllText(store.ProfilePath)).ImportState.LastScanUtc);
            return true;
        });
        session.Flush();
        Assert.Equal(DateTime.UnixEpoch.AddDays(1), ProfileJson.Deserialize(File.ReadAllText(store.ProfilePath)).ImportState.LastScanUtc);
    }
}
