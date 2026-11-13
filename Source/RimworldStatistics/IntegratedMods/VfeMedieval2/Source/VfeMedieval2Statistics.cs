using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.VfeMedieval2;

/// <summary>
/// Medieval2 honey from player apiaries. The pinned unload toil has a single
/// GenPlace call after TakeOutHoney. Its placement callback reports the actual
/// stack quantities placed, even with split or merged output; later hauling is
/// deliberately outside the production counter.
/// </summary>
internal static class VfeMedieval2Statistics
{
    private const string Owner = "OskarPotocki.VFE.Medieval2";
    private const string Unit = "mod.oskarpotocki.vfe.medieval2.apiaryharvest";
    private const string Key = Unit + ".honey_items";

    private static PropertyInfo? apiaryProperty;
    private static PropertyInfo? readyProperty;
    private static FieldInfo? jobDriverPawnField;
    [ThreadStatic] private static HarvestScope? active;

    private sealed class HarvestScope
    {
        internal required Map Map { get; init; }
        internal required HarvestScope? Parent { get; init; }
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(
            output, "honey", Key, withBreakdown:true),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.apiaryharvest",
            OwnerPackageId = Owner,
            FamilyId = "apiaryharvest",
            MetricName = "honey_items",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = true,
            RequiredPackages = [new ModPackageRequirement(Owner)],
            LabelKey = "RWS_VfeMedieval2_HoneyItems",
            DescriptionKey = "RWS_VfeMedieval2_HoneyItemsDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var driver = AccessTools.TypeByName("VFEMedieval.JobDriver_TakeHoneyOutOfApiary");
        var apiary = AccessTools.TypeByName("VFEMedieval.Building_Apiary");
        if (driver is null || apiary is null ||
            !typeof(JobDriver).IsAssignableFrom(driver) ||
            !typeof(Building).IsAssignableFrom(apiary) ||
            !string.Equals(driver.Assembly.GetName().Name, "VFEMedieval",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(driver.Assembly, apiary.Assembly))
            throw new InvalidOperationException("Selected Medieval2 1.7 apiary assembly is absent");

        var unload = AccessTools.DeclaredMethod(driver, "<MakeNewToils>b__5_1", Type.EmptyTypes);
        var place = AccessTools.DeclaredMethod(typeof(GenPlace), "TryPlaceThing",
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing,int>), typeof(Predicate<IntVec3>)]);
        var property = AccessTools.Property(driver, "Apiary");
        var ready = AccessTools.Property(apiary, "HoneyReady");
        var pawn = AccessTools.Field(typeof(JobDriver), "pawn");
        if (unload?.ReturnType != typeof(void) || place?.ReturnType != typeof(bool) ||
            property?.PropertyType != apiary || ready?.PropertyType != typeof(bool) ||
            pawn?.FieldType != typeof(Pawn))
            throw new InvalidOperationException("Medieval2 apiary unload and placement signatures differ");

        apiaryProperty = property;
        readyProperty = ready;
        jobDriverPawnField = pawn;
        try
        {
            harmony.Patch(unload,
                prefix:new HarmonyMethod(typeof(VfeMedieval2Statistics), nameof(BeforeUnload)),
                finalizer:new HarmonyMethod(typeof(VfeMedieval2Statistics), nameof(FinishUnload)));
            harmony.Patch(place,
                prefix:new HarmonyMethod(typeof(VfeMedieval2Statistics), nameof(BeforePlace)));
        }
        catch
        {
            harmony.Unpatch(unload, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(place, HarmonyPatchType.All, harmony.Id);
            apiaryProperty = readyProperty = null;
            jobDriverPawnField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeUnload(object __instance, out HarvestScope? __state)
    {
        __state = null;
        if (!CanObserve())
            return;
        try
        {
            if (jobDriverPawnField?.GetValue(__instance) is not Pawn pawn ||
                !GameFacts.IsMember(pawn) ||
                apiaryProperty?.GetValue(__instance) is not Building apiary ||
                readyProperty?.GetValue(apiary) is not true ||
                !apiary.Spawned || apiary.Faction != Faction.OfPlayer ||
                apiary.Map is not { IsPlayerHome:true } map ||
                pawn.MapHeld != map)
                return;
            __state = new HarvestScope { Map = map, Parent = active };
            active = __state;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void BeforePlace(Thing __0, Map __2, ref Action<Thing,int>? __4)
    {
        if (active is not { } scope || !CanObserve() ||
            !ReferenceEquals(__2, scope.Map) || __0?.def?.defName != "VFEM2_Honey")
            return;
        var originalAction = __4;
        var def = __0.def;
        var key = OptionalTransitionRecording.QualifiedDefKey(
            def.modContentPack?.PackageId, def.defName);
        __4 = (thing, placed) =>
        {
            // Keep the exact original placement callback and its order.
            originalAction?.Invoke(thing, placed);
            try
            {
                var count = VfeMedieval2Rules.HoneyPlaced(placed,
                    playerOwned: true, matchingApiary: ReferenceEquals(active, scope));
                if (count > 0 && CanObserve())
                    GameFacts.Record(Unit, state => OptionalTransitionRecording.RecordCount(
                        state, "honey", count, key, withBreakdown: true));
            }
            catch (Exception e) { LiveMetrics.Fail(Unit, e); }
        };
    }

    private static Exception? FinishUnload(Exception? __exception, HarvestScope? __state)
    {
        if (__state is not null && ReferenceEquals(active, __state))
            active = __state.Parent;
        return __exception;
    }
}
