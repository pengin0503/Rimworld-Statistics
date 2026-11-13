using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfeFactory;

/// <summary>
/// Per-row VFE Factory Autofarmer harvesting and sowing. Counts output added
/// to its harvest buffer, placed output observed through the vanilla placement
/// callback, and newly spawned sown plants. Offloading is not production.
/// </summary>
internal static class VfeFactoryStatistics
{
    private const string Owner = "VanillaExpanded.VFEFactory";
    private const string Unit = "mod.vanillaexpanded.vfefactory.autofarming";
    private const string HarvestKey = Unit + ".harvested_items";
    private const string SowKey = Unit + ".plants_sown";
    private static MethodInfo? getRowCells;
    private static FieldInfo? storageField;
    private static FieldInfo? selectedPlantField;
    [ThreadStatic] private static RowScope? active;

    private sealed class RowScope
    {
        internal required Map Map { get; init; }
        internal required HashSet<Plant> ExistingPlants { get; init; }
        internal required IntVec3[] Cells { get; init; }
        internal required Dictionary<ThingDef,long> BufferBefore { get; init; }
        internal required ThingDef? SelectedPlant { get; init; }
        internal required RowScope? Parent { get; init; }
        internal readonly Dictionary<ThingDef,long> Placed = new();
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            OptionalTransitionRecording.PutCount(output, "harvested", HarvestKey, withBreakdown: true);
            OptionalTransitionRecording.PutCount(output, "sown", SowKey, withBreakdown: true);
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor D(string name, string label, string desc) => new()
        {
            ProviderId = "rws.autofarming",
            OwnerPackageId = Owner,
            FamilyId = "autofarming",
            MetricName = name,
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = label,
            DescriptionKey = desc,
        };
        return
        [
            new ModMetricAdapterRegistration(
                D("harvested_items", "RWS_VfeFactory_Harvested", "RWS_VfeFactory_HarvestedDesc"),
                Family, Install),
            new ModMetricAdapterRegistration(
                D("plants_sown", "RWS_VfeFactory_Sown", "RWS_VfeFactory_SownDesc"),
                Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var type = AccessTools.TypeByName("VanillaFurnitureExpandedFactory.Building_Autofarmer");
        if (type is null || !typeof(Building).IsAssignableFrom(type) ||
            !string.Equals(type.Assembly.GetName().Name, "VanillaFurnitureExpandedFactory",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Selected VFE Factory 1.7 Autofarmer assembly absent");

        var row = AccessTools.DeclaredMethod(type, "ProcessRow", [typeof(int)]);
        var cells = AccessTools.DeclaredMethod(type, "GetRowCells", [typeof(int)]);
        var buffer = AccessTools.DeclaredField(type, "harvestStorageThings");
        var plant = AccessTools.DeclaredField(type, "plantDefToGrow");
        var place = AccessTools.DeclaredMethod(typeof(GenPlace), "TryPlaceThing",
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing,int>), typeof(Predicate<IntVec3>)]);
        if (row?.ReturnType != typeof(void) ||
            cells is null || !typeof(IEnumerable<IntVec3>).IsAssignableFrom(cells.ReturnType) ||
            buffer is null || !typeof(IList).IsAssignableFrom(buffer.FieldType) ||
            plant?.FieldType != typeof(ThingDef) ||
            place?.ReturnType != typeof(bool))
            throw new InvalidOperationException("VFE Factory Autofarmer harvest/sow signature changed");

        getRowCells = cells;
        storageField = buffer;
        selectedPlantField = plant;
        try
        {
            harmony.Patch(row,
                prefix: new HarmonyMethod(typeof(VfeFactoryStatistics), nameof(BeforeRow)),
                postfix: new HarmonyMethod(typeof(VfeFactoryStatistics), nameof(AfterRow)),
                finalizer: new HarmonyMethod(typeof(VfeFactoryStatistics), nameof(FinishRow)));
            harmony.Patch(place,
                prefix: new HarmonyMethod(typeof(VfeFactoryStatistics), nameof(BeforeGroundPlace)));
        }
        catch
        {
            harmony.Unpatch(row, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(place, HarmonyPatchType.All, harmony.Id);
            getRowCells = null;
            storageField = selectedPlantField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static Dictionary<ThingDef,long> Buffered(object machine)
    {
        var result = new Dictionary<ThingDef,long>();
        if (storageField?.GetValue(machine) is not IList things)
            return result;
        foreach (var item in things)
        {
            if (item is not Thing { def: { } def } thing || thing.stackCount <= 0)
                continue;
            result.TryGetValue(def, out var previous);
            result[def] = previous + thing.stackCount;
        }
        return result;
    }

    private static void BeforeRow(object __instance, int __0, out RowScope? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not Building { Spawned: true } building ||
            building.Faction != Faction.OfPlayer ||
            !string.Equals(building.def?.modContentPack?.PackageId, Owner,
                StringComparison.OrdinalIgnoreCase) ||
            building.Map is not { IsPlayerHome: true } map)
            return;
        try
        {
            if (getRowCells?.Invoke(__instance, [__0]) is not IEnumerable<IntVec3> cellSequence)
                return;
            var cells = cellSequence.ToArray();
            var existingPlants = cells.Select(c => c.GetPlant(map)).OfType<Plant>().ToHashSet();
            __state = new RowScope
            {
                Map = map,
                ExistingPlants = existingPlants,
                Cells = cells,
                BufferBefore = Buffered(__instance),
                SelectedPlant = selectedPlantField?.GetValue(__instance) as ThingDef,
                Parent = active,
            };
            active = __state;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforeGroundPlace(Thing __0, Map __2, ref Action<Thing,int>? __4)
    {
        if (active is not { } scope || !CanObserve() ||
            !ReferenceEquals(__2, scope.Map) || __0?.def is not { } def)
            return;
        var originalAction = __4;
        __4 = (thing, count) =>
        {
            originalAction?.Invoke(thing, count);
            try
            {
                if (count <= 0 || !ReferenceEquals(scope, active))
                    return;
                scope.Placed.TryGetValue(def, out var prior);
                scope.Placed[def] = checked(prior + count);
            }
            catch (Exception e) { LiveMetrics.Fail(Unit, e); }
        };
    }

    private static void AfterRow(object __instance, bool __runOriginal, RowScope? __state)
    {
        if (!__runOriginal || __state is null ||
            !ReferenceEquals(active, __state) || !CanObserve())
            return;
        try
        {
            var grown = Buffered(__instance);
            var harvested = new Dictionary<ThingDef,long>(__state.Placed);
            foreach (var (def, after) in grown)
            {
                __state.BufferBefore.TryGetValue(def, out var before);
                var difference = VfeFactoryRules.BufferYield(before, after);
                if (difference <= 0)
                    continue;
                harvested.TryGetValue(def, out var placed);
                harvested[def] = checked(placed + difference);
            }

            var newlySown = __state.Cells
                .Select(c => c.GetPlant(__state.Map)).OfType<Plant>()
                .Where(p => VfeFactoryRules.Sown(
                    newPlant: !__state.ExistingPlants.Contains(p),
                    sown: p.sown && p.Spawned,
                    kindMatches: ReferenceEquals(p.def, __state.SelectedPlant),
                    playerOwned: p.Map == __state.Map)).ToArray();
            if (newlySown.Length == 0 && harvested.Count == 0)
                return;
            GameFacts.Record(Unit, state =>
            {
                foreach (var (def, amount) in harvested)
                {
                    if (amount <= 0)
                        continue;
                    var key = OptionalTransitionRecording.QualifiedDefKey(
                        def.modContentPack?.PackageId, def.defName);
                    OptionalTransitionRecording.RecordCount(state, "harvested",
                        amount, key, withBreakdown: true);
                }
                foreach (var plant in newlySown)
                {
                    var def = plant.def;
                    var key = OptionalTransitionRecording.QualifiedDefKey(
                        def.modContentPack?.PackageId, def.defName);
                    OptionalTransitionRecording.RecordCount(state, "sown",
                        key: key, withBreakdown: true);
                }
            });
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishRow(Exception? __exception, RowScope? __state)
    {
        if (__state is not null && ReferenceEquals(active, __state))
            active = __state.Parent;
        return __exception;
    }
}
