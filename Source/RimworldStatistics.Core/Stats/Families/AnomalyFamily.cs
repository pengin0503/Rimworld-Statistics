namespace RimworldStatistics.Core;

/// <summary>
/// Anomaly DLC confirmed events: completed player psychic rituals (with package-qualified
/// PsychicRitualDef breakdown), holding-platform entity escapes, newly contained entities,
/// and cleared/collapsed pit gates.
/// </summary>
public static class AnomalyFamily
{
    public const string Name = "anomaly";
    public const string PsychicRituals = "anomaly.psychic_rituals";
    public const string PsychicRitualsByDef = "anomaly.psychic_rituals_by_def";
    public const string EntityEscapes = "anomaly.entity_escapes";
    public const string EntitiesContained = "anomaly.entities_contained";
    public const string PitGatesCollapsed = "anomaly.pit_gates_collapsed";

    private const string CompletedRitualIds = "completed_ritual_ids";
    private const string RitualCounts = "rituals";
    private const string RitualUnknown = "rituals_unknown";
    private const string Escapes = "escapes";
    private const string ContainedBaselineSet = "contained_seen";
    private const string ContainedCount = "contained_count";
    private const string CollapsedGateIds = "collapsed_gates";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name,
        Revision = 1,
        RequiredDlc = AcquisitionUnits.Anomaly,
        Build = Build,
    };

    /// <summary>
    /// Seeds entities already held on player platforms when the family starts fresh so
    /// pre-existing captives and subsequent platform-to-platform transfers are not counted as new captures.
    /// </summary>
    public static void BaselineHeldEntities(FamilyState s, IEnumerable<string> heldPawnIds)
    {
        foreach (var id in heldPawnIds)
            if (!string.IsNullOrWhiteSpace(id))
                s.Distinct(ContainedBaselineSet, id);
    }

    /// <summary>Records one completed player-initiated psychic ritual.</summary>
    public static void PsychicRitualCompleted(
        FamilyState s,
        string ritualEventId,
        string? qualifiedRitualDef,
        bool playerRitual,
        bool completed)
    {
        if (!playerRitual || !completed || string.IsNullOrWhiteSpace(ritualEventId))
            return;
        if (!s.Distinct(CompletedRitualIds, ritualEventId))
            return;
        var bucket = string.IsNullOrWhiteSpace(qualifiedRitualDef) ? MetricKeys.UnknownBucket : qualifiedRitualDef!;
        s.Count(RitualCounts);
        s.CountBy(RitualCounts, bucket);
        if (bucket == MetricKeys.UnknownBucket)
            s.Count(RitualUnknown);
    }

    /// <summary>Records a living entity escaping from a player-owned holding platform.</summary>
    public static void EntityEscaped(FamilyState s, string pawnId, bool playerPlatform, bool escapedFromPlatform)
    {
        if (!playerPlatform || !escapedFromPlatform || string.IsNullOrWhiteSpace(pawnId))
            return;
        s.Count(Escapes);
    }

    /// <summary>
    /// Records an entity newly secured on a player-owned holding platform. Distinct Pawn ID tracking
    /// prevents platform-to-platform transfers from double counting while still counting newly spawned
    /// pawns converted from items such as RevenantSpine.
    /// </summary>
    public static void EntityContained(FamilyState s, string pawnId, bool playerPlatform, bool livingEntityOnPlatform)
    {
        if (!playerPlatform || !livingEntityOnPlatform || string.IsNullOrWhiteSpace(pawnId))
            return;
        if (s.Distinct(ContainedBaselineSet, pawnId))
            s.Count(ContainedCount);
    }

    /// <summary>Records a PitGate whose post-Dreadmeld collapse completed and destroyed the gate.</summary>
    public static void PitGateCollapsed(FamilyState s, string gateId, bool confirmedCollapse)
    {
        if (!confirmedCollapse || string.IsNullOrWhiteSpace(gateId))
            return;
        s.Distinct(CollapsedGateIds, gateId);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.CountAndBreakdown(RitualCounts, PsychicRituals, PsychicRitualsByDef);
        c.PutCount(Escapes, EntityEscapes);
        c.PutCount(ContainedCount, EntitiesContained);
        c.PutDistinct(CollapsedGateIds, PitGatesCollapsed);

        var s = c.State;
        if (s.Peek(CompletedRitualIds)?.Overflow == true)
            c.MarkPartial(PsychicRituals, PsychicRitualsByDef);
        if ((s.Peek(RitualUnknown)?.N ?? 0) > 0)
            c.MarkPartial(PsychicRitualsByDef);
        if (s.Peek(ContainedBaselineSet)?.Overflow == true)
            c.MarkPartial(EntitiesContained);
        if (s.Peek(CollapsedGateIds)?.Overflow == true)
            c.MarkPartial(PitGatesCollapsed);
    }
}
