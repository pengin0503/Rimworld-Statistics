using System.Diagnostics;

namespace RimworldStatistics.Core.Tests.Common;

/// <summary>
/// Common performance contract, measured without wall-clock thresholds where possible: managed allocation on the test's
/// own thread (other tests running in parallel do not change it) and sizes. Growth is checked as a ratio between two sizes
/// of the same work, with room for at least twice the linear cost, so a slower or busier machine cannot fail it while a
/// quadratic cost (16 times for 4 times the work) still does. Wall-clock times are printed for the record only, with a
/// bound far above any machine (a hang or an accidental exponential path).
/// </summary>
[Trait("Category", "Contract")]
[Trait("Category", "Performance")]
public sealed class PerformanceContractTests
{
    [Fact]
    public void RecordingIntoAnExistingCounterAllocatesNothing()
    {
        var state = new FamilyState { Revision = 1 };
        var key = TestShapes.DefKey("MealFine");
        // Warm up past .NET Tiered Compilation / Dynamic PGO Tier-1 promotion so JIT metadata is not allocated mid-measurement.
        for (var i = 0; i < 2_000; i++)
        {
            OptionalTransitionRecording.RecordCount(state, TestShapes.Counter);
            OptionalTransitionRecording.RecordCount(state, "by_def", 1, key, withBreakdown: true);
        }

        var plain = Allocations.OnThisThread(() =>
        {
            for (var i = 0; i < 10_000; i++)
                OptionalTransitionRecording.RecordCount(state, TestShapes.Counter);
        });
        var byDef = Allocations.OnThisThread(() =>
        {
            for (var i = 0; i < 10_000; i++)
                OptionalTransitionRecording.RecordCount(state, "by_def", 1, key, withBreakdown: true);
        });

        Assert.Equal(0, plain);
        Assert.Equal(0, byDef);
        Assert.Equal(12_000, state.Peek(TestShapes.Counter)!.N);
        Assert.Equal(12_000, state.Peek("by_def")!.By![key]);
    }

    [Fact]
    public void ACheckpointStaysTheSameSizeHoweverManyEventsItCounts()
    {
        var metric = TestShapes.CounterMetric(breakdown: true);
        string Saved(int events)
        {
            var game = GameSession.NewGame(new TestColony("size"), TestShapes.FamilyOf(metric));
            var keys = new[] { TestShapes.DefKey("A"), TestShapes.DefKey("B"), TestShapes.DefKey("C") };
            for (var i = 0; i < events; i++)
                game.Record(metric, 1, keys[i % keys.Length]);
            return game.Save(100, "c1").Json;
        }

        var timer = Stopwatch.StartNew();
        var many = Saved(100_000);
        var elapsed = timer.Elapsed;
        var few = Saved(12);

        // Only the digits of the four totals grow (12 → 100000, 4 → 33334).
        Assert.InRange(many.Length - few.Length, 0, 4 * 5);
        Report($"100,000 recorded events and one save: {elapsed.TotalMilliseconds:F0} ms; checkpoint {many.Length} bytes");
        Assert.True(elapsed < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ManyStatisticsCostAboutLinearlyPerSave()
    {
        long Cost(int statistics)
        {
            var metrics = Enumerable.Range(0, statistics).Select(i => TestShapes.CounterMetric("f" + i, breakdown: i % 2 == 0)).ToArray();
            var game = GameSession.NewGame(new TestColony("many"), metrics.Select(TestShapes.FamilyOf).ToArray());
            foreach (var metric in metrics)
                game.Record(metric, 2, TestShapes.DefKey("A"));
            game.Save(50, "warm-up");
            var tick = 100;
            return Allocations.OnThisThread(() => game.Save(tick, "measured"));
        }

        Cost(10);
        var small = Cost(50);
        var large = Cost(200);
        Report($"save with 50 statistics: {small / 1024} KiB allocated; with 200: {large / 1024} KiB");
        Assert.InRange((double)large / small, 1, 4 * 2);
    }

    [Fact]
    public void ManyObservationsBuildTheViewAboutLinearly()
    {
        var metrics = Enumerable.Range(0, 5).Select(i => TestShapes.CounterMetric("f" + i, breakdown: true)).ToArray();
        (long Bytes, TimeSpan Time, int Rows) Cost(int saves)
        {
            var colony = new TestColony("history");
            var profile = new TestProfile().Played(colony, saves * 10).Remember(metrics);
            var game = GameSession.NewGame(colony, metrics.Select(TestShapes.FamilyOf).ToArray());
            for (var i = 1; i <= saves; i++)
            {
                foreach (var metric in metrics)
                    game.Record(metric, 1, TestShapes.DefKey("D" + (i % 7)));
                profile.Import(game.Save(i * 10, "s" + i));
            }
            profile.View();
            var timer = Stopwatch.StartNew();
            ProfileView? view = null;
            var bytes = Allocations.OnThisThread(() => view = profile.View());
            return (bytes, timer.Elapsed, view!.Colony(colony).Rows.Count);
        }

        Cost(20);
        var small = Cost(100);
        var large = Cost(400);
        Report($"view of 100 saves: {small.Bytes / 1024} KiB, {small.Time.TotalMilliseconds:F0} ms; "
            + $"of 400 saves: {large.Bytes / 1024} KiB, {large.Time.TotalMilliseconds:F0} ms");
        Assert.Equal(small.Rows, large.Rows);
        Assert.InRange((double)large.Bytes / small.Bytes, 1, 4 * 2);
        Assert.True(large.Time < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void RepeatedSavesAndImportsKeepTheProfileBoundedAndUnchangedOnReimport()
    {
        var metrics = Enumerable.Range(0, 5).Select(i => TestShapes.CounterMetric("f" + i, breakdown: true)).ToArray();
        var colony = new TestColony("repeat");
        var profile = new TestProfile().Played(colony, 2000).Remember(metrics);
        var game = GameSession.NewGame(colony, metrics.Select(TestShapes.FamilyOf).ToArray());
        var saves = new List<SavedCheckpoint>();
        var sizes = new Dictionary<int, int>();
        var timer = Stopwatch.StartNew();
        for (var i = 1; i <= 200; i++)
        {
            foreach (var metric in metrics)
                game.Record(metric, 1, TestShapes.DefKey("D" + (i % 7)));
            var save = game.Save(i * 10, "s" + i);
            saves.Add(save);
            profile.Import(save);   // as the game does after each successful save
            if (i is 50 or 200)
                sizes[i] = ProfileJson.Serialize(profile.Profile).Length;
        }
        var importTime = timer.Elapsed;
        var perStatisticAndSave = (sizes[200] - sizes[50]) / 150.0 / metrics.Length;

        var before = ProfileJson.Serialize(profile.Profile);
        timer.Restart();
        profile.Import([.. saves]);   // every save file read again
        var reimportTime = timer.Elapsed;

        Assert.Equal(before, ProfileJson.Serialize(profile.Profile));
        Assert.Equal(200, profile.Profile.MetricObservations.Count(o => o.Source == MetricObservationSource.Checkpoint));
        Assert.Equal(200, profile.View().Row(colony, metrics[0].StableKey).Value.Number);
        Report($"200 saves imported in {importTime.TotalMilliseconds:F0} ms, again in {reimportTime.TotalMilliseconds:F0} ms; "
            + $"profile {sizes[200] / 1024} KiB, {perStatisticAndSave:F0} bytes per extra save and statistic");
        // Optional MOD values are kept in every checkpoint of the profile (the archive moves only built-in counters), so
        // the profile grows with saves × MOD statistics: about 0.9 KiB here for a counter with 7 Defs. The bound is about
        // twice that; more means each save stores more than its own values.
        Assert.InRange(perStatisticAndSave, 0, 2048);
        Assert.True(importTime + reimportTime < TimeSpan.FromSeconds(120));
    }

    private static void Report(string message) => TestContext.Current.SendDiagnosticMessage(message);
}
