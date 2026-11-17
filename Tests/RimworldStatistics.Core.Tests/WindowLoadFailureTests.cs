using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Issues #322 and #326: a background build or load that fails must neither start again every few frames nor leave the
/// window loading forever. The window polls <see cref="RetainedBuild{TKey,TValue}"/> and <see cref="RetryableLoad{T}"/>
/// once per frame; the builds here finish only when the test says so.
/// </summary>
public sealed class WindowLoadFailureTests
{
    private sealed class Builds
    {
        public readonly List<TaskCompletionSource<string>> Started = new();
        public Task<string> Start()
        {
            var build = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            Started.Add(build);
            return build.Task;
        }
    }

    private static readonly InvalidOperationException Broken = new("broken view");

    [Fact]
    public void AFailedBuild_IsNotStartedAgainForTheSameRevision_AndIsReportedOnce()
    {
        var builds = new Builds();
        var build = new RetainedBuild<long, string>();
        Assert.Null(build.Update(1, builds.Start));
        builds.Started[0].SetException(Broken);

        Assert.Same(Broken, build.Update(1, builds.Start));
        for (var frame = 0; frame < 10; frame++)
            Assert.Null(build.Update(1, builds.Start));
        Assert.Single(builds.Started);
        Assert.Same(Broken, build.Failure);
        Assert.False(build.Running);
        Assert.Null(build.Value);
    }

    [Fact]
    public void AChangedRevision_BuildsAgain_WhileTheFailureStaysShownUntilTheBuildEnds()
    {
        var builds = new Builds();
        var build = new RetainedBuild<long, string>();
        build.Update(1, builds.Start);
        builds.Started[0].SetException(Broken);
        build.Update(1, builds.Start);

        build.Update(2, builds.Start);
        Assert.Equal(2, builds.Started.Count);
        Assert.True(build.Running);
        Assert.Same(Broken, build.Failure);   // no flicker between the error and "loading"
        builds.Started[1].SetResult("view at 2");
        Assert.Null(build.Update(2, builds.Start));
        Assert.Null(build.Failure);
        Assert.Equal("view at 2", build.Value);
        build.Update(2, builds.Start);
        Assert.Equal(2, builds.Started.Count);
    }

    [Fact]
    public void Retry_BuildsTheSameRevisionAgain_AndARepeatedFailureIsNotLoggedAgain()
    {
        var builds = new Builds();
        var build = new RetainedBuild<long, string>();
        build.Update(1, builds.Start);
        builds.Started[0].SetException(Broken);
        Assert.NotNull(build.Update(1, builds.Start));

        build.Retry();
        build.Update(1, builds.Start);
        Assert.Equal(2, builds.Started.Count);
        builds.Started[1].SetException(new InvalidOperationException("still broken"));
        Assert.Null(build.Update(1, builds.Start));   // the same condition: logged once
        Assert.Equal("still broken", build.Failure!.Message);

        build.Retry();
        build.Update(1, builds.Start);
        builds.Started[2].SetResult("view");
        build.Update(1, builds.Start);
        Assert.Equal("view", build.Value);
        Assert.Null(build.Failure);

        // After a success, a new failure is a new condition and is logged again.
        build.Update(2, builds.Start);
        builds.Started[3].SetException(Broken);
        Assert.Same(Broken, build.Update(2, builds.Start));
    }

    [Fact]
    public void TheNormalPath_BuildsOncePerRevision_UsesCacheHits_AndRunsOneBuildAtATime()
    {
        var builds = new Builds();
        var build = new RetainedBuild<long, string>();
        Assert.Null(build.Update(1, builds.Start, ready: "cached at 1"));
        Assert.Empty(builds.Started);
        Assert.Equal("cached at 1", build.Value);

        build.Update(2, builds.Start);
        build.Update(3, builds.Start);   // the profile changed again while the build runs: wait for it
        Assert.Single(builds.Started);
        Assert.Equal("cached at 1", build.Value);   // the older view stays shown meanwhile, as before
        builds.Started[0].SetResult("view at 2");
        build.Update(3, builds.Start);
        Assert.Equal("view at 2", build.Value);
        Assert.Equal(2, builds.Started.Count);
        builds.Started[1].SetResult("view at 3");
        build.Update(3, builds.Start);
        build.Update(3, builds.Start);   // no cache hit this frame (the profile was busy): not built again
        Assert.Equal(2, builds.Started.Count);
        Assert.Equal("view at 3", build.Value);
    }

    [Fact]
    public void Reset_ForgetsValueAndFailure_AndIgnoresTheBuildItAbandoned()
    {
        var builds = new Builds();
        var build = new RetainedBuild<long, string>();
        build.Update(1, builds.Start);
        build.Reset();   // another scope was chosen
        build.Update(1, builds.Start);
        Assert.Equal(2, builds.Started.Count);
        builds.Started[0].SetResult("old scope");
        build.Update(1, builds.Start);
        Assert.Null(build.Value);
        builds.Started[1].SetResult("new scope");
        build.Update(1, builds.Start);
        Assert.Equal("new scope", build.Value);
    }

    [Fact]
    public void CancelledAndSynchronouslyFailingBuilds_AreFailuresToo()
    {
        var builds = new Builds();
        var build = new RetainedBuild<long, string>();
        build.Update(1, builds.Start);
        builds.Started[0].SetCanceled(TestContext.Current.CancellationToken);
        Assert.IsAssignableFrom<OperationCanceledException>(build.Update(1, builds.Start));

        var throwing = new RetainedBuild<long, string>();
        var calls = 0;
        Task<string> Throw() { calls++; throw Broken; }
        Assert.Same(Broken, throwing.Update(1, Throw));
        throwing.Update(1, Throw);
        Assert.Equal(1, calls);
        Assert.False(throwing.Running);
    }

    [Fact]
    public void TheProfileRevision_ChangesWithEveryAppliedChange()
    {
        using var temp = new TempDir();
        var session = new ProfileSession(new ProfileStore(temp.Path), () => new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc));
        var before = session.Revision;
        session.Mutate(_ => { }, SaveMode.None);
        Assert.Equal(before + 1, session.Revision);
        session.MutateWithoutBlocking(_ => { });
        session.Read(_ => 0);   // a queued change is applied before any read
        Assert.Equal(before + 2, session.Revision);
    }

    [Fact]
    public void AFailedLoad_StaysFailedWhilePolled_AndRetryLoadsAgain()
    {
        var loads = 0;
        var fail = true;
        var load = new RetryableLoad<string>(() =>
        {
            loads++;
            return fail ? Task.FromException<string>(new IOException("disk busy")) : Task.FromResult("session");
        });

        for (var frame = 0; frame < 5; frame++)
        {
            Assert.Null(load.TryGet(out var failure));
            Assert.IsType<IOException>(failure);
        }
        Assert.Equal(1, loads);

        fail = false;
        load.Start(retryFailed: true);
        Assert.Equal("session", load.TryGet(out var none));
        Assert.Null(none);
        load.Start(retryFailed: true);   // a loaded value is never loaded again
        Assert.Equal(2, loads);
    }

    [Fact]
    public void ALoadInProgress_IsNotReplacedByRetry_ButARestoreCanHandOverItsSession()
    {
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var loads = 0;
        var load = new RetryableLoad<string>(() => { loads++; return pending.Task; });
        Assert.Null(load.TryGet(out var failure));
        Assert.Null(failure);
        load.Start(retryFailed: true);
        Assert.Equal(1, loads);

        load.Replace(Task.FromResult("restored"));
        Assert.Equal("restored", load.TryGet(out _));

        var throwing = new RetryableLoad<string>(() => throw new UnauthorizedAccessException("denied"));
        Assert.Null(throwing.TryGet(out var denied));
        Assert.IsType<UnauthorizedAccessException>(denied);
    }
}
