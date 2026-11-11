namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · food (ledger W-016, W-019, N1-A-008 (N2-F-010 retired); evidence #food).
/// <para>
/// N1-A-008 integrates the cells of every <c>Zone_Growing</c> over time from a recount at the start of recording and the
/// zone's <c>AddCell</c>/<c>RemoveCell</c> (and map removal); its numerator is the main harvest of player-harvested crops standing
/// in a growing zone. Both sides are measured only while the cell count is known, so they cover the same time; a save-time
/// recount that disagrees with the tracked count makes the output partial.
/// </para>
/// </summary>
public static class FoodFamily
{
    public const string Name = "food";
    public const string SpoiledUnits = "food.spoiled.units";
    public const string Nutrition = "food.consumed.nutrition";
    public const string HarvestPerGrowingCell = "food.harvest.per_growing_cell";

    // Retain revision 2 for compatibility with existing food-family checkpoints; removed N2-F-010
    // data is ignored and never resurrected as a published metric.
    public static LiveFamily Family { get; } = new()
    {
        Name = Name, Revision = 2, Build = Build, Interrupted = (s, throughTick, _) => AccrueOpen(s, throughTick),
    };

    private const string Spoil = "spoiled";
    private const string Eaten = "eaten";
    private const string Grow = "grow";                  // active: "cells" → growing-zone cells now, "since" → tick they were last accrued
    private const string Cells = "cells";
    private const string Since = "since";
    private const string CellTicks = "grow_cell_ticks";  // Sum: growing-zone cells × ticks while the count was known
    private const string ZoneHarvest = "grow_harvest";   // N: main harvest units from growing-zone crops while the count was known
    private const string GrowDrift = "grow_drift";       // Overflow: a recount disagreed with the tracked count

    /// <summary>A player food stack was destroyed by rotting (CompRottable): its whole stack is lost.</summary>
    public static void Spoiled(FamilyState s, int units)
    {
        if (units > 0)
            s.Count(Spoil, units);
    }

    /// <summary>Thing.Ingested returned the nutrition actually taken.</summary>
    public static void Ate(FamilyState s, double nutrition, bool freeColonist)
    {
        if (!freeColonist || !(nutrition > 0) || !double.IsFinite(nutrition))
            return;
        var a = s.Acc(Eaten);
        a.Sum = (a.Sum ?? 0) + nutrition;
    }

    /// <summary>
    /// The growing-zone cells counted from the game (at the start of recording, at each load and at each save). The time since the
    /// last change is accrued with the tracked count first; a different count than tracked marks the output partial.
    /// </summary>
    public static void GrowingCellsBaseline(FamilyState s, int cells, int tick)
    {
        AccrueGrowing(s, tick);
        if (s.Tracked(Grow, Cells) is { } known && known != cells)
            s.Acc(GrowDrift).Overflow = true;
        s.Track(Grow, Cells, Math.Max(0, cells));
        s.Track(Grow, Since, tick);
    }

    /// <summary>A growing zone gained (+) or lost (−) cells: Zone.AddCell / RemoveCell, or a removed map's zones.</summary>
    public static void GrowingCellsChanged(FamilyState s, int delta, int tick)
    {
        if (s.Tracked(Grow, Cells) is not { } cells)
            return;   // not known since a gap: the next recount starts the measure again
        AccrueGrowing(s, tick);
        var next = cells + delta;
        if (next < 0)
        {
            s.Acc(GrowDrift).Overflow = true;
            next = 0;
        }
        s.Track(Grow, Cells, next);
    }

    /// <summary>Read only (the verification harness): the growing-zone cells tracked now; null while unknown.</summary>
    public static long? TrackedGrowingCells(FamilyState s) => s.Tracked(Grow, Cells);

    /// <summary>
    /// The main harvest of a player-harvested crop standing in a growing zone. The cell-day integral is brought up to
    /// <paramref name="tick"/> first, so an interruption later can never keep a harvest without the cell-days that preceded it.
    /// </summary>
    public static void ZoneHarvested(FamilyState s, int units, int tick)
    {
        if (units <= 0 || s.Tracked(Grow, Cells) is null)
            return;
        AccrueGrowing(s, tick);
        s.Count(ZoneHarvest, units);
    }

    private static void AccrueGrowing(FamilyState s, int tick)
    {
        if (s.Tracked(Grow, Cells) is not { } cells || s.Tracked(Grow, Since) is not { } since || tick < since)
            return;
        if (tick > since)
        {
            var a = s.Acc(CellTicks);
            a.Sum = (a.Sum ?? 0) + cells * (double)(tick - since);
        }
        s.Track(Grow, Since, tick);
    }

    /// <summary>Before an interruption drops the active state: what was measured through <paramref name="throughTick"/> is kept.</summary>
    internal static void AccrueOpen(FamilyState s, int throughTick)
    {
        AccrueGrowing(s, throughTick);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Spoil, SpoiledUnits);
        c.PutSum(Eaten, Nutrition);
        PutYield(c);
    }

    private static void PutYield(FamilyOutputContext c)
    {
        double cellTicks = c.State.Peek(CellTicks)?.Sum ?? 0;
        var known = c.State.Tracked(Grow, Cells);
        if (known is { } cells && c.State.Tracked(Grow, Since) is { } since && c.State.ThroughTick > since)
            cellTicks += cells * (double)(c.State.ThroughTick - since);
        var units = c.State.Peek(ZoneHarvest)?.N ?? 0;
        var cellDays = cellTicks / StatFormat.TicksPerDay;
        if (cellDays <= 0)
        {
            // No growing area was measured (none known, or no zone at all): a yield per cell-day is not defined. A harvest
            // without any cell-day would contradict the tracked count and is unknown rather than a number.
            c.Put(HarvestPerGrowingCell, c.Value(coverage: units == 0 ? MetricCoverage.NotApplicable : MetricCoverage.Unknown, numerator: units, denominator: 0));
            return;
        }
        var partial = c.State.Peek(GrowDrift)?.Overflow == true;
        c.Put(HarvestPerGrowingCell, c.Value(real: units / cellDays, numerator: units, denominator: cellDays, partial: partial));
    }
}
