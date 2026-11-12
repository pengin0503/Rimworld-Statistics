using HarmonyLib;
using RimWorld;
using RimworldStatistics.Api;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.HospitalityAddon;

internal static class HospitalityPurchases
{
    private const string Unit = "mod.orion.hospitality.trade";
    private const string UnitsId = Unit + ".guest_purchases";
    private const string SilverId = Unit + ".guest_purchase_silver";
    private static bool unitsConnected;
    private static bool silverConnected;

    internal static void TryRegister()
    {
        if (!StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
            return;
        var driver = AccessTools.TypeByName("Hospitality.JobDriver_BuyItem");
        if (driver is null || !typeof(JobDriver).IsAssignableFrom(driver))
            return;
        var target = AccessTools.DeclaredMethod(driver, "BuyThing", [typeof(Toil)]);
        if (target is null || target.IsStatic || target.ReturnType != typeof(void))
            return;

        unitsConnected = host.Register(Metric("guest_purchases", "Count", "RWS_Hospitality_ShoppingUnits", "RWS_Hospitality_ShoppingUnitsDesc"))
            == ApiRegistrationResult.Accepted;
        silverConnected = host.Register(Metric("guest_purchase_silver", "Silver", "RWS_Hospitality_ShoppingSilver", "RWS_Hospitality_ShoppingSilverDesc"))
            == ApiRegistrationResult.Accepted;
        if (unitsConnected || silverConnected)
            ExternalSdkHost.Instance.IntegratedHooks.Defer(Unit, () =>
            {
                new Harmony("pengin0503.rws.hospitality.trade").Patch(target,
                    prefix: new HarmonyMethod(typeof(HospitalityPurchases), nameof(Before)),
                    postfix: new HarmonyMethod(typeof(HospitalityPurchases), nameof(After)));
                return true;
            });
    }

    private static ApiMetricDefinition Metric(string name, string unit, string label, string description) => new()
    {
        OwnerPackageId = "Orion.Hospitality",
        ProviderId = "rws.hospitality.trade",
        ProviderVersion = "1.0.0",
        FamilyId = "trade",
        MetricName = name,
        Revision = 1,
        Kind = ApiMetricKind.Event,
        EventMode = ApiEventMode.Sum,
        Category = "Economy",
        Unit = unit,
        Overall = "Sum",
        Selection = "Representative",
        LabelKey = label,
        DescriptionKey = description,
        MinimumOwnerVersion = "1.1.7",
        MaximumOwnerVersionExclusive = "1.1.9",
    };

    private sealed record Capture(Pawn Guest, ThingDef Item, string EventId, Dictionary<int, int> Items, long Silver);

    private static void Before(JobDriver __instance, Toil __0, out Capture? __state)
    {
        __state = null;
        if (!MetricGates.IsRecording(Unit) || Current.ProgramState != ProgramState.Playing ||
            Scribe.mode != LoadSaveMode.Inactive)
            return;
        try
        {
            var guest = __0.actor;
            if (guest is null || guest != __instance.pawn || guest.Faction == Faction.OfPlayer ||
                guest.MapHeld is not { IsPlayerHome: true } || guest.CurJob is not { } job ||
                job.targetA.Thing?.def is not { } item || item == ThingDefOf.Silver ||
                HospitalityPurchaseMath.EventId(job.loadID, job.startTick) is not { } id)
                return;
            var (items, silver) = Holdings(guest, item);
            __state = new Capture(guest, item, id, items, silver);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void After(bool __runOriginal, Capture? __state)
    {
        if (!__runOriginal || __state is not { } before)
            return;
        try
        {
            var (items, silver) = Holdings(before.Guest, before.Item);
            var (bought, paid) = HospitalityPurchaseMath.Completed(before.Items, items, before.Silver, silver, true);
            if (bought == 0 || !StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
                return;
            if (unitsConnected) _ = host.RecordEvent(UnitsId, before.EventId, bought);
            if (silverConnected && paid > 0) _ = host.RecordEvent(SilverId, before.EventId, paid);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static (Dictionary<int, int> Items, long Silver) Holdings(Pawn guest, ThingDef item)
    {
        var items = new Dictionary<int, int>();
        long silver = 0;
        if (guest.inventory is { } inventory)
            foreach (var thing in inventory.innerContainer)
            {
                if (thing.def == ThingDefOf.Silver) silver += thing.stackCount;
                Add(thing);
            }
        if (guest.apparel is { } apparel)
            foreach (var thing in apparel.WornApparel)
                Add(thing);
        if (guest.equipment is { } equipment)
            foreach (var thing in equipment.AllEquipmentListForReading)
                Add(thing);
        return (items, silver);

        void Add(Thing thing)
        {
            if (thing.def == item) items[thing.thingIDNumber] = thing.stackCount;
        }
    }
}
