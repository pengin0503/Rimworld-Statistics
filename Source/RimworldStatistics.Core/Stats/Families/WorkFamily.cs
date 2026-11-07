using System.Globalization;

namespace RimworldStatistics.Core;

/// <summary>One product stack of a finished bill (RecordsUtility.Notify_BillDone).</summary>
public sealed class CraftedThing
{
    public required string Def { get; init; }
    public required int Count { get; init; }

    /// <summary>QualityCategory name, or null for a product without quality.</summary>
    public string? Quality { get; init; }

    public double UnitMarketValue { get; init; }
    public bool Meal { get; init; }
    public bool Art { get; init; }
}

/// <summary>
/// Phase C · work (ledger W-001…W-015, W-017, W-018, W-021, W-023, N1-A-009, N2-F-001…N2-F-003, N2-F-005, N2-F-009, N2-F-011,
/// N2-F-012, N3-W-001…N3-W-005, N4-R-004; evidence #work). Units are stack counts. A sown plant enters a cohort; its first
/// successful harvest or its confirmed death decides it (a plant removed otherwise stays undecided and is not counted).
/// </summary>
public static class WorkFamily
{
    public const string Name = "work";

    public const string HarvestUnits = "work.harvest.units";
    public const string MiningUnits = "work.mining.units";
    public const string CraftedUnits = "work.crafting.units";
    public const string Constructions = "work.construction.count";
    public const string BestQuality = "work.crafting.best_quality";
    public const string QualityBreakdown = "work.crafting.quality";
    public const string ConstructionMaterials = "work.construction.material_units";
    public const string Deconstructions = "work.deconstruction.count";
    public const string ConstructionFailures = "work.construction.failures";
    public const string Legendary = "work.crafting.quality.legendary";
    public const string Masterwork = "work.crafting.quality.masterwork";
    public const string BestPrice = "work.crafting.best_price";
    public const string Sculptures = "work.crafting.sculptures";
    public const string Sown_ = "work.crops.sown";
    public const string HarvestFailures = "work.harvest.failures";
    public const string MealsByDef = "work.cooking.by_def";
    public const string UniqueCraftedDefs = "work.crafting.unique_defs";
    public const string ColdLoss = "work.crops.cold_loss";
    public const string PodsLaunched_ = "transport.pods.launched";
    public const string DailyPeak = "work.crafting.daily_peak";
    public const string SownDefs = "work.crops.sown_defs";
    public const string HarvestByCrop = "work.harvest.by_crop";
    public const string TopCrop = "work.harvest.top_crop";
    public const string OtherLoss = "work.crops.other_loss";
    public const string LossRate = "work.crops.loss_rate";
    public const string HarvestYearPeak = "work.harvest.year_peak";
    public const string BestCrafter = "work.crafting.best_worker";
    public const string BestHarvester = "work.harvest.best_worker";
    public const string BestBuilder = "work.construction.best_worker";
    public const string BestCook = "work.cooking.best_worker";
    public const string BestMiner = "work.mining.best_worker";
    public const string Repairs = "work.repair.completed";

    public const string NoQuality = "none";

    /// <summary>RimWorld.QualityCategory, worst to best.</summary>
    public static readonly IReadOnlyList<string> Qualities = new[] { "Awful", "Poor", "Normal", "Good", "Excellent", "Masterwork", "Legendary" };

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Harvest = "harvest";            // Sum; By crop
    private const string HarvestByWorker = "harvest_by_worker";
    private const string HarvestByYear = "harvest_by_year";
    private const string Fails = "harvest_failed";
    private const string Mine = "mining";
    private const string MineByWorker = "mining_by_worker";
    private const string Craft = "crafting";             // N; By quality
    private const string CraftDefs = "crafted_defs";
    private const string CraftByWorker = "crafting_by_worker";
    private const string CraftByDay = "crafting_by_day";
    private const string Price = "unit_price";
    private const string Art = "art";
    private const string Meals = "meals";
    private const string CookByWorker = "cooking_by_worker";
    private const string Build_ = "construction";        // N; By material
    private const string BuildByWorker = "construction_by_worker";
    private const string Deconstruct = "deconstructed";
    private const string BuildFail = "construction_failed";
    private const string Sow = "sown";                   // N; By crop (distinct crops)
    private const string Cold = "cold_loss";
    private const string Other = "other_loss";
    private const string CohortLoss = "cohort_loss";
    private const string CohortHarvest = "cohort_harvest";
    private const string Pods = "pods";
    private const string Repair = "repairs";
    private const string Cohort = "cohort";              // active: plant id → sow tick

    private static string Period(int tick, int ticks) => (tick / ticks).ToString(CultureInfo.InvariantCulture);

    /// <summary>A player worker's main harvest (QuestManager.Notify_PlantHarvested), of the plant the worker's job targets.</summary>
    public static void Harvested(FamilyState s, string workerId, string? plantId, string? cropDef, int units, int tick)
    {
        if (units <= 0)
            return;
        var a = s.Acc(Harvest);
        a.Sum = (a.Sum ?? 0) + units;
        s.CountBy(Harvest, cropDef ?? MetricKeys.UnknownBucket, units);
        s.CountBy(HarvestByWorker, workerId, units);
        s.CountBy(HarvestByYear, Period(tick, StatFormat.TicksPerDay * StatFormat.DaysPerYear), units);
        if (plantId != null && s.Untrack(Cohort, plantId) is not null)
            s.Count(CohortHarvest);
    }

    public static void HarvestFailed(FamilyState s) => s.Count(Fails);

    public static void Mined(FamilyState s, string workerId, string def, int units)
    {
        if (units <= 0)
            return;
        var a = s.Acc(Mine);
        a.Sum = (a.Sum ?? 0) + units;
        s.CountBy(MineByWorker, workerId, units);
    }

    public static void Crafted(FamilyState s, string workerId, IEnumerable<CraftedThing> products, int tick)
    {
        foreach (var p in products)
        {
            if (p.Count <= 0)
                continue;
            s.Count(Craft, p.Count);
            s.CountBy(Craft, p.Quality ?? NoQuality, p.Count);
            s.Distinct(CraftDefs, p.Def);
            s.CountBy(CraftByWorker, workerId, p.Count);
            s.CountBy(CraftByDay, Period(tick, StatFormat.TicksPerDay), p.Count);
            s.Observe(Price, p.UnitMarketValue);
            if (p.Art)
                s.Count(Art, p.Count);
            if (p.Meal)
            {
                s.Count(Meals, p.Count);
                s.CountBy(Meals, p.Def, p.Count);
                s.CountBy(CookByWorker, workerId, p.Count);
            }
        }
    }

    /// <summary>A frame of the player was completed into a building, with the materials it held.</summary>
    public static void Constructed(FamilyState s, string? workerId, IReadOnlyDictionary<string, int> materials)
    {
        s.Count(Build_);
        foreach (var (def, n) in materials)
            if (n > 0)
                s.CountBy(Build_, def, n);
        if (workerId != null)
            s.CountBy(BuildByWorker, workerId);
    }

    public static void ConstructionFailed(FamilyState s) => s.Count(BuildFail);
    public static void Deconstructed(FamilyState s) => s.Count(Deconstruct);
    public static void Repaired(FamilyState s) => s.Count(Repair);

    public static void PodsLaunched(FamilyState s, int pods)
    {
        if (pods > 0)
            s.Count(Pods, pods);
    }

    public static void Sown(FamilyState s, string plantId, string cropDef)
    {
        s.Count(Sow);
        s.CountBy(Sow, cropDef);
        s.Track(Cohort, plantId, 0);
    }

    /// <summary>A sown plant left the cohort without a decision (cut by hand, a failed harvest, destroyed otherwise).</summary>
    public static void PlantRemoved(FamilyState s, string plantId) => s.Untrack(Cohort, plantId);

    /// <summary>A player crop died of a confirmed cause (cold, or another cause such as poison or pollution).</summary>
    public static void CropDied(FamilyState s, string plantId, bool cold)
    {
        s.Count(cold ? Cold : Other);
        if (s.Untrack(Cohort, plantId) is not null)
            s.Count(CohortLoss);
    }

    private static void Build(FamilyOutputContext c)
    {
        var s = c.State;
        var harvest = s.Peek(Harvest);
        c.Put(HarvestUnits, c.Value(integer: (long)(harvest?.Sum ?? 0)));
        c.PutBreakdown(HarvestByCrop, c.Counts(harvest), c.Value(integer: (long)(harvest?.Sum ?? 0), partial: harvest?.Overflow == true));
        var crops = harvest?.By?.Where(kv => kv.Key != MetricKeys.UnknownBucket).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        PutTopOf(c, crops, harvest?.Overflow == true, TopCrop);
        c.PutTop(HarvestByWorker, BestHarvester);
        c.PutCompletePeriodPeak(HarvestByYear, HarvestYearPeak, StatFormat.TicksPerDay * StatFormat.DaysPerYear);
        c.PutCount(Fails, HarvestFailures);
        c.Put(MiningUnits, c.Value(integer: (long)(s.Peek(Mine)?.Sum ?? 0)));
        c.PutTop(MineByWorker, BestMiner);

        var craft = s.Peek(Craft);
        c.Put(CraftedUnits, c.Value(integer: craft?.N ?? 0));
        c.PutBreakdown(QualityBreakdown, c.Counts(craft), c.Value(integer: craft?.N ?? 0, partial: craft?.Overflow == true));
        long Q(string q) => craft?.By != null && craft.By.TryGetValue(q, out var v) ? (long)v : 0;
        c.Put(Legendary, c.Value(integer: Q("Legendary")));
        c.Put(Masterwork, c.Value(integer: Q("Masterwork")));
        var best = Qualities.Select((q, i) => (q, i)).Where(x => Q(x.q) > 0).Select(x => (int?)x.i).Max();
        if (best is { } bi)
            c.PutBreakdown(BestQuality, new Dictionary<string, MetricValue> { [Qualities[bi]] = c.Value(integer: bi) }, c.Value(integer: bi));
        else
            c.Put(BestQuality, c.Value(coverage: MetricCoverage.NotApplicable));
        c.PutExtreme(Price, BestPrice, max: true);
        c.PutCount(Art, Sculptures);
        c.PutDistinct(CraftDefs, UniqueCraftedDefs);
        c.PutTop(CraftByWorker, BestCrafter);
        c.PutCompletePeriodPeak(CraftByDay, DailyPeak, StatFormat.TicksPerDay);
        var meals = s.Peek(Meals);
        c.PutBreakdown(MealsByDef, c.Counts(meals), c.Value(integer: meals?.N ?? 0, partial: meals?.Overflow == true));
        c.PutTop(CookByWorker, BestCook);

        var build = s.Peek(Build_);
        c.Put(Constructions, c.Value(integer: build?.N ?? 0));
        c.PutBreakdown(ConstructionMaterials, c.Counts(build), null);
        c.PutTop(BuildByWorker, BestBuilder);
        c.PutCount(BuildFail, ConstructionFailures);
        c.PutCount(Deconstruct, Deconstructions);
        c.PutCount(Repair, Repairs);
        c.PutCount(Pods, PodsLaunched_);

        var sow = s.Peek(Sow);
        c.Put(Sown_, c.Value(integer: sow?.N ?? 0));
        c.Put(SownDefs, c.Value(integer: sow?.By?.Count ?? 0, partial: sow?.Overflow == true));
        c.PutCount(Cold, ColdLoss);
        c.PutCount(Other, OtherLoss);
        var loss = s.Peek(CohortLoss)?.N ?? 0;
        var decided = loss + (s.Peek(CohortHarvest)?.N ?? 0);
        c.Put(LossRate, decided == 0
            ? c.Value(numerator: 0, denominator: 0)
            : c.Value(real: loss / (double)decided, numerator: loss, denominator: decided, partial: s.TrackerOverflowed(Cohort)));
    }

    private static void PutTopOf(FamilyOutputContext c, Dictionary<string, double>? by, bool overflow, string key)
    {
        if (by is not { Count: > 0 })
        {
            c.Put(key, c.Value(coverage: MetricCoverage.NotApplicable));
            return;
        }
        var best = by.Values.Max();
        var holders = by.Where(kv => kv.Value == best).Select(kv => kv.Key).Order(StringComparer.Ordinal)
            .ToDictionary(k => k, _ => c.Value(integer: (long)Math.Round(best), partial: overflow), StringComparer.Ordinal);
        c.PutBreakdown(key, holders, c.Value(integer: (long)Math.Round(best), partial: overflow));
    }
}
