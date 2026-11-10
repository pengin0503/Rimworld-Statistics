using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Work (evidence #work). Harvests: QuestManager.Notify_PlantHarvested passes the main product made in the harvest toil of the
/// worker's current job, whose target A is the plant (JobDriver_PlantWork, 1.7.4985), so the crop is the job's target.
/// A harvest of a harvestable crop that reached PlantCollected without that notification in the same tick failed.
/// </summary>
internal static class HarvestProbe
{
    public static string? LastPlant;
    public static int LastTick = -1;

    public static Plant? JobPlant(Pawn worker) => worker.CurJob?.targetA.Thing as Plant;

    /// <summary>
    /// The crop of a player's main harvest (QuestManager.Notify_PlantHarvested): the spawned, sown crop the worker's job targets.
    /// Shared by the work (W-001) and food (N1-A-008) adapters so their harvest numerators never drift apart.
    /// </summary>
    public static Plant? PlayerCrop(Pawn? worker, Thing? harvested)
    {
        if (worker == null || harvested == null || !GameFacts.IsPlayer(worker))
            return null;
        var plant = JobPlant(worker);
        return plant != null && plant.Spawned && plant.IsCrop ? plant : null;
    }
}

/// <summary>Only the exact wood Thing reported by the harvest toil will be matched to
/// a subsequent placement callback. The game's Notify_PlantHarvested runs before
/// GenPlace.TryPlaceThing, so a notification by itself is never added as gained wood.</summary>
internal static class TreeWoodPlacementProbe
{
    private static Thing? expected;
    private static Map? map;
    private static Game? game;
    private static int tick = -1;

    public static void Expect(Pawn? worker, Thing? harvest)
    {
        if (LiveMetrics.Accumulator?.Family(ChoreFamily.Name) is null ||
            worker == null || harvest == null || !GameFacts.IsPlayer(worker) ||
            worker.jobs?.curDriver is not JobDriver_PlantCut ||
            HarvestProbe.JobPlant(worker) is not { Spawned: true } tree ||
            tree.def?.plant?.IsTree != true || tree.def.plant.harvestedThingDef != harvest.def ||
            harvest.stackCount <= 0)
            return;
        expected = harvest;
        map = tree.Map;
        game = Current.Game;
        tick = GameFacts.Tick;
    }

    public static bool Take(Thing? candidate, Map? targetMap)
    {
        if (tick != GameFacts.Tick || !ReferenceEquals(game, Current.Game))
        {
            expected = null;
            map = null;
            game = null;
            return false;
        }
        if (candidate == null || !ReferenceEquals(expected, candidate) || !ReferenceEquals(map, targetMap))
            return false;
        expected = null;
        map = null;
        game = null;
        return true;
    }
}

[MetricAdapter(WorkFamily.Name, ChoreFamily.Name)]
[HarmonyPatch(typeof(QuestManager), nameof(QuestManager.Notify_PlantHarvested))]
public static class PlantHarvestedMetricPatch
{
    public static void Postfix(Pawn worker, Thing harvested)
    {
        TreeWoodPlacementProbe.Expect(worker, harvested);
        if (HarvestProbe.PlayerCrop(worker, harvested) is not { } plant)
            return;
        var plantId = GameFacts.Id(plant);
        var tick = GameFacts.Tick;
        HarvestProbe.LastPlant = plantId;
        HarvestProbe.LastTick = tick;
        var crop = plant.def.defName;
        var worker_ = GameFacts.Id(worker);
        var units = harvested.stackCount;
        GameFacts.Record(WorkFamily.Name, s => WorkFamily.Harvested(s, worker_, plantId, crop, units, tick));
    }
}

[MetricAdapter(WorkFamily.Name)]
[HarmonyPatch(typeof(Plant), nameof(Plant.PlantCollected))]
public static class PlantCollectedMetricPatch
{
    public static void Prefix(Plant __instance, Pawn by)
    {
        if (__instance == null || !__instance.Spawned || LiveMetrics.Family(WorkFamily.Name) is null)
            return;
        var id = GameFacts.Id(__instance);
        var harvestJob = by?.CurJobDef == JobDefOf.Harvest || by?.CurJobDef == JobDefOf.HarvestDesignated;
        var failed = harvestJob && by!.RaceProps.Humanlike && GameFacts.IsPlayer(by) && __instance.IsCrop
            && __instance.def.plant.harvestedThingDef != null && __instance.def.plant.harvestFailable && !__instance.Blighted
            && __instance.YieldNow() > 0 && !(HarvestProbe.LastPlant == id && HarvestProbe.LastTick == GameFacts.Tick);
        GameFacts.Record(WorkFamily.Name, s =>
        {
            if (failed)
                WorkFamily.HarvestFailed(s);
            WorkFamily.PlantRemoved(s, id);
        });
    }
}

/// <summary>
/// Counts completed cell-storage delivery, never the early "ThingsHauled" pickup.
/// In 1.7.4985 Toils_Haul.PlaceHauledThingInCell calls the 4-argument
/// Pawn_CarryTracker.TryDropCarriedThing overload with Direct placement.
/// Its bool success comes from ThingOwner.TryDrop. The retained carried-count
/// delta measures deposited units including partial stack drops.
/// The hook observes other use cases, but only commits player HaulToCell drops
/// at the job's destination cell. No per-tick or pawn collection traversal.
/// </summary>
[MetricAdapter(ChoreFamily.Name)]
[HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.TryDropCarriedThing),
    new[] { typeof(IntVec3), typeof(ThingPlaceMode), typeof(Thing), typeof(Action<Thing, int>) },
    new[] { ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal })]
internal static class HaulToCellCompletionMetricPatch
{
    private static void Prefix(Pawn_CarryTracker __instance, IntVec3 dropLoc,
        ThingPlaceMode mode, out int __state)
    {
        __state = 0;
        // Avoid incrementing diagnostics for speculative haul deliveries.
        if (LiveMetrics.Accumulator?.Family(ChoreFamily.Name) is null)
            return;
        var actor = __instance.pawn;
        var job = actor?.CurJob;
        if (actor == null || !GameFacts.IsPlayer(actor) ||
            job?.def != JobDefOf.HaulToCell || mode != ThingPlaceMode.Direct ||
            job.targetB.Cell != dropLoc)
            return;
        __state = __instance.CarriedThing?.stackCount ?? 0;
    }

    private static void Postfix(Pawn_CarryTracker __instance, bool __result, int __state)
    {
        if (!__result || __state <= 0)
            return;
        var remaining = __instance.CarriedThing?.stackCount ?? 0;
        int deposited = __state - remaining;
        if (deposited > 0)
            GameFacts.Record(ChoreFamily.Name, state => ChoreFamily.HaulDeposited(state, deposited));
    }
}

/// <summary>
/// Marks only the corpse of the pawn actually killed by a player Slaughter job.
/// The game's AnimalsSlaughtered record is incremented *after* DoExecutionByCut.
/// Hunting can also increment AnimalsSlaughtered, so the exact job type is required.
/// The corpse identity is stored as checkpoint Active data and can cross save/load.
/// </summary>
internal static class SlaughterProvenanceMetric
{
    public static void Observe(Pawn actor)
    {
        if (actor.jobs?.curDriver is not JobDriver_Slaughter ||
            actor.CurJob?.targetA.Thing is not Pawn animal ||
            !animal.RaceProps.Animal || !animal.Dead || !GameFacts.IsPlayer(animal) ||
            animal.Corpse is not { Destroyed: false } corpse)
            return;
        var corpseId = GameFacts.Id(corpse);
        var tick = GameFacts.Tick;
        GameFacts.Record(ChoreFamily.Name, state => ChoreFamily.MarkSlaughteredCorpse(state, corpseId, tick));
    }
}

/// <summary>
/// 1.7 Toils_Recipe consumes ingredients then calls Bill.Notify_IterationCompleted,
/// then RecordsUtility.Notify_BillDone. The completed Bill receives the original
/// ingredient list even after consumption. Only tracked corpse IDs and the exact
/// vanilla flesh-butcher recipe qualify; cremation and hunting can never qualify.
/// </summary>
[MetricAdapter(ChoreFamily.Name)]
[HarmonyPatch(typeof(Bill_Production), nameof(Bill_Production.Notify_IterationCompleted))]
internal static class SlaughteredButcheryMetricPatch
{
    private static void Postfix(Bill_Production __instance, Pawn billDoer, List<Thing> ingredients)
    {
        if (billDoer == null || !GameFacts.IsPlayer(billDoer) ||
            ingredients is not { Count: > 0 } ||
            LiveMetrics.Accumulator?.Family(ChoreFamily.Name) is null)
            return;
        var isButcherFlesh = __instance.recipe?.defName == "ButcherCorpseFlesh";
        // Each input corpse can be counted at most once even when a bill has duplicates.
        HashSet<string> uniqueCorpses = new(StringComparer.Ordinal);
        foreach (var ingredient in ingredients)
        {
            if (ingredient is Corpse corpse)
                uniqueCorpses.Add(GameFacts.Id(corpse));
        }
        if (uniqueCorpses.Count == 0)
            return;
        GameFacts.Record(ChoreFamily.Name, state =>
        {
            foreach (var corpseId in uniqueCorpses)
                ChoreFamily.CorpseConsumed(state, corpseId, isButcherFlesh);
        });
    }
}

/// <summary>
/// A non-butchered corpse can decay, burn or be destroyed by another job.
/// The destruction is recorded as pending; the butcher bill's completed
/// notification can consume its marker later in the same tick.
/// Only installed for the optional chore acquisition unit.
/// </summary>
[MetricAdapter(ChoreFamily.Name)]
[HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
internal static class SlaughteredCorpseDestroyedMetricPatch
{
    private static void Prefix(Thing __instance, out string? __state)
    {
        __state = null;
        if (__instance is not Corpse corpse || LiveMetrics.Accumulator?.Family(ChoreFamily.Name) is null)
            return;
        try
        {
            __state = GameFacts.Id(corpse);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(ChoreFamily.Name, e);
        }
    }

    private static void Postfix(Thing __instance, string? __state)
    {
        if (__state is null || !__instance.Destroyed)
            return;
        var tick = GameFacts.Tick;
        GameFacts.Record(ChoreFamily.Name, state => ChoreFamily.CorpseDestroyed(state, __state, tick));
    }
}

/// <summary>Vanilla success records for sowing, removed filth, and extinguished fires.</summary>
/// <summary>Job completion is recorded only after at least one successful removal.
/// Pending positive work is keyed by persistent Job ID and discarded on cancellation.</summary>
[MetricAdapter(ChoreFamily.Name)]
[HarmonyPatch(typeof(Verse.AI.Pawn_JobTracker), nameof(Verse.AI.Pawn_JobTracker.EndCurrentJob))]
internal static class CleanJobCompletedMetricPatch
{
    private static void Prefix(Verse.AI.Pawn_JobTracker __instance, Verse.AI.JobCondition condition)
    {
        var actor = __instance.curDriver?.pawn;
        if (actor == null || !GameFacts.IsPlayer(actor) ||
            __instance.curDriver is not JobDriver_CleanFilth ||
            __instance.curJob is not { } job)
            return;
        string jobId = job.GetUniqueLoadID();
        GameFacts.Record(ChoreFamily.Name, state =>
            ChoreFamily.CleaningJobEnded(state, jobId, condition == Verse.AI.JobCondition.Succeeded));
    }
}

[MetricAdapter(WorkFamily.Name, ChoreFamily.Name)]
[HarmonyPatch(typeof(Pawn_RecordsTracker), nameof(Pawn_RecordsTracker.Increment))]
public static class PlantsSownMetricPatch
{
    public static void Postfix(Pawn_RecordsTracker __instance, RecordDef def)
    {
        // 1.7.4985: the clean-filth toil increments MessesCleaned only after
        // Filth.ThinFilth leaves the filth Destroyed. Both fire-fighting toils
        // increment FiresExtinguished after target fire.Destroyed; self-extinguish
        // destroys fire before the record increment. Reuse the same already-patched
        // tracker instead of adding a per-tick or extra Harmony interception.
        if (__instance.pawn is not { } actor || !GameFacts.IsPlayer(actor))
            return;
        if (def == RecordDefOf.AnimalsSlaughtered)
        {
            SlaughterProvenanceMetric.Observe(actor);
            return;
        }
        if (def == RecordDefOf.MessesCleaned)
        {
            GameFacts.Record(ChoreFamily.Name, state =>
            {
                ChoreFamily.FilthCleared(state);
                if (actor.jobs?.curDriver is JobDriver_CleanFilth && actor.CurJob is { } job)
                    ChoreFamily.CleanedWithinJob(state, job.GetUniqueLoadID());
            });
            return;
        }
        if (def == RecordDefOf.FiresExtinguished)
        {
            GameFacts.Record(ChoreFamily.Name, ChoreFamily.FireCleared);
            return;
        }
        if (def != RecordDefOf.PlantsSown)
            return;
        if (actor.CurJob?.targetA.Thing is not Plant plant || !plant.Spawned)
            return;
        var id = GameFacts.Id(plant);
        var crop = plant.def.defName;
        GameFacts.Record(WorkFamily.Name, s => WorkFamily.Sown(s, id, crop));
    }
}

/// <summary>A crop made leafless dies when its def says so (Plant.MakeLeafless): cold is one cause, poison and pollution others.</summary>
[MetricAdapter(WorkFamily.Name)]
[HarmonyPatch(typeof(Plant), nameof(Plant.MakeLeafless))]
public static class CropLeaflessMetricPatch
{
    public static void Prefix(Plant __instance, out string? __state)
        => __state = __instance != null && __instance.Spawned && __instance.Map?.IsPlayerHome == true && __instance.IsCrop && !__instance.Destroyed ? GameFacts.Id(__instance) : null;

    public static void Postfix(Plant __instance, Plant.LeaflessCause cause, string? __state)
    {
        if (__state is not { } id || !__instance.Destroyed)
            return;
        var cold = cause == Plant.LeaflessCause.Cold;
        GameFacts.Record(WorkFamily.Name, s => WorkFamily.CropDied(s, id, cold));
    }
}

[MetricAdapter(WorkFamily.Name, WorkDetailsFamily.Name)]
[HarmonyPatch(typeof(RecordsUtility), nameof(RecordsUtility.Notify_BillDone))]
public static class BillDoneMetricPatch
{
    public static void Postfix(Pawn billDoer, List<Thing> products)
    {
        if (billDoer == null || products == null || !GameFacts.IsPlayer(billDoer))
            return;
        var items = products.Where(p => p != null && p.stackCount > 0).Select(p => new CraftedThing
        {
            Def = p.def.defName,
            Count = p.stackCount,
            Quality = p.TryGetQuality(out var q) ? q.ToString() : null,
            UnitMarketValue = p.MarketValue,
            Meal = p.def.IsIngestible && p.def.ingestible.IsMeal,
            Art = p.TryGetComp<CompArt>() != null,
        }).ToList();
        var worker = GameFacts.Id(billDoer);
        var tick = GameFacts.Tick;
        GameFacts.Record(WorkFamily.Name, s => WorkFamily.Crafted(s, worker, items, tick));
        GameFacts.Record(WorkDetailsFamily.Name, s => WorkDetailsFamily.CraftedProducts(s, items));
    }
}

[MetricAdapter(WorkFamily.Name)]
[HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
public static class FrameCompleteMetricPatch
{
    public static void Prefix(Frame __instance, out Dictionary<string, int>? __state)
    {
        __state = null;
        if (__instance == null || !GameFacts.IsPlayer(__instance) || LiveMetrics.Family(WorkFamily.Name) is null)
            return;
        __state = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var t in __instance.resourceContainer)
            if (t?.def != null)
                __state[t.def.defName] = __state.GetValueOrDefault(t.def.defName) + t.stackCount;
    }

    public static void Postfix(Frame __instance, Pawn worker, Dictionary<string, int>? __state)
    {
        if (__state == null || !__instance.Destroyed)
            return;
        var id = worker != null ? GameFacts.Id(worker) : null;
        GameFacts.Record(WorkFamily.Name, s => WorkFamily.Constructed(s, id, __state));
    }
}

[MetricAdapter(WorkFamily.Name)]
[HarmonyPatch(typeof(Frame), nameof(Frame.FailConstruction))]
public static class FrameFailMetricPatch
{
    public static void Prefix(Frame __instance, out bool __state) => __state = __instance != null && GameFacts.IsPlayer(__instance);

    public static void Postfix(bool __state)
    {
        if (__state)
            GameFacts.Record(WorkFamily.Name, WorkFamily.ConstructionFailed);
    }
}

/// <summary>A deconstruction order completed (not the deconstruction step of a replacing blueprint).</summary>
[MetricAdapter(WorkFamily.Name)]
[HarmonyPatch(typeof(JobDriver_Deconstruct), "FinishedRemoving")]
public static class DeconstructMetricPatch
{
    public static void Prefix(JobDriver_Deconstruct __instance, out Thing? __state)
    {
        var target = __instance?.job?.targetA.Thing;
        __state = __instance?.GetType() == typeof(JobDriver_Deconstruct) && target is Building && GameFacts.IsPlayer(target) ? target : null;
    }

    public static void Postfix(Thing? __state)
    {
        if (__state != null && __state.Destroyed)
            GameFacts.Record(WorkFamily.Name, WorkFamily.Deconstructed);
    }
}

[MetricAdapter(WorkFamily.Name)]
[HarmonyPatch(typeof(ListerBuildingsRepairable), nameof(ListerBuildingsRepairable.Notify_BuildingRepaired))]
public static class RepairedMetricPatch
{
    public static void Postfix(Building b)
    {
        if (b != null && GameFacts.IsPlayer(b) && b.HitPoints >= b.MaxHitPoints)
            GameFacts.Record(WorkFamily.Name, WorkFamily.Repaired);
    }
}

/// <summary>Mining yield (evidence #work): the stack Mineable.TrySpawnYield places for a player miner, inside its scope.</summary>
internal static class MiningProbe
{
    public static Pawn? Miner;
    public static ThingDef? Yield;
}

[MetricAdapter(WorkFamily.Name, WorkDetailsFamily.Name)]
[HarmonyPatch(typeof(Mineable), "TrySpawnYield", new[] { typeof(Map), typeof(bool), typeof(Pawn) })]
public static class MiningScopeMetricPatch
{
    public static void Prefix(Mineable __instance, Pawn pawn, out bool __state)
    {
        __state = false;
        if (pawn == null || !GameFacts.IsPlayer(pawn) || __instance?.def?.building?.mineableThing == null || MiningProbe.Miner != null)
            return;
        MiningProbe.Miner = pawn;
        MiningProbe.Yield = __instance.def.building.mineableThing;
        __state = true;
    }

    public static Exception? Finalizer(Exception? __exception, bool __state)
    {
        if (__state)
        {
            MiningProbe.Miner = null;
            MiningProbe.Yield = null;
        }
        return __exception;
    }
}

[MetricAdapter(WorkFamily.Name, WorkDetailsFamily.Name, ChoreFamily.Name)]
[HarmonyPatch(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
    new[] { typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode), typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int) })]
public static class MiningPlaceMetricPatch
{
    public sealed class Placement
    {
        public string? MinerId;
        public required ThingDef YieldDef;
        public bool CutTreeWood;
        public int Placed;
    }

    // GenPlace's placedAction reports quantities that actually got placed/merged.
    // Reading thing.stackCount in a prefix counted attempts (including failed placement).
    // Wrap rather than replace the caller's action so vanilla and other mods still receive it.
    public static void Prefix(Thing thing, Map map, ref Action<Thing, int> placedAction, out Placement? __state)
    {
        __state = null;
        if (thing?.def == null)
            return;
        var miner = MiningProbe.Miner;
        bool isMineYield = miner != null && thing.def == MiningProbe.Yield;
        bool isTreeWood = TreeWoodPlacementProbe.Take(thing, map);
        if (!isMineYield && !isTreeWood)
            return;
        var state = new Placement { MinerId = isMineYield ? GameFacts.Id(miner!) : null,
            YieldDef = thing.def, CutTreeWood = isTreeWood };
        __state = state;
        var original = placedAction;
        placedAction = (placedThing, count) =>
        {
            original?.Invoke(placedThing, count);
            if (placedThing?.def == state.YieldDef && count > 0)
                state.Placed = state.Placed > int.MaxValue - count ? int.MaxValue : state.Placed + count;
        };
    }

    // Preserve the callback's successful partial placements even if TryPlaceThing returns false
    // because a remaining portion could not be placed. Exceptions never reach this postfix.
    public static void Postfix(Placement? __state)
    {
        if (__state is not { Placed: > 0 } state)
            return;
        if (state.MinerId is { } miner)
        {
            GameFacts.Record(WorkFamily.Name, s => WorkFamily.Mined(s, miner, state.YieldDef.defName, state.Placed));
            GameFacts.Record(WorkDetailsFamily.Name, s => WorkDetailsFamily.MinedYield(s, state.YieldDef.defName, state.Placed));
        }
        if (state.CutTreeWood)
            GameFacts.Record(ChoreFamily.Name, s => ChoreFamily.WoodObtained(s, state.Placed));
    }
}

[MetricAdapter(WorkFamily.Name)]
[HarmonyPatch(typeof(CompLaunchable), nameof(CompLaunchable.TryLaunch))]
public static class PodsLaunchMetricPatch
{
    public static void Prefix(CompLaunchable __instance, out int __state)
        => __state = __instance?.parent is { Spawned: true } p && GameFacts.IsPlayer(p) ? p.TryGetComp<CompTransporter>()?.TransportersInGroup(p.Map)?.Count ?? 0 : 0;

    public static void Postfix(CompLaunchable __instance, int __state)
    {
        if (__state > 0 && __instance.parent.Destroyed)
            GameFacts.Record(WorkFamily.Name, s => WorkFamily.PodsLaunched(s, __state));
    }
}
