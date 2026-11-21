using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Stage 2 metric for Empire Refactored (Matathias.Empire, 1.7/Assemblies/Empire.dll):
/// Counts vassal tax silver and tithe goods market value actually delivered onto a player
/// home map via FactionColonies.util.DeliveryEvent.Action(FCEvent, bool).
/// Delayed/rescheduled events (DeliveryEvent.CreateDeliveryEvent) and destroyed items
/// when no tax map exists are excluded so ledger numbers and retried deliveries are never
/// counted prematurely or twice.
/// </summary>
internal static class EmpireTaxStage2Outcomes
{
    private const string AssemblyName = "Empire";
    private const string TaxDeliveredUnit = "mod.matathias.empire.tax_value_delivered";

    private static FieldInfo eventDefField = null!;
    private static FieldInfo eventGoodsField = null!;
    private static FieldInfo eventIsDelayedField = null!;
    private static FieldInfo eventCustomDescField = null!;

    [ThreadStatic] private static TaxDeliveryScope? activeScope;

    private sealed record GoodsSnapshot(Thing Item, ThingDef Def, int StackCount, float UnitMarketValue);

    private sealed class TaxDeliveryScope
    {
        internal required List<GoodsSnapshot> Goods { get; init; }
        internal required TaxDeliveryScope? Parent { get; init; }
        internal bool Rescheduled;
    }

    internal static ModMetricAdapterRegistration Build() =>
        OutcomeMetric.Count(
            EmpireTaxStage2Rules.OwnerPackageId,
            "tax_value_delivered",
            "RWS_EmpireTaxDelivered",
            "RWS_EmpireTaxDeliveredDesc",
            MetricCategory.Economy,
            Install,
            breakdown: true,
            unit: MetricUnit.Silver);

    private static void Install(Harmony harmony)
    {
        var deliveryType = OutcomeMetric.Bind("FactionColonies.util.DeliveryEvent", AssemblyName);
        var fcEventType = OutcomeMetric.Bind("FactionColonies.FCEvent", AssemblyName);
        var fcEventDefType = OutcomeMetric.Bind("FactionColonies.FCEventDef", AssemblyName);
        if (!typeof(Def).IsAssignableFrom(fcEventDefType))
            throw new InvalidOperationException("Empire FCEventDef hierarchy changed");

        eventDefField = RequireField(fcEventType, "def", fcEventDefType);
        eventGoodsField = RequireField(fcEventType, "goods", typeof(List<Thing>));
        eventIsDelayedField = RequireField(fcEventType, "isDelayed", typeof(bool));
        eventCustomDescField = RequireField(fcEventType, "customDescription", typeof(string));

        var actionMethod = OutcomeMetric.Method(deliveryType, "Action", typeof(void), fcEventType, typeof(bool));
        var rescheduleMethod = OutcomeMetric.Method(deliveryType, "CreateDeliveryEvent", typeof(void), fcEventType);

        var patcher = new ScopedHarmonyPatcher(harmony);
        patcher.Patch(actionMethod, typeof(EmpireTaxStage2Outcomes),
            prefix: nameof(BeforeDeliveryAction), postfix: nameof(AfterDeliveryAction), finalizer: nameof(FinishDeliveryAction));
        patcher.Patch(rescheduleMethod, typeof(EmpireTaxStage2Outcomes), postfix: nameof(OnRescheduled));
    }

    private static void BeforeDeliveryAction(object? __0, out TaxDeliveryScope? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(TaxDeliveredUnit) || __0 is null)
            return;

        try
        {
            var eventDef = eventDefField.GetValue(__0) as Def;
            if (eventIsDelayedField.GetValue(__0) is not bool isDelayed)
                return;

            var customDesc = eventCustomDescField.GetValue(__0) as string;
            if (!EmpireTaxStage2Rules.IsEligibleTaxDeliveryEvent(eventDef?.defName, isDelayed, customDesc))
                return;

            if (eventGoodsField.GetValue(__0) is not List<Thing> { Count: > 0 } goods)
                return;

            var snapshots = new List<GoodsSnapshot>(goods.Count);
            foreach (var thing in goods)
            {
                if (thing is not { Destroyed: false, stackCount: > 0, def: { } def } ||
                    thing.Spawned || thing.holdingOwner is not null)
                    continue;

                var unitValue = def == ThingDefOf.Silver ? 1f : thing.MarketValue;
                snapshots.Add(new GoodsSnapshot(thing, def, thing.stackCount, unitValue));
            }

            if (snapshots.Count == 0)
                return;

            __state = new TaxDeliveryScope { Goods = snapshots, Parent = activeScope };
            activeScope = __state;
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(TaxDeliveredUnit, error);
        }
    }

    private static void OnRescheduled(bool __runOriginal)
    {
        if (__runOriginal && activeScope is { } scope)
            scope.Rescheduled = true;
    }

    private static void AfterDeliveryAction(bool __runOriginal, TaxDeliveryScope? __state)
    {
        if (!__runOriginal || __state is null || !ReferenceEquals(activeScope, __state) ||
            __state.Rescheduled || !OutcomeMetric.Observing(TaxDeliveredUnit))
            return;

        try
        {
            var byDef = new Dictionary<ThingDef, long>();
            foreach (var item in __state.Goods)
            {
                var value = EmpireTaxStage2Rules.ComputeDeliveredItemSilverValue(
                    wasRescheduled: __state.Rescheduled,
                    isDestroyed: item.Item.Destroyed,
                    isSpawnedOnPlayerHome: item.Item.Spawned && item.Item.Map is { IsPlayerHome: true },
                    hasHoldingOwner: item.Item.holdingOwner is not null,
                    isSilverDef: item.Def == ThingDefOf.Silver,
                    stackCount: item.StackCount,
                    unitMarketValue: item.UnitMarketValue);

                if (value > 0)
                    byDef[item.Def] = checked(byDef.GetValueOrDefault(item.Def) + value);
            }

            if (byDef.Count == 0)
                return;

            GameFacts.Record(TaxDeliveredUnit, state =>
            {
                foreach (var (def, value) in byDef)
                {
                    var key = OptionalTransitionRecording.QualifiedDefKey(def.modContentPack?.PackageId, def.defName);
                    OptionalTransitionRecording.RecordCount(state, "completed", value, key: key, withBreakdown: true);
                }
            });
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(TaxDeliveredUnit, error);
        }
    }

    private static Exception? FinishDeliveryAction(Exception? __exception, TaxDeliveryScope? __state)
    {
        if (__state is not null && ReferenceEquals(activeScope, __state))
            activeScope = __state.Parent;
        return __exception;
    }

    private static FieldInfo RequireField(Type owner, string name, Type expectedType)
        => AccessTools.DeclaredField(owner, name) is { } field && field.FieldType == expectedType
            ? field
            : throw new InvalidOperationException($"Empire {owner.Name}.{name} signature changed");
}
