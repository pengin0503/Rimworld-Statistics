using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeSpacer;

/// <summary>
/// Vanilla Furniture Expanded - Spacer Module: records actual hit points
/// restored and items brought back to full MaxHitPoints by player-owned
/// powered MFSpacer.CompRepairStored shelves on scheduled repair ticks.
/// </summary>
internal static class VfeSpacerStatistics
{
    private const string Unit = "mod.vanillaexpanded.vfespacer.repair_shelf";
    private const string HpKey = Unit + ".hp_repaired";
    private const string ItemsKey = Unit + ".items_repaired";

    private static Func<object, CompPowerTrader?>? getPowerComp;
    private static Func<object, Building_Storage?>? getStorage;
    private static Func<object, int>? getRepairInterval;

    private readonly record struct ItemSnapshot(Thing Item, int BeforeHp, int MaxHp);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            output.PutCount("hp_repaired", HpKey);
            OptionalTransitionRecording.PutCount(output, "items_repaired", ItemsKey, withBreakdown: true);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor Definition(string metricName, string label, string desc, bool hasBreakdown) => new()
        {
            ProviderId = "rws.vfe_spacer",
            OwnerPackageId = VfeSpacerRules.OwnerPackageId,
            FamilyId = "repair_shelf",
            MetricName = metricName,
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Facilities,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = hasBreakdown,
            RequiredPackages =
            [
                new ModPackageRequirement(VfeSpacerRules.OwnerPackageId),
                new ModPackageRequirement(VfeSpacerRules.FrameworkPackageId),
            ],
            LabelKey = label,
            DescriptionKey = desc,
        };

        return
        [
            new ModMetricAdapterRegistration(
                Definition("hp_repaired", "RWS_VfeSpacer_RepairShelfHp", "RWS_VfeSpacer_RepairShelfHpDesc", false),
                Family, Install),
            new ModMetricAdapterRegistration(
                Definition("items_repaired", "RWS_VfeSpacer_RepairShelfItems", "RWS_VfeSpacer_RepairShelfItemsDesc", true),
                Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var compType = AccessTools.TypeByName("MFSpacer.CompRepairStored");
        var propsType = AccessTools.TypeByName("MFSpacer.CompProperties_RepairStored");
        if (compType is null || propsType is null ||
            !string.Equals(compType.Assembly.GetName().Name, "MFSpacer", StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(compType.Assembly, propsType.Assembly) ||
            !typeof(ThingComp).IsAssignableFrom(compType))
            throw new InvalidOperationException("Selected 1.7 MFSpacer CompRepairStored assembly not available");

        var tick = AccessTools.DeclaredMethod(compType, "TickInterval", [typeof(int)]);
        var powerField = AccessTools.DeclaredField(compType, "powerComp");
        var storeField = AccessTools.DeclaredField(compType, "storage");
        var intervalField = AccessTools.DeclaredField(propsType, "repairInterval");
        if (tick?.ReturnType != typeof(void) ||
            powerField?.FieldType != typeof(CompPowerTrader) ||
            storeField is null || !typeof(Building_Storage).IsAssignableFrom(storeField.FieldType) ||
            intervalField?.FieldType != typeof(int))
            throw new InvalidOperationException("MFSpacer.CompRepairStored contract changed");

        getPowerComp = OptionalTransitionCallSite.CompileFieldGetter<CompPowerTrader?>(powerField);
        getStorage = OptionalTransitionCallSite.CompileFieldGetter<Building_Storage?>(storeField);
        getRepairInterval = OptionalTransitionCallSite.CompileFieldGetter<int>(intervalField);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(tick, typeof(VfeSpacerStatistics), nameof(BeforeTick), nameof(AfterTick));
        }
        catch
        {
            getPowerComp = null;
            getStorage = null;
            getRepairInterval = null;
            throw;
        }
    }

    private static bool CanObserve() => OptionalTransitionCallSite.Observing(Unit);

    private static void BeforeTick(ThingComp __instance, int __0, out List<ItemSnapshot>? __state)
    {
        __state = null;
        if (!CanObserve())
            return;

        try
        {
            if (__instance.props is null || getRepairInterval is null)
                return;
            var interval = getRepairInterval(__instance.props);
            if (interval <= 0)
                return;

            if (getStorage?.Invoke(__instance) is not { Spawned: true } storage ||
                !GameFacts.IsPlayerFaction(storage.Faction) ||
                !storage.IsHashIntervalTick(interval, __0))
                return;

            if (getPowerComp?.Invoke(__instance) is { PowerOn: false })
                return;

            var settings = storage.GetParentStoreSettings();
            var held = storage.GetSlotGroup()?.HeldThings;
            if (held is null || settings is null)
                return;

            foreach (var thing in held)
            {
                if (thing is null || thing.Destroyed || thing.def is null || !settings.AllowedToAccept(thing))
                    continue;
                var hp = thing.HitPoints;
                var max = thing.MaxHitPoints;
                if (max > 0 && hp >= 0 && hp < max)
                {
                    __state ??= new List<ItemSnapshot>();
                    __state.Add(new ItemSnapshot(thing, hp, max));
                }
            }
        }
        catch (Exception e)
        {
            __state = null;
            LiveMetrics.Fail(Unit, e);
        }
    }

    private static void AfterTick(bool __runOriginal, List<ItemSnapshot>? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;

        try
        {
            long totalHp = 0;
            Dictionary<string, long>? completedByDef = null;
            long missingKeyCompleted = 0;

            foreach (var snapshot in __state)
            {
                if (snapshot.Item.Destroyed)
                    continue;
                var outcome = VfeSpacerRules.EvaluateRepair(snapshot.BeforeHp, snapshot.Item.HitPoints, snapshot.MaxHp);
                if (outcome.HpRestored <= 0)
                    continue;
                totalHp += outcome.HpRestored;
                if (outcome.CompletedItem)
                {
                    var key = OptionalTransitionRecording.QualifiedDefKey(
                        snapshot.Item.def?.modContentPack?.PackageId, snapshot.Item.def?.defName);
                    if (key is null)
                        missingKeyCompleted++;
                    else
                    {
                        completedByDef ??= new Dictionary<string, long>(StringComparer.Ordinal);
                        completedByDef[key] = completedByDef.GetValueOrDefault(key) + 1;
                    }
                }
            }

            if (totalHp <= 0 && missingKeyCompleted <= 0 && (completedByDef is null || completedByDef.Count == 0))
                return;

            GameFacts.Record(Unit, state =>
            {
                if (totalHp > 0)
                    state.Count("hp_repaired", totalHp);
                if (completedByDef is not null)
                    foreach (var (key, count) in completedByDef)
                        OptionalTransitionRecording.RecordCount(state, "items_repaired", count, key, withBreakdown: true);
                if (missingKeyCompleted > 0)
                    OptionalTransitionRecording.RecordCount(state, "items_repaired", missingKeyCompleted, null, withBreakdown: true);
            });
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(Unit, e);
        }
    }
}
