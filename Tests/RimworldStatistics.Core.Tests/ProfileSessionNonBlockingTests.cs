using System.Diagnostics;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Issue #195 / F-14: the game thread records each save into the profile. While another thread holds the profile
/// (a background write serializing it, an import, a view being built) it must not wait. The change is then applied
/// later, in order, before anything else reads or writes the profile.
/// </summary>
public sealed class ProfileSessionNonBlockingTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 8, 0, 0, DateTimeKind.Utc);

    private static SaveObservation Obs(string file) => new() { FileName = file, Status = ObservationStatus.Ok };

    private static Profile Reload(TempDir dir) => new ProfileStore(dir.Path).Load(Now).Profile;

    /// <summary>Holds the profile on another thread until disposed, like a long background serialization.</summary>
    private sealed class Holder : IDisposable
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly Task _task;

        public Holder(ProfileSession session)
        {
            _task = Task.Run(() => session.Read(_ =>
            {
                _entered.Set();
                _release.Wait(TimeSpan.FromSeconds(30));
                return 0;
            }));
            Assert.True(_entered.Wait(TimeSpan.FromSeconds(30)));
        }

        public void Dispose()
        {
            _release.Set();
            _task.Wait(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public void AChangeWhileTheProfileIsBusyDoesNotWaitAndIsSeenByTheNextReader()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        var elapsed = TimeSpan.Zero;
        using (new Holder(session))
        {
            var watch = Stopwatch.StartNew();
            session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("during.rws")));
            elapsed = watch.Elapsed;
        }

        Assert.True(elapsed < TimeSpan.FromSeconds(1), $"waited {elapsed.TotalMilliseconds} ms");
        Assert.Equal("during.rws", session.Read(p => p.SaveObservations.Single().FileName));
    }

    [Fact]
    public void QueuedChangesKeepTheirOrderBeforeLaterChangesFromAnyThread()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        using (new Holder(session))
        {
            session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("1.rws")));
            session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("2.rws")));
        }
        session.Mutate(p => p.SaveObservations.Add(Obs("3.rws")), SaveMode.None);
        session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("4.rws")));

        Assert.Equal(new[] { "1.rws", "2.rws", "3.rws", "4.rws" }, session.Read(p => p.SaveObservations.Select(o => o.FileName).ToArray()));
    }

    [Fact]
    public void AQueuedChangeIsWrittenToTheFile()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        using (new Holder(session))
            session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("queued.rws")));

        session.Flush();

        Assert.Equal("queued.rws", Reload(dir).SaveObservations.Single().FileName);
        Assert.Null(session.LastSaveError);
    }

    [Fact]
    public void CloseAppliesAndWritesQueuedChanges()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        using (new Holder(session))
            session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("before-close.rws")));

        session.Close();

        Assert.Equal("before-close.rws", Reload(dir).SaveObservations.Single().FileName);
    }

    [Fact]
    public void AChangeWhenTheProfileIsFreeIsAppliedAndSavedAtOnce()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);

        session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("free.rws")));

        Assert.Equal("free.rws", session.Read(p => p.SaveObservations.Single().FileName));
        session.Flush();
        Assert.Equal("free.rws", Reload(dir).SaveObservations.Single().FileName);
    }

    [Fact]
    public void AFailingQueuedChangeIsRecordedWithoutStoppingTheOthersOrTheReader()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        using (new Holder(session))
        {
            session.MutateWithoutBlocking(_ => throw new InvalidOperationException("broken change"));
            session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("after.rws")));
        }

        Assert.Equal("after.rws", session.Read(p => p.SaveObservations.Single().FileName));
        Assert.Equal("broken change", Assert.IsType<InvalidOperationException>(session.LastQueuedMutationError).Message);
    }

    [Fact]
    public void AReaderInsideAChangeDoesNotApplyQueuedChangesInTheMiddleOfIt()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        var token = TestContext.Current.CancellationToken;
        using var inside = new ManualResetEventSlim();
        using var queued = new ManualResetEventSlim();
        var observed = -1;
        var holder = new Thread(() => session.Mutate(p =>
        {
            p.SaveObservations.Add(Obs("outer-start.rws"));
            inside.Set();
            queued.Wait(TimeSpan.FromSeconds(30), token);
            observed = session.Read(q => q.SaveObservations.Count);   // re-entrant read inside the change
            p.SaveObservations.Add(Obs("outer-end.rws"));
        }, SaveMode.None));
        holder.Start();
        Assert.True(inside.Wait(TimeSpan.FromSeconds(30), token));
        session.MutateWithoutBlocking(p => p.SaveObservations.Add(Obs("queued.rws")));
        queued.Set();
        Assert.True(holder.Join(TimeSpan.FromSeconds(30)));

        Assert.Equal(1, observed);
        Assert.Equal(new[] { "outer-start.rws", "outer-end.rws", "queued.rws" },
            session.Read(p => p.SaveObservations.Select(o => o.FileName).ToArray()));
    }

    [Fact]
    public void ManyChangesDuringConstantBackgroundWritesAreAllKept()
    {
        using var dir = new TempDir();
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now);
        session.Mutate(p => p.SaveObservations.Add(Obs("counter.rws")), SaveMode.Immediate);
        var stop = 0;
        var writers = Enumerable.Range(0, 3).Select(w => new Thread(() =>
        {
            var i = 0;
            while (Volatile.Read(ref stop) == 0)
            {
                session.Mutate(p => p.ImportState.LastScanUtc = Now.AddSeconds(++i), SaveMode.Background);
                _ = session.GetView(new StatsOptions());
            }
        })).ToArray();
        foreach (var writer in writers)
            writer.Start();

        for (var i = 0; i < 300; i++)
            session.MutateWithoutBlocking(p => p.SaveObservations[0].Values.NumRaidsEnemy++);
        Volatile.Write(ref stop, 1);
        Assert.All(writers, w => Assert.True(w.Join(TimeSpan.FromSeconds(60))));
        session.Flush();

        Assert.Equal(300, session.Read(p => p.SaveObservations[0].Values.NumRaidsEnemy));
        Assert.Equal(300, Reload(dir).SaveObservations[0].Values.NumRaidsEnemy);
        Assert.Null(session.LastQueuedMutationError);
        Assert.Null(session.LastSaveError);
    }
}
