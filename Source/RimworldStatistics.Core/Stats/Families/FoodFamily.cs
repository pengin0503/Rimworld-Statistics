namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · food (ledger W-016, W-019, N2-F-010, N1-A-008; evidence #food).
/// <para>
/// N2-F-010 uses the game's own food stock: the vanilla <c>ResourceCounter</c> recounts the stored resources of every map every
/// 204 ticks (and on hauling), and its human-edible nutrition is what the resource readout and the low-food alert show. The
/// adapter reads each player home map's recount and adds the human-edible food player pawns are carrying there, so moving the
/// last stack between stockpiles is not a shortage. A span runs from the first observation with food to the last one before an
/// observation without food; only observed ticks are used, and a silence longer than <see cref="StockObservationGap"/> (no
/// home map, a missed hook) ends the span instead of bridging it.
/// </para>
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
    public const string NoShortageLongest = "food.no_shortage.longest";
    public const string HarvestPerGrowingCell = "food.harvest.per_growing_cell";

    /// <summary>Longest silence between two stock observations (ticks) that still counts as watched: two recount periods and a margin.</summary>
    public const int StockObservationGap = 420;

    // Revision 2 added N2-F-010 and N1-A-008: a revision-1 checkpoint did not measure them and is never continued.
    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 2, Build = Build };

    private const string Spoil = "spoiled";
    private const string Eaten = "eaten";
    private const string StockSeen = "stock_obs";        // N: stock observations
    private const string Span = "stock_span";            // samples: closed spans with food (ticks between observations)
    private const string Stock = "stock";                // active: "start", "last" (observations with food), "seen" (any)
    private const string SpanStart = "start";
    private const string SpanLast = "last";
    private const string SeenTick = "seen";
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

    /// <summary>The colony's food stock after a recount of a player home map: some human-edible food stored or carried, or none.</summary>
    public static void StockObserved(FamilyState s, bool hasFood, int tick)
    {
        if (s.Tracked(Stock, SeenTick) is { } seen && (tick < seen || tick - seen > StockObservationGap))
            CloseSpan(s);   // not watched in between: the span ends at its last observation, the next one starts afresh
        s.Track(Stock, SeenTick, tick);
        s.Count(StockSeen);
        if (!hasFood)
        {
            CloseSpan(s);
            return;
        }
        if (s.Tracked(Stock, SpanStart) is null)
            s.Track(Stock, SpanStart, tick);
        s.Track(Stock, SpanLast, tick);
    }

    private static void CloseSpan(FamilyState s)
    {
        var start = s.Untrack(Stock, SpanStart);
        var last = s.Untrack(Stock, SpanLast);
        if (start is { } a && last is { } b && b >= a)
            s.Observe(Span, b - a);
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

    /// <summary>The main harvest of a player-harvested crop standing in a growing zone.</summary>
    public static void ZoneHarvested(FamilyState s, int units)
    {
        if (units > 0 && s.Tracked(Grow, Cells) is not null)
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
        CloseSpan(s);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Spoil, SpoiledUnits);
        c.PutSum(Eaten, Nutrition);
        PutLongestSpan(c);
        PutYield(c);
    }

    private static void PutLongestSpan(FamilyOutputContext c)
    {
        if ((c.State.Peek(StockSeen)?.N ?? 0) == 0)
        {
            c.Put(NoShortageLongest, c.Value(coverage: MetricCoverage.NotApplicable));
            return;
        }
        double best = c.State.Peek(Span)?.Max ?? 0;
        if (c.State.Tracked(Stock, SpanStart) is { } start && c.State.Tracked(Stock, SpanLast) is { } last && last >= start)
            best = Math.Max(best, last - start);   // the span still running at this save, as far as it was observed
        c.Put(NoShortageLongest, c.Value(real: best / StatFormat.TicksPerDay));
    }

    private static void PutYield(FamilyOutputContext c)
    {
        double cellTicks = c.State.Peek(CellTicks)?.Sum ?? 0;
        var known = c.State.Tracked(Grow, Cells);
        if (known is { } cells && c.State.Tracked(Grow, Since) is { } since && c.State.ThroughTick > since)
            cellTicks += cells * (double)(c.State.ThroughTick - since);
        var units = c.State.Peek(ZoneHarvest)?.N ?? 0;
        var cellDays = cellTicks / StatFormat.TicksPerDay;
        var partial = c.State.Peek(GrowDrift)?.Overflow == true;
        if (cellTicks <= 0 && known is null && c.State.Peek(CellTicks) is null)
            c.Put(HarvestPerGrowingCell, c.Value(coverage: MetricCoverage.NotApplicable, numerator: 0, denominator: 0));   // never measured
        else
            c.Put(HarvestPerGrowingCell, c.Value(real: cellDays > 0 ? units / cellDays : null, numerator: units, denominator: cellDays, partial: partial));
    }
}
