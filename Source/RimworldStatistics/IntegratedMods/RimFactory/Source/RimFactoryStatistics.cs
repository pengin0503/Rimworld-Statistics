using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.RimFactory;

/// <summary>Verified ground placement from the Auto Machine Tool product path.</summary>
internal static class RimFactoryStatistics
{
    private const string Owner = "spdskatr.projectrimfactory";
    private const string Unit = "mod.spdskatr.projectrimfactory.machineoutput";
    private const string Key = Unit + ".ground_placed";
    private static Type? autoMachine;

    [ThreadStatic] private static Placement? active;
    private sealed record Placement(Thing Item, Map Map, int StartingStack, Placement? Parent)
    {
        public long Confirmed { get; set; }
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "placed", Key, withBreakdown: true),
    };

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        ProviderId = "rws.rimfactorygroundoutput",
        OwnerPackageId = Owner,
        FamilyId = "machineoutput",
        MetricName = "ground_placed",
        Source = MetricObservationSource.Checkpoint,
        Revision = 1,
        Category = MetricCategory.Work,
        Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        HasBreakdown = true,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_RimFactory_GroundOutput",
        DescriptionKey = "RWS_RimFactory_GroundOutputDesc",
    }, Family, Install);

    private static void Install(Harmony harmony)
    {
        var utility = AccessTools.TypeByName("ProjectRimFactory.Common.PlaceThingUtility");
        var assembly = utility?.Assembly;
        var machine = assembly?.GetType("ProjectRimFactory.AutoMachineTool.Building_AutoMachineTool");
        var placer = assembly?.GetType("ProjectRimFactory.Common.IPRF_Building");
        if (utility is null || machine is null || placer is null ||
            !typeof(Building).IsAssignableFrom(machine) || !placer.IsAssignableFrom(machine) ||
            !string.Equals(assembly?.GetName().Name, "ProjectRimFactory", StringComparison.Ordinal))
            throw new InvalidOperationException("Project RimFactory assembly or machine type changed");

        var place = AccessTools.DeclaredMethod(utility, "PRFTryPlaceThing",
            [placer, typeof(Thing), typeof(IntVec3), typeof(Map), typeof(bool)]);
        var ground = AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)])
            ?? AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>)]);
        if (place is not { IsStatic: true } || place.ReturnType != typeof(bool) || ground?.ReturnType != typeof(bool))
            throw new InvalidOperationException("Project RimFactory placement signature changed");

        autoMachine = machine;
        try
        {
            harmony.Patch(place,
                prefix: new HarmonyMethod(typeof(RimFactoryStatistics), nameof(BeforePlacement)),
                postfix: new HarmonyMethod(typeof(RimFactoryStatistics), nameof(AfterPlacement)),
                finalizer: new HarmonyMethod(typeof(RimFactoryStatistics), nameof(FinishPlacement)));
            harmony.Patch(ground,
                prefix: new HarmonyMethod(typeof(RimFactoryStatistics), nameof(ObserveGroundPlacement)));
        }
        catch
        {
            harmony.Unpatch(place, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(ground, HarmonyPatchType.All, harmony.Id);
            autoMachine = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing
            && Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static void BeforePlacement(object __0, Thing __1, Map __3, out Placement? __state)
    {
        __state = null;
        if (autoMachine?.IsInstanceOfType(__0) != true || !CanObserve() ||
            __0 is not Building machine || !machine.Spawned || !GameFacts.IsPlayer(machine) ||
            __3 != machine.Map || __1 is null || __1.Destroyed || __1.Spawned || __1.stackCount <= 0)
            return;

        __state = new Placement(__1, __3, __1.stackCount, active);
        active = __state;
    }

    private static void ObserveGroundPlacement(Thing __0, Map __2, ref Action<Thing, int>? __4)
    {
        if (active is not { } scope || !ReferenceEquals(scope.Item, __0) || !ReferenceEquals(scope.Map, __2))
            return;
        var existing = __4;
        __4 = (item, quantity) =>
        {
            existing?.Invoke(item, quantity);
            if (ReferenceEquals(active, scope) && quantity > 0)
                scope.Confirmed += quantity;
        };
    }

    private static void AfterPlacement(bool __runOriginal, Placement? __state)
    {
        if (!__runOriginal || __state is null || !CanObserve())
            return;
        try
        {
            var amount = RimFactoryRules.PlacedQuantity(__state.StartingStack, __state.Confirmed);
            if (amount <= 0)
                return;
            var def = __state.Item.def;
            var key = OptionalTransitionRecording.QualifiedDefKey(def.modContentPack?.PackageId, def.defName);
            GameFacts.Record(Unit, state =>
                OptionalTransitionRecording.RecordCount(state, "placed", amount, key, withBreakdown: true));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishPlacement(Exception? __exception, Placement? __state)
    {
        if (__state is not null)
            active = __state.Parent;
        return __exception;
    }
}
