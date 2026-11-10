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
    private const string FilthKey = "filth";
    private const string FireKey = "fire";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    public static void FilthCleared(FamilyState state) => state.Count(FilthKey);
    public static void FireCleared(FamilyState state) => state.Count(FireKey);

    private static void Build(FamilyOutputContext context)
    {
        context.Put(FilthRemoved, context.Value(integer: context.State.Peek(FilthKey)?.N ?? 0));
        context.Put(FiresExtinguished, context.Value(integer: context.State.Peek(FireKey)?.N ?? 0));
    }
}
