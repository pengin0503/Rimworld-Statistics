namespace RimworldStatistics.Core;

/// <summary>
/// Independent P2 counters sourced from 1.7's successful vanilla Pawn_RecordsTracker increments.
/// FilthRemoved counts completely destroyed filth objects, not partial thinning or job counts.
/// Isolated from WorkFamily's revision and disabled unless the "chore" acquisition gate opens.
/// </summary>
public static class ChoreFamily
{
    public const string Name = "chore";
    public const string FilthRemoved = "work.cleaning.filth_removed";
    public const string CleaningJobsCompleted = "work.cleaning.jobs_completed";
    public const string FiresExtinguished = "work.fire.extinguished";
    public const string MealIngestions = "work.food.meal_ingestions";
    public const string HaulPlacements = "work.hauling.cell_deposits";
    public const string HauledUnits = "work.hauling.cell_deposited_units";
    public const string TreeWoodPlaced = "work.trees.wood_placed";
    public const string SlaughteredButchery = "work.butcher.slaughtered_carcasses_processed";
    private const string FilthKey = "filth";
    private const string CleaningJobsKey = "clean_jobs";
    private const string CleaningPendingKey = "clean_pending";
    private const string FireKey = "fire";
    private const string MealKey = "meal";
    private const string HaulPlacementsKey = "hauls";
    private const string HaulUnitsKey = "haul_units";
    private const string WoodKey = "wood_placed";
    private const string SlaughteredCorpseKey = "slaughter_corpses";
    private const string DestroyedCorpseKey = "destroyed_slaughter_corpses";
    private const string ButcheredKey = "butcher_after_slaughter";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    public static void FilthCleared(FamilyState state) => state.Count(FilthKey);

    /// <summary>One actual destroyed filth object in an active cleaning job.
    /// Job.loadID is persisted by the game, and family.Active by the checkpoint.</summary>
    public static void CleanedWithinJob(FamilyState state, string jobId)
    {
        if (!string.IsNullOrEmpty(jobId))
            state.Track(CleaningPendingKey, jobId, 1);
    }

    /// <summary>Only a successfully ended job that cleaned at least one filth object.
    /// Clearing a pending job after cancellation prevents an interrupted partial job
    /// from being reported as a completed job.</summary>
    public static void CleaningJobEnded(FamilyState state, string jobId, bool succeeded)
    {
        if (string.IsNullOrEmpty(jobId))
            return;
        var cleaned = state.Untrack(CleaningPendingKey, jobId);
        if (succeeded && cleaned > 0)
            state.Count(CleaningJobsKey);
    }
    public static void FireCleared(FamilyState state) => state.Count(FireKey);
    public static void MealIngested(FamilyState state) => state.Count(MealKey);

    /// <summary>Only wood units reported by GenPlace's successful placedAction on a player's cut tree.</summary>
    public static void WoodObtained(FamilyState state, int placedWoodUnits)
    {
        if (placedWoodUnits > 0) state.Count(WoodKey, placedWoodUnits);
    }

    /// <summary>Only corpses created by successful, player-operated slaughter are eligible.
    /// Retire corpses destroyed on earlier ticks before allocating a new tracking key.</summary>
    public static void MarkSlaughteredCorpse(FamilyState state, string corpseId, int tick = 0)
    {
        if (string.IsNullOrEmpty(corpseId)) return;
        if (tick > 0)
            PruneDestroyedCorpses(state, tick);
        state.Track(SlaughteredCorpseKey, corpseId, 1);
    }

    /// <summary>The corpse was destroyed, but may still complete its butcher bill
    /// later in the same tick. Defer removal so ingredient consumption can be counted.</summary>
    public static void CorpseDestroyed(FamilyState state, string corpseId, int tick)
    {
        if (!string.IsNullOrEmpty(corpseId) && state.Tracked(SlaughteredCorpseKey, corpseId) is not null)
            state.Track(DestroyedCorpseKey, corpseId, tick);
    }

    /// <summary>Release dead corpse markers after their possible bill completion tick.
    /// At most KeyCap tracked keys are inspected, with no world or map scan.</summary>
    public static void PruneDestroyedCorpses(FamilyState state, int tick)
    {
        if (!state.Active.TryGetValue(DestroyedCorpseKey, out var destroyed))
            return;
        foreach (var id in destroyed.Where(pair => pair.Value < tick).Select(pair => pair.Key).ToList())
        {
            state.Untrack(SlaughteredCorpseKey, id);
            state.Untrack(DestroyedCorpseKey, id);
        }
    }

    /// <summary>One successfully consumed tracked corpse, not a generic butcher bill or an animal death.
    /// A non-butchery consumer clears the marker but must never count as a butcher job.</summary>
    public static void CorpseConsumed(FamilyState state, string corpseId, bool butcherFleshRecipe)
    {
        if (string.IsNullOrEmpty(corpseId)) return;
        var formerlySlaughtered = state.Untrack(SlaughteredCorpseKey, corpseId);
        state.Untrack(DestroyedCorpseKey, corpseId);
        if (butcherFleshRecipe && formerlySlaughtered > 0)
            state.Count(ButcheredKey);
    }

    public static void HaulDeposited(FamilyState state, int depositedUnits)
    {
        if (depositedUnits <= 0) return;
        state.Count(HaulPlacementsKey);
        state.Count(HaulUnitsKey, depositedUnits);
    }

    private static void Build(FamilyOutputContext context)
    {
        context.Put(FilthRemoved, context.Value(integer: context.State.Peek(FilthKey)?.N ?? 0));
        context.Put(CleaningJobsCompleted, context.Value(
            integer: context.State.Peek(CleaningJobsKey)?.N ?? 0,
            partial: context.State.TrackerOverflowed(CleaningPendingKey)));
        context.Put(FiresExtinguished, context.Value(integer: context.State.Peek(FireKey)?.N ?? 0));
        context.Put(MealIngestions, context.Value(integer: context.State.Peek(MealKey)?.N ?? 0));
        // Cell-storage drops only: container hauling and nonstandard MOD paths are not covered.
        context.Put(HaulPlacements, context.Value(integer: context.State.Peek(HaulPlacementsKey)?.N ?? 0, partial: true));
        context.Put(HauledUnits, context.Value(integer: context.State.Peek(HaulUnitsKey)?.N ?? 0, partial: true));
        // Primary harvest only; comp-based bonus yield and nonstandard modded cutting remain outside coverage.
        context.Put(TreeWoodPlaced, context.Value(integer: context.State.Peek(WoodKey)?.N ?? 0, partial: true));
        // Only the vanilla flesh-butcher recipe has a source-proven completion
        // boundary. Alternate MOD butchering and unobserved corpse lifetimes remain partial.
        context.Put(SlaughteredButchery, context.Value(integer: context.State.Peek(ButcheredKey)?.N ?? 0, partial: true));
    }
}
