using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.VfePower;

/// <summary>
/// Vanilla Furniture Expanded - Power: counts committed nuclear reactor
/// meltdowns by observing CompPowerPlantNuclear.CompTick transitions where
/// signalMeltdown flips from false to true and CompBreakdownable is BrokenDown.
/// </summary>
internal static class VfePowerStatistics
{
    private const string Owner = "VanillaExpanded.VFEPower";
    private const string Unit = "mod.vanillaexpanded.vfepower.nuclear_meltdowns";

    private static Func<object, bool>? getSignalMeltdown;
    private static Func<object, CompBreakdownable?>? getBreakdownableComp;

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            Owner,
            "nuclear_meltdowns",
            "RWS_VfePower_NuclearMeltdowns",
            "RWS_VfePower_NuclearMeltdownsDesc",
            MetricCategory.Facilities,
            Install,
            breakdown: true),
    ];

    private static void Install(Harmony harmony)
    {
        var compType = OutcomeMetric.Bind("VanillaPowerExpanded.CompPowerPlantNuclear", "VanillaPowerExpanded");
        if (!typeof(CompPowerTrader).IsAssignableFrom(compType))
            throw new InvalidOperationException("CompPowerPlantNuclear base type changed");

        var tick = OutcomeMetric.Method(compType, "CompTick", typeof(void));
        var signalField = AccessTools.DeclaredField(compType, "signalMeltdown");
        var breakdownField = AccessTools.DeclaredField(compType, "breakdownableComp");
        if (signalField?.FieldType != typeof(bool) ||
            breakdownField is null || !typeof(CompBreakdownable).IsAssignableFrom(breakdownField.FieldType))
            throw new InvalidOperationException("CompPowerPlantNuclear meltdown fields changed");

        getSignalMeltdown = OptionalTransitionCallSite.CompileFieldGetter<bool>(signalField);
        getBreakdownableComp = OptionalTransitionCallSite.CompileFieldGetter<CompBreakdownable?>(breakdownField);
        try
        {
            OutcomeMetric.Patch(harmony, tick, typeof(VfePowerStatistics),
                prefix: nameof(BeforeTick), postfix: nameof(AfterTick));
        }
        catch
        {
            getSignalMeltdown = null;
            getBreakdownableComp = null;
            throw;
        }
    }

    private readonly record struct MeltdownProbe(bool Active, Building Building);

    private static void BeforeTick(object __instance, out MeltdownProbe __state)
    {
        __state = default;
        if (!OutcomeMetric.Observing(Unit) ||
            __instance is not ThingComp { parent: Building { Spawned: true } building } ||
            !GameFacts.IsPlayerFaction(building.Faction) ||
            !string.Equals(building.def?.modContentPack?.PackageId, Owner, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            if (getSignalMeltdown is null || getSignalMeltdown(__instance))
                return;
            __state = new MeltdownProbe(Active: true, Building: building);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterTick(object __instance, bool __runOriginal, MeltdownProbe __state)
    {
        if (!__state.Active || !__runOriginal || !OutcomeMetric.Observing(Unit))
            return;

        try
        {
            var signaledNow = getSignalMeltdown is not null && getSignalMeltdown(__instance);
            var breakdownable = getBreakdownableComp?.Invoke(__instance);
            var building = __state.Building;
            if (!VfePowerRules.MeltdownCommitted(
                    wasSignaled: false,
                    isSignaled: signaledNow,
                    isBrokenDown: breakdownable?.BrokenDown == true,
                    playerOwned: GameFacts.IsPlayerFaction(building.Faction),
                    spawned: building.Spawned))
                return;

            OutcomeMetric.Record(Unit, building.def, breakdown: true);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
