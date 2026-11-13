using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Rimefeller;

/// <summary>
/// Measures real pipeline fuel debits during committed refinery and powerplant
/// ticks. The refinery baseline is captured AFTER CompPipe.CompTick, since that
/// base method can independently replenish a refuelable from the same net.
/// Compiled accessors avoid reflection/boxing on per-tick observation paths.
/// </summary>
internal static class RimefellerFuelUsageStatistics
{
    private const string Owner = "Dubwise.Rimefeller";
    private const string Unit = "mod.dubwise.rimefeller.fueluse";
    private const string RefineryKey = Unit + ".refinery_pipe_units";
    private const string PlantKey = Unit + ".powerplant_pipe_units";

    private static Func<object, object?>? getPipeNet;
    private static Func<object, object?>? getPlantPipe;
    private static Func<object, double>? getFuel;

    private struct RefineryScope
    {
        internal object? Refinery;
        internal object? Net;
        internal double BaseFuel;
        internal bool Captured;
    }

    [ThreadStatic] private static RefineryScope refinery;

    private readonly record struct FuelBefore(object? Net, double Available);

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output =>
        {
            output.Put(RefineryKey,
                output.Value(real: output.State.Peek("refinery_fuel")?.Sum ?? 0d));
            output.Put(PlantKey,
                output.Value(real: output.State.Peek("plant_fuel")?.Sum ?? 0d));
        },
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        static ModMetricDescriptor Definition(string name, string label, string desc)
            => new()
            {
                ProviderId = "rws.rimefellerfueluse",
                OwnerPackageId = Owner,
                FamilyId = "fueluse",
                MetricName = name,
                Revision = 1,
                Source = MetricObservationSource.Checkpoint,
                Category = MetricCategory.Work,
                Unit = MetricUnit.Count,
                Overall = OverallAggregation.Sum,
                Selection = ColonySelection.Representative,
                HasBreakdown = false,
                RequiredPackages = [new ModPackageRequirement(Owner)],
                LabelKey = label,
                DescriptionKey = desc,
            };

        return
        [
            new ModMetricAdapterRegistration(Definition("refinery_pipe_units",
                "RWS_Rimefeller_RefineryFuelUsed", "RWS_Rimefeller_RefineryFuelUsedDesc"),
                Family, Install),
            new ModMetricAdapterRegistration(Definition("powerplant_pipe_units",
                "RWS_Rimefeller_PlantFuelUsed", "RWS_Rimefeller_PlantFuelUsedDesc"),
                Family, Install),
        ];
    }

    private static void Install(Harmony harmony)
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a =>
            string.Equals(a.GetName().Name, "Rimefeller", StringComparison.OrdinalIgnoreCase));
        if (assembly is null)
            throw new InvalidOperationException("Rimefeller 1.7 assembly not loaded");

        var pipe = assembly.GetType("Rimefeller.CompPipe", false);
        var refineryType = assembly.GetType("Rimefeller.CompRefinery", false);
        var plantType = assembly.GetType("Rimefeller.FuelPowerplant", false);
        var netType = assembly.GetType("Rimefeller.PipelineNet", false);
        if (pipe is null || refineryType is null || plantType is null || netType is null ||
            !typeof(ThingComp).IsAssignableFrom(pipe) ||
            !pipe.IsAssignableFrom(refineryType) ||
            !typeof(ThingComp).IsAssignableFrom(plantType))
            throw new InvalidOperationException("Rimefeller 1.7 pipeline types changed");

        var baseTick = AccessTools.DeclaredMethod(pipe, "CompTick", Type.EmptyTypes);
        var refineryTick = AccessTools.DeclaredMethod(refineryType, "CompTick", Type.EmptyTypes);
        var plantTick = AccessTools.DeclaredMethod(plantType, "CompTick", Type.EmptyTypes);
        var netGetter = AccessTools.DeclaredPropertyGetter(pipe, "pipeNet");
        var plantPipe = AccessTools.DeclaredField(plantType, "PipeComp");
        var fuel = AccessTools.DeclaredField(netType, "FuelAvailable");
        if (baseTick?.ReturnType != typeof(void) ||
            refineryTick?.ReturnType != typeof(void) ||
            plantTick?.ReturnType != typeof(void) ||
            netGetter?.ReturnType != netType ||
            plantPipe?.FieldType != pipe ||
            fuel?.FieldType != typeof(double))
            throw new InvalidOperationException("Rimefeller 1.7 fuel debit signatures changed");

        // Compile field/method access only once at initialization; the high-frequency
        // callbacks below do not invoke FieldInfo/MethodInfo reflection.
        static Func<object, object?> BoxedGetter(Type type, MemberInfo member)
        {
            var arg = Expression.Parameter(typeof(object), "instance");
            var owner = Expression.Convert(arg, type);
            Expression value = member switch
            {
                FieldInfo field => Expression.Field(owner, field),
                MethodInfo method => Expression.Call(owner, method),
                _ => throw new InvalidOperationException("Unsupported Rimefeller getter"),
            };
            return Expression.Lambda<Func<object, object?>>(
                Expression.Convert(value, typeof(object)), arg).Compile();
        }

        var available = Expression.Parameter(typeof(object), "net");
        var fuelGetter = Expression.Lambda<Func<object, double>>(
            Expression.Field(Expression.Convert(available, netType), fuel),
            available).Compile();
        getPipeNet = BoxedGetter(pipe, netGetter);
        getPlantPipe = BoxedGetter(plantType, plantPipe);
        getFuel = fuelGetter;

        try
        {
            harmony.Patch(refineryTick,
                prefix: new HarmonyMethod(typeof(RimefellerFuelUsageStatistics), nameof(BeforeRefinery)),
                postfix: new HarmonyMethod(typeof(RimefellerFuelUsageStatistics), nameof(AfterRefinery)),
                finalizer: new HarmonyMethod(typeof(RimefellerFuelUsageStatistics), nameof(FinishRefinery)));
            harmony.Patch(baseTick,
                postfix: new HarmonyMethod(typeof(RimefellerFuelUsageStatistics), nameof(AfterBasePipeTick)));
            harmony.Patch(plantTick,
                prefix: new HarmonyMethod(typeof(RimefellerFuelUsageStatistics), nameof(BeforePlant)),
                postfix: new HarmonyMethod(typeof(RimefellerFuelUsageStatistics), nameof(AfterPlant)));
        }
        catch
        {
            harmony.Unpatch(refineryTick, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(baseTick, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(plantTick, HarmonyPatchType.All, harmony.Id);
            getPipeNet = getPlantPipe = null;
            getFuel = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static bool OwnedAndHome(object instance)
        => instance is ThingComp { parent: { Spawned: true } building }
           && building.Faction == Faction.OfPlayer
           && building.Map is { IsPlayerHome: true }
           && string.Equals(building.def?.modContentPack?.PackageId, Owner,
               StringComparison.OrdinalIgnoreCase);

    private static void BeforeRefinery(object __instance, out bool __state)
    {
        __state = false;
        if (refinery.Refinery is not null || !CanObserve() || !OwnedAndHome(__instance))
            return;
        refinery = new RefineryScope { Refinery = __instance };
        __state = true;
    }

    private static void AfterBasePipeTick(object __instance)
    {
        if (!ReferenceEquals(refinery.Refinery, __instance) ||
            refinery.Captured || !CanObserve())
            return;
        try
        {
            var net = getPipeNet?.Invoke(__instance);
            if (net is null || getFuel is null)
                return;
            var fuel = getFuel(net);
            if (!double.IsFinite(fuel) || fuel < 0d)
                return;
            refinery.Net = net;
            refinery.BaseFuel = fuel;
            refinery.Captured = true;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterRefinery(object __instance, bool __runOriginal, bool __state)
    {
        if (!__state || !__runOriginal ||
            !ReferenceEquals(refinery.Refinery, __instance) ||
            !refinery.Captured || !CanObserve())
            return;
        try
        {
            var net = getPipeNet?.Invoke(__instance);
            if (!ReferenceEquals(net, refinery.Net) || net is null || getFuel is null)
                return;
            var amount = RimefellerFuelUsageRules.Debited(
                refinery.BaseFuel, getFuel(net), ownedConsumer: OwnedAndHome(__instance));
            if (amount > 0d)
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordReal(state, "refinery_fuel", amount));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishRefinery(Exception? __exception, bool __state)
    {
        if (__state)
            refinery = default;
        return __exception;
    }

    private static void BeforePlant(object __instance, out FuelBefore __state)
    {
        __state = default;
        if (!CanObserve() || !OwnedAndHome(__instance) || getPlantPipe is null ||
            getPipeNet is null || getFuel is null)
            return;
        try
        {
            var pipe = getPlantPipe(__instance);
            if (pipe is null)
                return;
            var net = getPipeNet(pipe);
            if (net is null)
                return;
            var available = getFuel(net);
            if (double.IsFinite(available) && available > 0d)
                __state = new FuelBefore(net, available);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterPlant(object __instance, bool __runOriginal, FuelBefore __state)
    {
        if (!__runOriginal || __state.Net is null || !CanObserve() ||
            !OwnedAndHome(__instance) || getFuel is null || getPlantPipe is null ||
            getPipeNet is null)
            return;
        try
        {
            var pipe = getPlantPipe(__instance);
            if (pipe is null || !ReferenceEquals(getPipeNet(pipe), __state.Net))
                return;
            var amount = RimefellerFuelUsageRules.Debited(
                __state.Available, getFuel(__state.Net), ownedConsumer: true);
            if (amount > 0d)
                GameFacts.Record(Unit, state =>
                    OptionalTransitionRecording.RecordReal(state, "plant_fuel", amount));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
