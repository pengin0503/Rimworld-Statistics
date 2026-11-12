using System.Collections;
using System.Reflection;
using HarmonyLib;
using RimworldStatistics.Api;
using RimworldStatistics.Metrics;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace RimworldStatistics.VanillaTradingExpandedAddon;

/// <summary>
/// VTE NPC buy-order deliveries. Scheduling a caravan is NOT contract fulfillment.
/// Only items actually consumed immediately or carried away by a departing NPC
/// caravan are counted. Bank payout has a separate confirmation step and is not
/// claimed by these metrics.
/// </summary>
internal static class VteContractStatistics
{
    private const string Unit = "mod.vanillaexpanded.vanillatradingexpanded.contract";
    private const string Fulfilled = Unit + ".deliveries";
    private const string Items = Unit + ".items_delivered";

    private static FieldInfo? pendingField;
    private static FieldInfo? scheduledField;
    private static FieldInfo? itemField;
    private static FieldInfo? stuffField;
    private static FieldInfo? amountField;
    private static FieldInfo? rewardField;
    private static FieldInfo? expiryField;
    private static FieldInfo? mapField;
    private static FieldInfo? lordContractField;
    private static FieldInfo? lordField;
    private static FieldInfo? caravanSentField;
    private static MethodInfo? foundItems;

    private sealed record SelectedStack(Thing Thing, int BeforeCount);
    private sealed record ContractBefore(string Fingerprint, int Tick, int Requested,
        object Contract, IReadOnlyList<SelectedStack> Stacks, int ItemCount);
    private sealed record CaravanBefore(string Fingerprint, int Tick, int Requested, int Reward, int ItemCount);

    internal static void TryRegister()
    {
        if (!StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
            return;
        var contractType = AccessTools.TypeByName("VanillaTradingExpanded.Contract");
        var managerType = AccessTools.TypeByName("VanillaTradingExpanded.TradingManager");
        var lordType = AccessTools.TypeByName("VanillaTradingExpanded.LordJob_GrabItemsAndLeave");
        if (contractType is null || managerType is null || lordType is null ||
            !typeof(LordJob).IsAssignableFrom(lordType))
            return;
        var complete = AccessTools.DeclaredMethod(managerType, "CompleteContract", [contractType]);
        var exit = AccessTools.DeclaredMethod(lordType, "ExitCaravan", Type.EmptyTypes);
        pendingField = AccessTools.Field(managerType, "npcSubmittedContracts");
        scheduledField = AccessTools.Field(managerType, "npcContractsToBeCompleted");
        itemField = AccessTools.Field(contractType, "item");
        stuffField = AccessTools.Field(contractType, "stuff");
        amountField = AccessTools.Field(contractType, "amount");
        rewardField = AccessTools.Field(contractType, "reward");
        expiryField = AccessTools.Field(contractType, "expiresInTicks");
        mapField = AccessTools.Field(contractType, "mapToTakeItems");
        foundItems = AccessTools.DeclaredMethod(contractType, "FoundItemsInMap", [typeof(Map)]);
        lordContractField = AccessTools.Field(lordType, "contract");
        lordField = AccessTools.Field(lordType, "lord");
        caravanSentField = AccessTools.Field(lordType, "caravanSent");

        if (complete is null || exit is null || complete.ReturnType != typeof(void) ||
            exit.ReturnType != typeof(void) || pendingField is null || scheduledField is null ||
            itemField?.FieldType != typeof(ThingDef) || stuffField?.FieldType != typeof(ThingDef) ||
            amountField?.FieldType != typeof(int) || rewardField?.FieldType != typeof(int) ||
            expiryField?.FieldType != typeof(int) || mapField?.FieldType != typeof(Map) ||
            foundItems is null || !typeof(IEnumerable).IsAssignableFrom(foundItems.ReturnType) ||
            lordContractField?.FieldType != contractType || lordField is null ||
            caravanSentField?.FieldType != typeof(bool))
            return;

        if (host.Register(Def("deliveries", ApiEventMode.Count, "RWS_VTE_ContractDeliveries",
                "RWS_VTE_ContractDeliveriesDesc")) != ApiRegistrationResult.Accepted ||
            host.Register(Def("items_delivered", ApiEventMode.Sum, "RWS_VTE_ContractItems",
                "RWS_VTE_ContractItemsDesc")) != ApiRegistrationResult.Accepted)
            return;

        ExternalSdkHost.Instance.IntegratedHooks.Defer(Unit, () =>
        {
            var harmony = new Harmony("pengin0503.rws.vte.contract");
            try
            {
                harmony.Patch(complete,
                    prefix: new HarmonyMethod(typeof(VteContractStatistics), nameof(BeforeComplete)),
                    postfix: new HarmonyMethod(typeof(VteContractStatistics), nameof(AfterComplete)));
                harmony.Patch(exit,
                    prefix: new HarmonyMethod(typeof(VteContractStatistics), nameof(BeforeExit)),
                    postfix: new HarmonyMethod(typeof(VteContractStatistics), nameof(AfterExit)));
                return true;
            }
            catch
            {
                harmony.Unpatch(complete, HarmonyPatchType.All, harmony.Id);
                harmony.Unpatch(exit, HarmonyPatchType.All, harmony.Id);
                return false;
            }
        });
    }

    private static ApiMetricDefinition Def(string name, ApiEventMode mode, string label, string description) => new()
    {
        ProviderId = "rws.vte.contract",
        OwnerPackageId = "VanillaExpanded.VanillaTradingExpanded",
        FamilyId = "contract",
        MetricName = name,
        ProviderVersion = "1.0.0",
        Revision = 1,
        Kind = ApiMetricKind.Event,
        EventMode = mode,
        Category = "Economy",
        Unit = "Count",
        Overall = "Sum",
        Selection = "Representative",
        LabelKey = label,
        DescriptionKey = description,
    };

    private static bool CanRecord() =>
        MetricGates.IsRecording(Unit) && Current.ProgramState == ProgramState.Playing &&
        Scribe.mode == LoadSaveMode.Inactive && Find.TickManager is not null;

    private static int ReadInt(object instance, FieldInfo? field) => (int)(field?.GetValue(instance) ?? 0);

    private static string Fingerprint(object contract, Map? map)
    {
        var item = itemField?.GetValue(contract) as ThingDef;
        var stuff = stuffField?.GetValue(contract) as ThingDef;
        return string.Concat(item?.defName ?? "?", ":", stuff?.defName ?? "none", ":",
            ReadInt(contract, amountField), ":", ReadInt(contract, rewardField), ":",
            ReadInt(contract, expiryField), ":", map?.Tile ?? -1);
    }

    private static List<SelectedStack> SelectedItems(object contract, Map map)
    {
        var selected = new List<SelectedStack>();
        if (foundItems?.Invoke(contract, [map]) is not IEnumerable found)
            return selected;
        var item = itemField?.GetValue(contract) as ThingDef;
        var stuff = stuffField?.GetValue(contract) as ThingDef;
        foreach (var thing in found)
            if (thing is Thing t && !t.Destroyed && t.def == item && t.Stuff == stuff)
                selected.Add(new SelectedStack(t, t.stackCount));
        return selected;
    }

    private static void BeforeComplete(object __instance, object __0, out ContractBefore? __state)
    {
        __state = null;
        if (!CanRecord())
            return;
        try
        {
            if ((pendingField?.GetValue(__instance) as IList)?.Contains(__0) != true)
                return;
            var map = mapField?.GetValue(__0) as Map ?? Find.AnyPlayerHomeMap;
            if (map is null)
                return;
            var requested = ReadInt(__0, amountField);
            var reward = ReadInt(__0, rewardField);
            if (requested <= 0 || reward <= 0)
                return;
            var stacks = SelectedItems(__0, map);
            long counted = 0;
            foreach (var stack in stacks)
                counted += stack.BeforeCount;
            __state = new ContractBefore(Fingerprint(__0, map), Find.TickManager.TicksGame,
                requested, __0, stacks, (int)Math.Min(int.MaxValue, counted));
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void AfterComplete(object __instance, bool __runOriginal, ContractBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanRecord())
            return;
        try
        {
            // Caravan mode only schedules pickup. Count it upon ExitCaravan instead.
            if ((pendingField?.GetValue(__instance) as IList)?.Contains(__state.Contract) == true ||
                (scheduledField?.GetValue(__instance) as IList)?.Contains(__state.Contract) == true)
                return;
            // The original FoundItemsInMap stops as soon as enough stacks were
            // found. Calling it again would include untouched replacement stacks,
            // masking a completed transfer when the map has excess stock.
            var remaining = new (int Before, int After)[__state.Stacks.Count];
            for (var i = 0; i < __state.Stacks.Count; i++)
            {
                var stack = __state.Stacks[i];
                remaining[i] = (stack.BeforeCount, stack.Thing.Destroyed ? 0 : stack.Thing.stackCount);
            }
            var delivered = VteTradeMath.ConsumedSelected(remaining, __state.Requested);
            if (delivered > 0)
                Record(__state.Fingerprint, "instant", __state.Tick,
                    __state.ItemCount, __state.ItemCount - delivered, delivered);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void BeforeExit(object __instance, out CaravanBefore? __state)
    {
        __state = null;
        if (!CanRecord())
            return;
        try
        {
            if (caravanSentField?.GetValue(__instance) is true ||
                lordContractField?.GetValue(__instance) is not { } contract ||
                lordField?.GetValue(__instance) is not Lord lord)
                return;
            int requested = ReadInt(contract, amountField);
            int reward = ReadInt(contract, rewardField);
            if (requested <= 0 || reward <= 0)
                return;
            var item = itemField?.GetValue(contract) as ThingDef;
            var stuff = stuffField?.GetValue(contract) as ThingDef;
            long collected = 0;
            foreach (var pawn in lord.ownedPawns)
                if (pawn.inventory is { } inventory)
                    foreach (var thing in inventory.innerContainer)
                        if (thing.def == item && thing.Stuff == stuff)
                            collected += thing.stackCount;
            int amount = (int)Math.Clamp(collected, 0, int.MaxValue);
            if (amount <= 0 || VteTradeMath.ProportionalReward(reward, requested, amount) <= 0)
                return;
            __state = new CaravanBefore(Fingerprint(contract, lord.Map), Find.TickManager.TicksGame,
                requested, reward, amount);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void AfterExit(object __instance, bool __runOriginal, CaravanBefore? __state)
    {
        if (!__runOriginal || __state is null || !CanRecord())
            return;
        try
        {
            if (caravanSentField?.GetValue(__instance) is not true)
                return;
            int delivered = Math.Min(__state.ItemCount, __state.Requested);
            if (delivered > 0)
                Record(__state.Fingerprint, "caravan", __state.Tick, 0, delivered, delivered);
        }
        catch (Exception e) { MetricGates.Fail(Unit, e); }
    }

    private static void Record(string fingerprint, string action, int tick, int before, int after, int delivered)
    {
        if (!StatisticsApi.TryGetHost(1, 1, out var host) || host is null)
            return;
        var id = VteTradeMath.EventId(fingerprint, action, tick, before, after);
        _ = host.RecordEvent(Fulfilled, id);
        _ = host.RecordEvent(Items, id, delivered);
    }
}
