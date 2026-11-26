using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class FileLockRetryTests
{
    [Fact]
    public void LockReleasedDuringTheRetryIsAcquired()
    {
        using var dir = new TempDir();
        var path = dir.File("profile.json.lock");
        var held = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var pauses = 0;

        using var acquired = FileLockRetry.Acquire(path, attempts: 6, initialDelay: TimeSpan.Zero,
            pause: _ =>
            {
                pauses++;
                held.Dispose();   // the other session finishes while we wait
            });

        Assert.Equal(1, pauses);
        Assert.True(File.Exists(path));
        held.Dispose();
    }

    [Fact]
    public void LockHeldPastTheBoundThrowsAfterSixAttempts()
    {
        using var dir = new TempDir();
        var path = dir.File("profile.json.lock");
        using var held = File.Open(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var pauses = 0;

        Assert.Throws<IOException>(() => FileLockRetry.Acquire(path, attempts: 6, initialDelay: TimeSpan.Zero,
            pause: _ => pauses++));

        Assert.Equal(5, pauses);
    }
}
