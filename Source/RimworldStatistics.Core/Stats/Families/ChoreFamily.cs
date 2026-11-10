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
    public const string FiresExtinguished = "work.fire.extinguished";
    public const string MealIngestions = "work.food.meal_ingestions";
    public const string HaulPlacements = "work.hauling.cell_deposits";
    public const string HauledUnits = "work.hauling.cell_deposited_units";
    private const string FilthKey = "filth";
    private const string FireKey = "fire";
    private const string MealKey = "meal";
    private const string HaulPlacementsKey = "hauls";
    private const string HaulUnitsKey = "haul_units";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    public static void FilthCleared(FamilyState state) => state.Count(FilthKey);
    public static void FireCleared(FamilyState state) => state.Count(FireKey);
    public static void MealIngested(FamilyState state) => state.Count(MealKey);

    public static void HaulDeposited(FamilyState state, int depositedUnits)
    {
        if (depositedUnits <= 0) return;
        state.Count(HaulPlacementsKey);
        state.Count(HaulUnitsKey, depositedUnits);
    }

    private static void Build(FamilyOutputContext context)
    {
        context.Put(FilthRemoved, context.Value(integer: context.State.Peek(FilthKey)?.N ?? 0));
        context.Put(FiresExtinguished, context.Value(integer: context.State.Peek(FireKey)?.N ?? 0));
        context.Put(MealIngestions, context.Value(integer: context.State.Peek(MealKey)?.N ?? 0));
        // Cell-storage drops only: container hauling and nonstandard MOD paths are not covered.
        context.Put(HaulPlacements, context.Value(integer: context.State.Peek(HaulPlacementsKey)?.N ?? 0, partial: true));
        context.Put(HauledUnits, context.Value(integer: context.State.Peek(HaulUnitsKey)?.N ?? 0, partial: true));
    }
}
