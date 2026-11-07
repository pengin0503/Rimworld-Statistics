namespace RimworldStatistics.Core;

/// <summary>Phase C · research (ledger Q-001…Q-004, N3-W-011, N5-M-004; evidence #research).</summary>
public static class ResearchFamily
{
    public const string Name = "research";
    public const string Completed = "research.live.completed";
    public const string CompletedTicks = "research.live.completed_ticks";
    public const string DurationMin = "research.live.duration_min";
    public const string DurationMax = "research.live.duration_max";
    public const string FirstDay = "research.live.first_day";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Done = "done";        // By: def → tick of its first completion while tracked
    private const string Start = "start";      // active: def → tick of the first real work on it
    private const string Duration = "duration";

    /// <summary>Real positive progress on a project that was not finished before it.</summary>
    public static void Worked(FamilyState s, string def, int tick)
    {
        if (s.Peek(Done)?.By?.ContainsKey(def) == true || s.Tracked(Start, def) is not null)
            return;
        s.Track(Start, def, tick);
    }

    /// <summary>A project went from not finished to finished: counted once per def while tracked.</summary>
    public static void Finished(FamilyState s, string def, int tick)
    {
        var done = s.Acc(Done);
        done.By ??= new Dictionary<string, double>(StringComparer.Ordinal);
        if (done.By.ContainsKey(def))
            return;
        var start = s.Untrack(Start, def);
        if (!s.CountBy(Done, def, tick))   // a new key holds its completion tick
            return;                        // past the cap: the count is partial from here
        if (start is { } st && tick >= st)
            s.Observe(Duration, tick - st, def);
    }

    private static void Build(FamilyOutputContext c)
    {
        var done = c.State.Peek(Done);
        var by = done?.By ?? new Dictionary<string, double>(StringComparer.Ordinal);
        var overflow = done?.Overflow == true;
        var count = c.Value(integer: by.Count, partial: overflow);
        c.Put(Completed, count);
        c.PutBreakdown(CompletedTicks, by.ToDictionary(kv => kv.Key, kv => c.Value(real: kv.Value / StatFormat.TicksPerDay, partial: overflow), StringComparer.Ordinal), count);

        c.PutExtreme(Duration, DurationMin, max: false, 1.0 / StatFormat.TicksPerDay);
        c.PutExtreme(Duration, DurationMax, max: true, 1.0 / StatFormat.TicksPerDay);

        // Only a range recorded from the start of the game knows the first completion (overflow cannot hide an earlier one:
        // the set fills in completion order).
        c.Put(FirstDay, !c.FromStart
            ? c.Value(coverage: MetricCoverage.Unknown)
            : c.Value(real: by.Count == 0 ? null : by.Values.Min() / StatFormat.TicksPerDay));
    }
}

/// <summary>
/// The call scopes of the research hooks (evidence #research): the outer progress calls (ResearchPerformed, AddProgress,
/// ApplyKnowledge) record the state before progress changes, because FinishProject is reached only after the project already
/// counts as finished. FinishProject inside such a call uses that earlier state; any other FinishProject (direct completion,
/// a prerequisite completed on the way) uses the state at its own entry. Game-thread only, not persisted.
/// </summary>
public sealed class ResearchHooks
{
    public sealed class Frame
    {
        internal Frame(string def, bool finishedBefore, double progressBefore)
        {
            Def = def;
            FinishedBefore = finishedBefore;
            ProgressBefore = progressBefore;
        }

        public string Def { get; }
        public bool FinishedBefore { get; }
        public double ProgressBefore { get; }
    }

    private readonly List<Frame> _frames = new();

    public Frame OuterBegin(string def, bool finishedBefore, double progressBefore)
    {
        var f = new Frame(def, finishedBefore, progressBefore);
        _frames.Add(f);
        return f;
    }

    /// <summary>Ends an outer call (also when it threw): real progress on an unfinished project is its first work.</summary>
    public void OuterEnd(FamilyState? s, Frame frame, double progressAfter, int tick)
    {
        var i = _frames.LastIndexOf(frame);
        if (i < 0)
            return;
        _frames.RemoveRange(i, _frames.Count - i);   // frames above it ended with an exception
        if (s != null && !frame.FinishedBefore && progressAfter > frame.ProgressBefore)
            ResearchFamily.Worked(s, frame.Def, tick);
    }

    /// <summary>Returns whether the project counted as finished before this completion began.</summary>
    public bool FinishBegin(FamilyState? s, string def, bool finishedNow, double progressNow, int tick)
    {
        for (var i = _frames.Count - 1; i >= 0; i--)
        {
            var f = _frames[i];
            if (f.Def != def)
                continue;
            // The same call made real progress and now finishes the project: that progress is its first work if none was seen.
            if (s != null && !f.FinishedBefore && progressNow > f.ProgressBefore)
                ResearchFamily.Worked(s, def, tick);
            return f.FinishedBefore;
        }
        return finishedNow;
    }

    public void FinishEnd(FamilyState? s, string def, bool wasFinished, bool finishedNow, int tick)
    {
        if (s != null && !wasFinished && finishedNow)
            ResearchFamily.Finished(s, def, tick);
    }
}
