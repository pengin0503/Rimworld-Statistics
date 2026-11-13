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
    }
}

// The linked production game bridge runs with the real Core accumulator.
// Supply only the minimal engine/context surface that it reads.
namespace Verse
{
    internal static class Current { internal static Game? Game { get; set; } }
    public sealed class Game;
    internal static class UnityData { internal static bool IsInMainThread { get; set; } }
    internal static class Find { internal static TickManager TickManager { get; } = new(); }
    internal sealed class TickManager { internal int TicksGame => 100; }
    internal static class LoadedModManager { internal static Mod[] RunningMods => []; }
    internal sealed class Mod { internal string? PackageId => null; }
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
