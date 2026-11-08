namespace RimworldStatistics.Core;

/// <summary>
/// Biotech D2 confirmed events. A game adapter must supply proof of completed production, repairs,
/// disposal and pollution cleanup; an attempted job or stock difference is never accepted as evidence.
/// </summary>
public static class BiotechFamily
{
    public const string Name = "biotech";
    public const string MechsCreated = "biotech.mechs.created";
    public const string BirthMothers = "biotech.birth.mothers";
    public const string ChildStages = "biotech.child_stages";
    public const string MechRepairs = "biotech.mechs.repairs";
    public const string WasteCreated = "biotech.waste.created";
    public const string WasteDisposed = "biotech.waste.disposed";
    public const string PollutionCleaned = "biotech.pollution.cleaned";

    private const string CreatedMechs = "created_mechs";
    private const string ReadyGestations = "ready_gestations";
    private const string Mothers = "mothers";
    private const string ReachedStages = "reached_stages";
    private const string Stages = "stage_counts";
    private const string CompletedRepairs = "completed_repairs";
    private const string CreatedWaste = "created_waste";
    private const string DisposedWaste = "disposed_waste";
    private const string CleanEvents = "clean_events";
    private const string Cleaned = "cleaned";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name, Revision = 1, RequiredDlc = AcquisitionUnits.Biotech, Build = Build,
    };

    /// <summary>Track a confirmed finished manufacturing cycle until the pawn really emerges.</summary>
    public static void GestationReady(FamilyState s, string mechId, bool playerGestator, bool resurrection)
    {
        if (playerGestator && !resurrection && !string.IsNullOrWhiteSpace(mechId))
            s.Track(ReadyGestations, mechId, 1);
    }

    /// <summary>
    /// A reloaded or moved tank can eject later. Only a living player-owned mech
    /// already tracked from a confirmed gestation result is a completed manufacture.
    /// </summary>
    public static void GestationEjected(FamilyState s, string mechId, bool livingPlayerMech)
    {
        if (!livingPlayerMech || string.IsNullOrWhiteSpace(mechId))
            return;
        if (s.Untrack(ReadyGestations, mechId) is not null)
            s.Distinct(CreatedMechs, mechId);
    }

    /// <summary>Only a player-made mech after the gestator actually produced a living instance.</summary>
    public static void MechProduced(FamilyState s, string mechThingId, bool playerMade, bool gestationFinished)
    {
        if (playerMade && gestationFinished && !string.IsNullOrWhiteSpace(mechThingId))
            s.Distinct(CreatedMechs, mechThingId);
    }

    /// <summary>One confirmed living baby, uniquely attributable to a free-colonist mother.</summary>
    public static void BirthConfirmed(FamilyState s, string motherId, bool motherIsFreeColonist, bool livingBaby)
    {
        if (motherIsFreeColonist && livingBaby && !string.IsNullOrWhiteSpace(motherId))
            s.Distinct(Mothers, motherId);
    }

    /// <summary>Stage key is the stable growth milestone, one event per child and stage across reloads.</summary>
    public static void ChildReachedStage(FamilyState s, string childId, string stage, bool playerChild,
        bool confirmedNewStage)
    {
        if (!playerChild || !confirmedNewStage || string.IsNullOrWhiteSpace(childId) || string.IsNullOrWhiteSpace(stage))
            return;
        if (s.Distinct(ReachedStages, childId + "/" + stage))
        {
            s.Count(Stages);
            s.CountBy(Stages, stage);
        }
    }

    /// <summary>One completed repair operation ending with no remaining injury, not every treatment tick.</summary>
    public static void MechFullyRepaired(FamilyState s, string repairOperationId, bool colonyMech, bool clearedLastInjury)
    {
        if (colonyMech && clearedLastInjury && !string.IsNullOrWhiteSpace(repairOperationId))
            s.Distinct(CompletedRepairs, repairOperationId);
    }

    /// <summary>Only positive stack count newly spawned by a confirmed production path.</summary>
    public static void WastePackProduced(FamilyState s, int amount, bool playerProductionComplete)
    {
        if (amount > 0 && playerProductionComplete)
            s.Count(CreatedWaste, amount);
    }

    /// <summary>Only positive stack count removed by confirmed player neutralization, never export or decomposition.</summary>
    public static void WastePackNeutralized(FamilyState s, int amount, bool playerNeutralizationComplete)
    {
        if (amount > 0 && playerNeutralizationComplete)
            s.Count(DisposedWaste, amount);
    }

    /// <summary>A confirmed player cleanup job made a previously polluted map cell clean.</summary>
    public static void PollutionCellCleaned(FamilyState s, string operationId, bool playerCleanup,
        bool pollutedBefore, bool pollutedAfter, bool mapCell)
    {
        if (!playerCleanup || !mapCell || !pollutedBefore || pollutedAfter || string.IsNullOrWhiteSpace(operationId))
            return;
        if (s.Distinct(CleanEvents, operationId))
            s.Count(Cleaned);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutDistinct(CreatedMechs, MechsCreated);
        c.PutDistinct(Mothers, BirthMothers);
        // ChildStages is both a scalar and a stage-keyed breakdown (ledger L-015).
        // CountAndBreakdown(..., null) silently discarded the promised buckets.
        var stages = c.State.Peek(Stages);
        c.PutBreakdown(ChildStages, c.Counts(stages),
            c.Value(integer: stages?.N ?? 0, partial: stages?.Overflow == true));
        c.PutDistinct(CompletedRepairs, MechRepairs);
        c.PutCount(CreatedWaste, WasteCreated);
        c.PutCount(DisposedWaste, WasteDisposed);
        c.PutCount(Cleaned, PollutionCleaned);
        var s = c.State;
        if (s.Peek(ReachedStages)?.Overflow == true || s.Peek(Stages)?.Overflow == true)
            c.MarkPartial(ChildStages);
        if (s.Peek(CreatedMechs)?.Overflow == true)
            c.MarkPartial(MechsCreated);
        if (s.TrackerOverflowed(ReadyGestations))
            c.MarkPartial(MechsCreated);
        if (s.Peek(Mothers)?.Overflow == true)
            c.MarkPartial(BirthMothers);
        if (s.Peek(CompletedRepairs)?.Overflow == true)
            c.MarkPartial(MechRepairs);
        if (s.Peek(CleanEvents)?.Overflow == true)
            c.MarkPartial(PollutionCleaned);
    }
}
