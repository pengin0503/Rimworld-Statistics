namespace RimworldStatistics.Core;

/// <summary>
/// Pure attribution rule for colony labor outcomes (harvesting, sowing, crafting bills,
/// mining, hauling, cleaning, fire-fighting, fishing, etc.).
/// Free colonists, colony slaves, and colony mechanoids/animals carry <c>isPlayerFaction == true</c>
/// while <c>isPrisoner == false</c>. Imprisoned pawns (<c>isPrisoner == true</c>) belong to
/// the colony workforce if and only if their jailer (<c>HostFaction</c>) is the player faction,
/// which both includes colony-custody prisoner labor (e.g. Prison Labor) and excludes player
/// pawns currently held captive by a hostile faction.
/// </summary>
public static class ColonyWorkAttribution
{
    /// <summary>
    /// The accumulator a family's totals carry when they began under this rule (#392). The rule replaced "player-faction pawns"
    /// in 2.0 (#385) without a new revision, so totals continued from 1.9 lack it: they counted no prisoner's work before.
    /// </summary>
    public const string RuleMarker = "colony_worker_rule";

    /// <summary>Set in totals without <see cref="RuleMarker"/> once a prisoner's work was counted in them: they mix both rules.</summary>
    public const string MixedMarker = "colony_worker_rule_mixed";

    public static bool IsColonyWorker(bool isPlayerFaction, bool isPrisoner, bool isHostPlayerFaction)
        => isPrisoner ? isHostPlayerFaction : isPlayerFaction;

    /// <summary>
    /// Whether a colony worker's work was not counted before this rule: one outside the player's faction, that is a prisoner
    /// in the colony's custody. (A player pawn held by another faction, no longer counted, does no colony work.)
    /// </summary>
    public static bool CountsOnlyUnderThisRule(bool isPlayerFaction) => !isPlayerFaction;

    /// <summary>The <see cref="LiveFamily.Started"/> hook of a family that counts colony workers.</summary>
    public static void Started(FamilyState state) => state.Acc(RuleMarker).N = 1;

    /// <summary>Notes, in the totals the work was counted in, that it counts only under this rule (see <see cref="CountsOnlyUnderThisRule"/>).</summary>
    public static void CountedPrisonerWork(FamilyState state)
    {
        if (state.Peek(RuleMarker) is null && state.Peek(MixedMarker) is null)
            state.Acc(MixedMarker).N = 1;
    }

    /// <summary>Whether totals continued from before this rule have since counted a prisoner's work.</summary>
    public static bool MixesRules(FamilyState state) => state.Peek(MixedMarker) is not null;

    /// <summary>
    /// Weakens <paramref name="keys"/> to a lower bound when the totals mix both rules: under this rule they lack the prisoners'
    /// work from before it, which was never recorded, so the value is at least what is shown (#392).
    /// </summary>
    public static void WeakenMixed(FamilyOutputContext context, params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (MixesRules(context.State))
            context.MarkPartial(keys);
    }
}
