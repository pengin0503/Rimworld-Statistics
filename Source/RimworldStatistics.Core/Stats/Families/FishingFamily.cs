namespace RimworldStatistics.Core;

/// <summary>GEN-FISH-001/002: actually placed catch amounts, one successful action per fishing job.</summary>
public static class FishingFamily
{
    public const string Name = "fishing";
    public const string SuccessfulActions = "exploration.fishing.successful_actions";
    public const string AcceptedUnitsByDef = "exploration.fishing.accepted_units_by_def";
    private const string Actions = "success_jobs";
    private const string Units = "placed_units";
    private const string SeenJobs = "seen_jobs";
    private const string SeenStages = "seen_stages";
    private const int ReconciliationWindow = 60000;

    public static LiveFamily Family { get; } = new()
    {
        Name = Name, Revision = 1, RequiredDlc = AcquisitionUnits.Odyssey, Build = Build
    };

    /// <summary>One completed and accepted placement phase. The optional extra phase adds units, not another action.</summary>
    public static void RecordAccepted(FamilyState state, string? jobId, string? stage,
        IReadOnlyDictionary<string, int> acceptedByDef, int tick)
    {
        if (string.IsNullOrWhiteSpace(jobId) || string.IsNullOrWhiteSpace(stage)
            || acceptedByDef is null || tick < 0)
            return;
        var valid = acceptedByDef.Where(kv => !string.IsNullOrWhiteSpace(kv.Key) && kv.Value > 0).ToArray();
        if (valid.Length == 0)
            return;
        Prune(state, SeenJobs, tick);
        Prune(state, SeenStages, tick);
        var stageKey = jobId + ":" + stage;
        if (state.Tracked(SeenStages, stageKey) is not null)
            return;
        if (!state.Track(SeenStages, stageKey, tick))
            return;
        if (state.Tracked(SeenJobs, jobId) is null)
        {
            if (!state.Track(SeenJobs, jobId, tick))
            {
                state.Untrack(SeenStages, stageKey);
                return;
            }
            state.Count(Actions);
        }
        foreach (var (def, units) in valid)
        {
            state.Count(Units, units);
            state.CountBy(Units, def, units);
        }
    }

    private static void Prune(FamilyState state, string tracker, int tick)
    {
        if (!state.Active.TryGetValue(tracker, out var values) || values.Count < 1024)
            return;
        var expired = values.Where(kv => kv.Value < tick - ReconciliationWindow)
            .Select(kv => kv.Key).ToArray();
        foreach (var id in expired)
            state.Untrack(tracker, id);
    }

    private static void Build(FamilyOutputContext context)
    {
        var missed = context.State.TrackerOverflowed(SeenJobs) ||
            context.State.TrackerOverflowed(SeenStages);
        context.Put(SuccessfulActions,
            context.Value(integer: context.State.Peek(Actions)?.N ?? 0, partial: missed));
        var acc = context.State.Peek(Units);
        context.PutBreakdown(AcceptedUnitsByDef, context.Counts(acc),
            context.Value(integer: acc?.N ?? 0, partial: missed));
        if (missed)
            context.MarkPartial(AcceptedUnitsByDef);
    }
}
