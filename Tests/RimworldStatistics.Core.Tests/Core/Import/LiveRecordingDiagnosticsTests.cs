using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Core.Tests
{
    public sealed class LiveRecordingDiagnosticsTests
    {
        [Fact]
        public void SettingsToggleChangesActiveRecordingWithoutResettingCheckpointSession()
        {
            var game = Current.Game = new Game();
            UnityData.IsInMainThread = true;
            MetricGates.Selected = [new("test-diagnostics", 1)];
            try
            {
                LiveMetrics.StartNew(game);
                var session = Assert.IsType<CheckpointSaveSession>(LiveMetrics.Session);
                var original = Assert.IsType<FamilyState>(LiveMetrics.Family("test-diagnostics"));
                original.Count("events");

                MetricGates.Selected = [];
                LiveMetrics.RefreshSelection();
                Assert.Null(LiveMetrics.Family("test-diagnostics"));
                Assert.Same(session, LiveMetrics.Session);

                MetricGates.Selected = [new("test-diagnostics", 1)];
                LiveMetrics.RefreshSelection();
                var resumed = Assert.IsType<FamilyState>(LiveMetrics.Family("test-diagnostics"));
                Assert.Equal(1, resumed.Peek("events")?.N);
                Assert.False(resumed.Continuous);
                Assert.Same(session, LiveMetrics.Session);
            }
            finally
            {
                MetricGates.Selected = [new("test-diagnostics", 1)];
                LiveMetrics.StartNew(game);
            }
        }

        [Fact]
        public void QueriesNeverCountOrCreateGapsButOffThreadEventsDo()
        {
            const string family = "test-diagnostics";
            var game = Current.Game = new Game();
            UnityData.IsInMainThread = true;
            LiveMetrics.StartNew(game);
            var state = Assert.IsType<FamilyState>(LiveMetrics.Family(family));
            Assert.Empty(LiveMetrics.HitCounts());

            LiveMetrics.EnableHitDiagnostics();
            Assert.True(LiveMetrics.IsRecordingNow(family));
            UnityData.IsInMainThread = false;
            Assert.False(LiveMetrics.IsRecordingNow(family));
            UnityData.IsInMainThread = true;
            LiveMetrics.FlushMissed();
            Assert.True(state.Continuous);
            Assert.Empty(LiveMetrics.HitCounts());

            UnityData.IsInMainThread = false;
            Assert.False(LiveMetrics.CanCaptureEvent(family));
            Assert.Null(LiveMetrics.Family(family));
            Assert.Empty(LiveMetrics.HitCounts());
            UnityData.IsInMainThread = true;
            Assert.True(LiveMetrics.IsRecordingNow(family));
            Assert.True(state.Continuous);
            LiveMetrics.FlushMissed();
            Assert.False(state.Continuous);
            Assert.Same(state, LiveMetrics.Family(family));
            Assert.Equal(1, LiveMetrics.HitCounts()[family]);

            LiveMetrics.StartNew(game);
            Assert.Empty(LiveMetrics.HitCounts());
        }

        [Fact]
        public void RecordOrDefer_QueuesOffThreadEventsWithoutGapAndDiscardsStaleGames()
        {
            const string family = "test-diagnostics";
            var game = Current.Game = new Game();
            UnityData.IsInMainThread = true;
            Scribe.mode = LoadSaveMode.Inactive;
            MetricGates.Selected = [new(family, 1)];
            try
            {
                LiveMetrics.StartNew(game);
                var state = Assert.IsType<FamilyState>(LiveMetrics.Family(family));

                UnityData.IsInMainThread = false;
                Assert.True(LiveMetrics.IsRecordingUnit(family));
                LiveMetrics.RecordOrDefer(family, s => s.Count("events", 3));
                Assert.Null(state.Peek("events"));

                UnityData.IsInMainThread = true;
                LiveMetrics.FlushDeferred();
                Assert.Equal(3, state.Peek("events")?.N);
                Assert.True(state.Continuous);

                // Events queued for a previous Game instance must never bleed into a newly started game.
                UnityData.IsInMainThread = false;
                LiveMetrics.RecordOrDefer(family, s => s.Count("events", 99));
                UnityData.IsInMainThread = true;
                var nextGame = Current.Game = new Game();
                LiveMetrics.StartNew(nextGame);
                LiveMetrics.FlushDeferred();
                var nextState = Assert.IsType<FamilyState>(LiveMetrics.Family(family));
                Assert.Null(nextState.Peek("events"));
                Assert.True(nextState.Continuous);
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                UnityData.IsInMainThread = true;
                MetricGates.Selected = [new(family, 1)];
            }
        }

        [Fact]
        public void ScribeActiveMode_SuppressesCaptureAndDeferWithoutMarkingGap()
        {
            const string family = "test-diagnostics";
            var game = Current.Game = new Game();
            UnityData.IsInMainThread = true;
            Scribe.mode = LoadSaveMode.Inactive;
            MetricGates.Selected = [new(family, 1)];
            try
            {
                LiveMetrics.StartNew(game);
                var state = Assert.IsType<FamilyState>(LiveMetrics.Family(family));

                Scribe.mode = LoadSaveMode.LoadingVars;
                UnityData.IsInMainThread = false;
                Assert.False(LiveMetrics.IsRecordingUnit(family));
                Assert.False(LiveMetrics.CanCaptureEvent(family));
                LiveMetrics.RecordOrDefer(family, s => s.Count("events", 5));

                Scribe.mode = LoadSaveMode.Inactive;
                UnityData.IsInMainThread = true;
                LiveMetrics.FlushMissed();
                Assert.Null(state.Peek("events"));
                Assert.True(state.Continuous);
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                UnityData.IsInMainThread = true;
            }
        }

        [Fact]
        public void OffThreadFail_StopsAccumulatorUnitOnMainThreadAndBlocksFurtherDeferrals()
        {
            const string family = "test-diagnostics";
            var game = Current.Game = new Game();
            UnityData.IsInMainThread = true;
            Scribe.mode = LoadSaveMode.Inactive;
            MetricGates.Selected = [new(family, 1)];
            try
            {
                LiveMetrics.StartNew(game);
                var state = Assert.IsType<FamilyState>(LiveMetrics.Family(family));
                state.Count("events", 2);

                UnityData.IsInMainThread = false;
                LiveMetrics.Fail(family, new InvalidOperationException("worker fault"));
                Assert.False(LiveMetrics.IsRecordingUnit(family));
                LiveMetrics.RecordOrDefer(family, s => s.Count("events", 10));

                UnityData.IsInMainThread = true;
                Assert.Null(LiveMetrics.Family(family));
                Assert.Equal(2, state.Peek("events")?.N);
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                UnityData.IsInMainThread = true;
                MetricGates.Selected = [new(family, 1)];
            }
        }

        [Fact]
        public void FlushDeferred_PreservesChronologicalOrderBetweenDeferredEventsAndOffThreadGaps()
        {
            const string family = "test-diagnostics";
            var game = Current.Game = new Game();
            UnityData.IsInMainThread = true;
            Scribe.mode = LoadSaveMode.Inactive;
            MetricGates.Selected = [new(family, 1)];
            try
            {
                LiveMetrics.StartNew(game);
                var state = Assert.IsType<FamilyState>(LiveMetrics.Family(family));

                // 1) Deferred event opens an active interval, 2) subsequent off-thread non-deferrable hook causes a gap.
                UnityData.IsInMainThread = false;
                LiveMetrics.RecordOrDefer(family, s => s.Active["raid"] = new System.Collections.Generic.Dictionary<string, long> { ["1"] = 50 });
                Assert.False(LiveMetrics.CanCaptureEvent(family));

                // On flush, the active interval opened BEFORE the gap must be cleared by the subsequent MarkGap!
                UnityData.IsInMainThread = true;
                LiveMetrics.FlushDeferred();
                Assert.False(state.Continuous);
                Assert.Empty(state.Active);
            }
            finally
            {
                Scribe.mode = LoadSaveMode.Inactive;
                UnityData.IsInMainThread = true;
                MetricGates.Selected = [new(family, 1)];
            }
        }
    }
}

// The linked production game bridge runs with the real Core accumulator.
// Supply only the minimal engine/context surface that it reads.
namespace Verse
{
    internal static class Current { internal static Game? Game { get; set; } }
    public sealed class Game;
    internal static class UnityData { internal static bool IsInMainThread { get; set; } }
    internal enum LoadSaveMode { Inactive = 0, Saving = 1, LoadingVars = 2, ResolvingCrossRefs = 3, PostLoadInit = 4 }
    internal static class Scribe { internal static LoadSaveMode mode { get; set; } = LoadSaveMode.Inactive; }
    internal static class Find { internal static TickManager TickManager { get; } = new(); }
    internal sealed class TickManager { internal int TicksGame => 100; }
    internal static class LoadedModManager { internal static Mod[] RunningMods => []; }
    internal sealed class Mod { internal string? PackageId => null; }
    internal static class Log { internal static void WarningOnce(string text, int key) { } }
}

namespace RimworldStatistics.Metrics
{
    internal static class MetricGates
    {
        internal static FamilySpec[] Selected = [new("test-diagnostics", 1)];
        internal static FamilySpec[] RecordingFamilies() => Selected;
        internal static IReadOnlySet<string>? RecordingExperimentalUnits() => null;
        internal static void Fail(string unit, Exception error) { }
    }

    internal static class ModProviderAdapters
    {
        internal static void AttachEventOrigins(LiveMetricAccumulator accumulator) { }
    }

    internal static class FamilyStartup
    {
        internal static void Apply(LiveMetricAccumulator accumulator, int tick, IReadOnlySet<string>? onlyFamilies = null) { }
        internal static void BeforeSave(LiveMetricAccumulator accumulator, int tick) { }
    }
}
