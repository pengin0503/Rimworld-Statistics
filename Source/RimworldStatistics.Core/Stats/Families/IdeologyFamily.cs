namespace RimworldStatistics.Core;

/// <summary>
/// Ideology D2 confirmed transitions. Adapters must prove ritual conclusions,
/// role assignments, and explicit conversion attempts; UI actions are not completions.
/// </summary>
public static class IdeologyFamily
{
    public const string Name = "ideology";
    public const string Conversions = "ideology.conversions";
    public const string RitualResults = "ideology.ritual.results";
    public const string RitualSuccessRate = "ideology.ritual.success_rate";
    public const string ConversionDaysMean = "ideology.conversion.days_mean";
    public const string RoleAppointments = "ideology.role_appointments";

    private const string Changes = "conversions";
    private const string RitualIds = "ritual_ids";
    private const string RitualCount = "ritual_count";
    private const string Outcomes = "ritual_outcomes";
    private const string ClassifiedRituals = "ritual_classified";
    private const string SuccessfulRituals = "ritual_successful";
    private const string UnclassifiedRituals = "ritual_unclassified";
    private const string Roles = "role_appointments";
    private const string ConversionStarts = "conversion_starts";
    private const string ConversionDurations = "conversion_durations";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name, Revision = 1, RequiredDlc = AcquisitionUnits.Ideology, Build = Build,
    };

    /// <summary>Pawn generation, same-ideology assignment and NPC-only changes do not count.</summary>
    public static void Converted(FamilyState s, string pawnId, string? from, string? to,
        bool eligible, bool isWorldSpawnedOrPlayerCaravan)
    {
        if (!eligible || !isWorldSpawnedOrPlayerCaravan || string.IsNullOrEmpty(pawnId)
            || string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from == to)
            return;
        s.Count(Changes);
    }

    /// <summary>One verified outcome; an interruption is never a classified successful/failed result.</summary>
    public static void RitualConcluded(FamilyState s, string ritualId, string? outcomeDef,
        bool? successful, bool completed, bool interrupted)
    {
        if ((!completed && !interrupted) || string.IsNullOrWhiteSpace(ritualId)
            || !s.Distinct(RitualIds, ritualId))
            return;

        s.Count(RitualCount);
        var outcome = interrupted ? "interrupted"
            : string.IsNullOrWhiteSpace(outcomeDef) ? "unclassified" : outcomeDef!;
        s.CountBy(Outcomes, outcome);
        if (interrupted)
            return;
        if (successful is not { } confirmed || string.IsNullOrWhiteSpace(outcomeDef))
        {
            s.Count(UnclassifiedRituals);
            return;
        }
        s.Count(ClassifiedRituals);
        if (confirmed)
            s.Count(SuccessfulRituals);
    }

    /// <summary>Begin only an explicit action with a confirmed target; repeated starts keep the earliest tick.</summary>
    public static void ConversionActionStarted(FamilyState s, string pawnId, string targetIdeo, int tick,
        bool eligible, bool explicitAction)
    {
        if (!eligible || !explicitAction || tick < 0 || string.IsNullOrWhiteSpace(pawnId)
            || string.IsNullOrWhiteSpace(targetIdeo))
            return;
        var key = pawnId + "|" + targetIdeo;
        if (s.Tracked(ConversionStarts, key) is null)
            s.Track(ConversionStarts, key, tick);
    }

    /// <summary>Only the matching target ending a tracked attempt contributes one duration sample.</summary>
    public static void ConversionReachedTarget(FamilyState s, string pawnId, string targetIdeo, int tick, bool eligible)
    {
        if (!eligible || tick < 0 || string.IsNullOrWhiteSpace(pawnId) || string.IsNullOrWhiteSpace(targetIdeo))
            return;
        var start = s.Untrack(ConversionStarts, pawnId + "|" + targetIdeo);
        if (start is { } t && tick >= t)
            s.Observe(ConversionDurations, tick - t);
    }

    /// <summary>Actual new role, not removal, failed assignment or same-role reapplication.</summary>
    public static void RoleAppointed(FamilyState s, string pawnId, string? previousRole, string? newRole,
        bool eligible, bool confirmed)
    {
        if (eligible && confirmed && !string.IsNullOrWhiteSpace(pawnId)
            && !string.IsNullOrWhiteSpace(newRole) && previousRole != newRole)
            s.Count(Roles);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Changes, Conversions);
        var outcomes = c.State.Peek(Outcomes);
        c.PutBreakdown(RitualResults, c.Counts(outcomes),
            c.Value(integer: c.State.Peek(RitualCount)?.N ?? 0, partial: outcomes?.Overflow == true));

        var classified = c.State.Peek(ClassifiedRituals)?.N ?? 0;
        var successes = c.State.Peek(SuccessfulRituals)?.N ?? 0;
        c.Put(RitualSuccessRate, classified == 0
            ? c.Value(coverage: MetricCoverage.NotApplicable, numerator: 0, denominator: 0)
            : c.Value(real: successes / (double)classified, numerator: successes,
                denominator: classified, partial: (c.State.Peek(UnclassifiedRituals)?.N ?? 0) > 0));

        c.PutCount(Roles, RoleAppointments);
        c.PutMean(ConversionDurations, ConversionDaysMean, 1.0 / StatFormat.TicksPerDay);
        if (c.State.Peek(RitualIds)?.Overflow == true)
            c.MarkPartial(RitualResults, RitualSuccessRate);
        if (c.State.Peek(Outcomes)?.Overflow == true)
            c.MarkPartial(RitualResults);
        if (c.State.TrackerOverflowed(ConversionStarts))
            c.MarkPartial(ConversionDaysMean);
    }
}
