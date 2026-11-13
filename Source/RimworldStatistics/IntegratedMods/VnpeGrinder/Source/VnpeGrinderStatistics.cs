using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VnpeGrinder;

/// <summary>
/// Actual nutrient slurry placed into a VNPE grinder's resource PipeNet.
/// The selected grinder's one-float DistributeAmongStorage call forwards to
/// the two-float overload. Its out parameter reports the accepted quantity.
/// </summary>
internal static class VnpeGrinderStatistics
{
    private const string Owner = "VanillaExpanded.VNutrientE";
    private const string Framework = "OskarPotocki.VanillaFactionsExpanded.Core";
    private const string Unit = "mod.vanillaexpanded.vnutriente.slurrystorage";
    private const string Key = Unit + ".stored_units";

    private static FieldInfo? resourceCompField;
    private static PropertyInfo? pipeNetProperty;
    [ThreadStatic] private static GrinderScope? active;

    private sealed class GrinderScope
    {
        internal required object Net { get; init; }
        internal required GrinderScope? Parent { get; init; }
        internal double Accepted;
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => output.Put(Key,
            output.Value(real: output.State.Peek("stored")?.Sum ?? 0d)),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.slurrystorage",
            OwnerPackageId = Owner,
            FamilyId = "slurrystorage",
            MetricName = "stored_units",
            Revision = 1,
            Category = MetricCategory.Work,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.Checkpoint,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner), new ModPackageRequirement(Framework)],
            LabelKey = "RWS_VnpeGrinder_SlurryStored",
            DescriptionKey = "RWS_VnpeGrinder_SlurryStoredDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var grinder = AccessTools.TypeByName("VNPE.Building_NutrientGrinder");
        var resource = AccessTools.TypeByName("PipeSystem.CompResource");
        var net = AccessTools.TypeByName("PipeSystem.PipeNet");
        if (grinder is null || resource is null || net is null ||
            !typeof(Building).IsAssignableFrom(grinder) ||
            !string.Equals(grinder.Assembly.GetName().Name, "VNPE",
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(resource.Assembly.GetName().Name, "PipeSystem",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(resource.Assembly, net.Assembly))
            throw new InvalidOperationException("Selected VNPE/PipeSystem 1.7 assembly unavailable");

        var produce = AccessTools.DeclaredMethod(grinder, "TryProducePaste", Type.EmptyTypes);
        var resourceField = AccessTools.DeclaredField(grinder, "resourceComp");
        var getNet = AccessTools.Property(resource, "PipeNet");
        var distribute = AccessTools.DeclaredMethod(net, "DistributeAmongStorage",
            [typeof(float), typeof(float).MakeByRefType()]);
        if (produce?.ReturnType != typeof(bool) ||
            resourceField?.FieldType != resource ||
            getNet?.PropertyType != net || getNet.GetMethod is null ||
            distribute?.ReturnType != typeof(void))
            throw new InvalidOperationException("VNPE grinder production or storage signature changed");

        resourceCompField = resourceField;
        pipeNetProperty = getNet;
        try
        {
            harmony.Patch(produce,
                prefix: new HarmonyMethod(typeof(VnpeGrinderStatistics), nameof(BeforeGrind)),
                postfix: new HarmonyMethod(typeof(VnpeGrinderStatistics), nameof(AfterGrind)),
                finalizer: new HarmonyMethod(typeof(VnpeGrinderStatistics), nameof(FinishGrind)));
            harmony.Patch(distribute,
                postfix: new HarmonyMethod(typeof(VnpeGrinderStatistics), nameof(AfterStored)));
        }
        catch
        {
            harmony.Unpatch(produce, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(distribute, HarmonyPatchType.All, harmony.Id);
            resourceCompField = null;
            pipeNetProperty = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeGrind(object __instance, out GrinderScope? __state)
    {
        __state = null;
        if (!CanObserve() || __instance is not Building
            { Spawned: true } building ||
            building.Faction != Faction.OfPlayer ||
            !string.Equals(building.def?.modContentPack?.PackageId, Owner,
                StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            if (resourceCompField?.GetValue(__instance) is not { } comp)
                return;
            var net = pipeNetProperty?.GetValue(comp);
            if (net is null)
                return;
            __state = new GrinderScope { Net = net, Parent = active };
            active = __state;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterStored(object __instance, float __1, bool __runOriginal)
    {
        if (!__runOriginal || active is not { } scope ||
            !ReferenceEquals(scope.Net, __instance) || !CanObserve())
            return;
        try
        {
            var accepted = VnpeGrinderRules.AcceptedPaste(__1, successful: true,
                matchingNet: true, playerOwned: true);
            if (accepted > 0)
                scope.Accepted += accepted;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterGrind(bool __result, bool __runOriginal, GrinderScope? __state)
    {
        if (!__result || !__runOriginal || __state is null ||
            !ReferenceEquals(active, __state) || !CanObserve())
            return;
        try
        {
            if (double.IsFinite(__state.Accepted) && __state.Accepted > 0d)
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordReal(state, "stored", __state.Accepted));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishGrind(Exception? __exception, GrinderScope? __state)
    {
        if (__state is not null && ReferenceEquals(active, __state))
            active = __state.Parent;
        return __exception;
    }
}
