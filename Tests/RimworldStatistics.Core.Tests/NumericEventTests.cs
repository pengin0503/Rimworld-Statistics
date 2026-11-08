namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Phase C1: the research and mental families (metrics ledger "Phase C · research", "Phase C · mental"; evidence #research,
/// #mental; plan C1). The game adapters pass plain facts; these tests fix the decisions made from them.
/// </summary>
public sealed class NumericEventTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);

    private static LiveMetricAccumulator NewGame(params LiveFamily[] families) => LiveMetricAccumulator.StartNew(families.Select(f => f.Spec).ToList(), 0);

    private static MetricObservation Save(LiveMetricAccumulator acc, int tick)
    {
        var d = acc.PrepareSave(tick, T0, "S" + tick)!;
        acc.Commit(d);
        return CheckpointMetrics.ToObservation(d.Payload!, d.Json, new ColonyIdentity { ModCampaignId = "c" }, null, null);
    }

    private static MetricValue? V(MetricObservation o, string key) => o.Scalar(key, MetricKeys.Revision(key));

    private static Dictionary<string, MetricValue> Breakdown(MetricObservation o, string key)
        => o.Metrics[key].Values.Where(kv => kv.Key != MetricEntry.Scalar).ToDictionary(kv => kv.Key, kv => kv.Value);

    // ----- research -----

    /// <summary>One ResearchPerformed call as the game makes it: progress first, then FinishProject when the cost is reached.</summary>
    private static void Perform(ResearchHooks hooks, FamilyState s, string def, double before, double after, bool finishes, int tick)
    {
        var frame = hooks.OuterBegin(def, finishedBefore: false, progressBefore: before);
        if (finishes)
        {
            var was = hooks.FinishBegin(s, def, finishedNow: true, progressNow: after, tick);
            hooks.FinishEnd(s, def, was, finishedNow: true, tick);
        }
        hooks.OuterEnd(s, frame, progressAfter: after, tick);
    }

    [Fact]
    public void Research_NormalWorkCrossingTheCost_CountsOnceWithItsDuration()
    {
        var acc = NewGame(ResearchFamily.Family);
        var s = acc.Family(ResearchFamily.Name)!;
        var hooks = new ResearchHooks();
        Perform(hooks, s, "Electricity", 0, 10, false, 60000);
        Perform(hooks, s, "Electricity", 10, 20, false, 90000);
        Perform(hooks, s, "Electricity", 20, 30, true, 180000);   // progress reaches the cost inside the call
        // The game calls FinishProject again on a finished project (a quest reward, a re-call): not a new completion.
        var was = hooks.FinishBegin(s, "Electricity", finishedNow: true, progressNow: 30, 200000);
        hooks.FinishEnd(s, "Electricity", was, true, 200000);
        var o = Save(acc, 240000);
        Assert.Equal(1, V(o, ResearchFamily.Completed)!.IntegerValue);
        Assert.Equal(2.0, V(o, ResearchFamily.DurationMin)!.RealValue);          // (180000 − 60000) ÷ 60000
        Assert.Equal(2.0, V(o, ResearchFamily.DurationMax)!.RealValue);
        Assert.Equal(3.0, V(o, ResearchFamily.FirstDay)!.RealValue);            // a completion tick, not a duration
        Assert.Equal(3.0, Breakdown(o, ResearchFamily.CompletedTicks)["Electricity"].RealValue);
        Assert.Equal(MetricCoverage.Complete, V(o, ResearchFamily.Completed)!.Coverage);
    }

    [Fact]
    public void Research_FirstWorkThatAlsoFinishes_UsesThatTickAsTheStart()
    {
        var acc = NewGame(ResearchFamily.Family);
        var s = acc.Family(ResearchFamily.Name)!;
        Perform(new ResearchHooks(), s, "Cheap", 0, 5, true, 120000);
        var o = Save(acc, 130000);
        Assert.Equal(0.0, V(o, ResearchFamily.DurationMin)!.RealValue);
        Assert.Equal(1, V(o, ResearchFamily.DurationMin)!.SampleCount);
    }

    [Fact]
    public void Research_DirectCompletionAndPrerequisites_CountWithoutADurationSample()
    {
        var acc = NewGame(ResearchFamily.Family);
        var s = acc.Family(ResearchFamily.Name)!;
        var hooks = new ResearchHooks();
        Perform(hooks, s, "Smithing", 0, 10, false, 60000);
        // A direct FinishProject(Machining) (no outer progress call) first completes its unfinished prerequisite Smithing.
        var outer = hooks.FinishBegin(s, "Machining", finishedNow: false, progressNow: 0, 120000);
        var inner = hooks.FinishBegin(s, "Smithing", finishedNow: false, progressNow: 10, 120000);
        hooks.FinishEnd(s, "Smithing", inner, finishedNow: true, 120000);
        hooks.FinishEnd(s, "Machining", outer, finishedNow: true, 120000);
        var o = Save(acc, 130000);
        Assert.Equal(2, V(o, ResearchFamily.Completed)!.IntegerValue);
        Assert.Equal(1, V(o, ResearchFamily.DurationMin)!.SampleCount);         // only Smithing had an observed start
        Assert.Equal(1.0, V(o, ResearchFamily.DurationMax)!.RealValue);
        Assert.Equal(new[] { "Machining", "Smithing" }, Breakdown(o, ResearchFamily.CompletedTicks).Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Research_ProgressOnAFinishedProjectOrNoProgress_IsNotWork()
    {
        var acc = NewGame(ResearchFamily.Family);
        var s = acc.Family(ResearchFamily.Name)!;
        var hooks = new ResearchHooks();
        var f = hooks.OuterBegin("Done", finishedBefore: true, progressBefore: 30);
        var was = hooks.FinishBegin(s, "Done", finishedNow: true, progressNow: 30, 100);
        hooks.FinishEnd(s, "Done", was, true, 100);
        hooks.OuterEnd(s, f, 30, 100);
        var z = hooks.OuterBegin("Stalled", finishedBefore: false, progressBefore: 4);
        hooks.OuterEnd(s, z, 4, 200);   // research speed 0: no real progress
        var o = Save(acc, 300);
        Assert.Equal(0, V(o, ResearchFamily.Completed)!.IntegerValue);
        Assert.Null(s.Tracked("start", "Stalled"));
        Assert.True(V(o, ResearchFamily.FirstDay)!.Known());                  // known range, not reached
        Assert.Null(V(o, ResearchFamily.FirstDay)!.RealValue);
        Assert.Equal(0, V(o, ResearchFamily.DurationMin)!.SampleCount ?? 0);
        Assert.Null(V(o, ResearchFamily.DurationMin)!.RealValue);
    }

    [Fact]
    public void Research_StartSurvivesALoad_AndALateStartHasNoFirstDay()
    {
        var acc = NewGame(ResearchFamily.Family);
        var hooks = new ResearchHooks();
        Perform(hooks, acc.Family(ResearchFamily.Name)!, "Long", 0, 1, false, 60000);
        var d = acc.PrepareSave(70000, T0, "P")!;
        var loaded = LiveMetricAccumulator.Resume(d.Json, new[] { ResearchFamily.Family.Spec }, 70000);
        Perform(new ResearchHooks(), loaded.Family(ResearchFamily.Name)!, "Long", 1, 9, true, 300000);
        var o = Save(loaded, 310000);
        Assert.Equal(4.0, V(o, ResearchFamily.DurationMax)!.RealValue);

        var late = LiveMetricAccumulator.Resume(null, new[] { ResearchFamily.Family.Spec }, 500000);
        Perform(new ResearchHooks(), late.Family(ResearchFamily.Name)!, "X", 0, 9, true, 600000);
        var lo = Save(late, 610000);
        Assert.Equal(MetricCoverage.Partial, V(lo, ResearchFamily.Completed)!.Coverage);
        Assert.Equal(MetricCoverage.Unknown, V(lo, ResearchFamily.FirstDay)!.Coverage);
    }

    // ----- mental -----

    [Fact]
    public void Mental_CountsOnlyMoodCausedSuccessesOfFreeColonists()
    {
        var acc = NewGame(MentalFamily.Family);
        var s = acc.Family(MentalFamily.Name)!;
        MentalFamily.Break(s, "Berserk", success: true, moodCaused: true, freeColonist: true);
        MentalFamily.Break(s, "Berserk", success: false, moodCaused: true, freeColonist: true);     // failed
        MentalFamily.Break(s, "Catatonic", success: true, moodCaused: false, freeColonist: true);   // psilocap, blood rage
        MentalFamily.Break(s, "Wander_Sad", success: true, moodCaused: true, freeColonist: false);  // slave, prisoner, raider
        MentalFamily.Break(s, "Wander_Sad", success: true, moodCaused: true, freeColonist: true);
        var o = Save(acc, 1000);
        Assert.Equal(2, V(o, MentalFamily.Breaks)!.IntegerValue);
        var by = Breakdown(o, MentalFamily.BreaksByDef);
        Assert.Equal((1L, 1L), (by["Berserk"].IntegerValue!.Value, by["Wander_Sad"].IntegerValue!.Value));
        Assert.Equal(2, V(o, MentalFamily.BreaksByDef)!.IntegerValue);
    }

    [Fact]
    public void Mental_InspirationsCountRealStartsOnly()
    {
        var acc = NewGame(MentalFamily.Family);
        var s = acc.Family(MentalFamily.Name)!;
        MentalFamily.Inspiration(s, "Inspired_Creativity", success: true, started: true, freeColonist: true, debug: false);
        MentalFamily.Inspiration(s, "Inspired_Trade", success: false, started: false, freeColonist: true, debug: false);   // already inspired
        MentalFamily.Inspiration(s, "Inspired_Trade", success: true, started: true, freeColonist: true, debug: true);      // dev "Debug gain"
        MentalFamily.Inspiration(s, "Inspired_Trade", success: true, started: true, freeColonist: false, debug: false);    // a prisoner
        MentalFamily.Inspiration(s, "Inspired_Trade", success: true, started: false, freeColonist: true, debug: false);    // no state change
        var o = Save(acc, 1000);
        Assert.Equal(1, V(o, MentalFamily.Inspirations)!.IntegerValue);
        Assert.Equal(new[] { "Inspired_Creativity" }, Breakdown(o, MentalFamily.InspirationsByDef).Keys);
    }

    [Fact]
    public void OneFamilyOff_TheOtherStillRecordsAndSaves()
    {
        var acc = NewGame(ResearchFamily.Family);   // the mental adapter failed to apply: not recording
        Assert.Null(acc.Family(MentalFamily.Name));
        Perform(new ResearchHooks(), acc.Family(ResearchFamily.Name)!, "A", 0, 9, true, 100);
        var o = Save(acc, 200);
        Assert.Equal(1, V(o, ResearchFamily.Completed)!.IntegerValue);
        Assert.False(o.Metrics.ContainsKey(MentalFamily.Breaks));
    }

    [Fact]
    public void Catalog_HasEveryC1Output()
    {
        foreach (var key in new[]
                 {
                     "research.live.completed", "research.live.completed_ticks", "research.live.duration_min", "research.live.duration_max",
                     "research.live.first_day", "mental.breaks.count", "mental.breaks.by_def", "mental.inspiration.count", "mental.inspiration.by_def",
                 })
        {
            var d = MetricRegistry.Find(key);
            Assert.True(d is { Source: MetricObservationSource.Checkpoint, Published: false }, key);
        }
        Assert.Contains("N3-W-011", MetricRegistry.Find("research.live.duration_max")!.LedgerIds);
    }
}

internal static class MetricValueTestExtensions
{
    /// <summary>A value, a "not reached" or a known 0 ÷ 0 (as the view treats it).</summary>
    public static bool Known(this MetricValue v)
        => v.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable);
}
