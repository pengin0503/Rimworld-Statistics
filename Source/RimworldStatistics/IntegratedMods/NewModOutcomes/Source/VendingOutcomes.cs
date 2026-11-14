using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.NewModOutcomes;

internal static class VendingOutcomes
{
    private const string Owner = "Adamas.VendingMachines";
    private const string Assembly = "VendingMachines";
    private const string Items = "mod.adamas.vendingmachines.itemsdispensed";
    private const string Silver = "mod.adamas.vendingmachines.vendingsilver";
    private static MethodInfo receivePayment = null!;
    private static FieldInfo silverContainer = null!;
    private sealed record Payment(object Machine, ThingDef Def, long Before);

    internal static ModMetricAdapterRegistration ItemsDispensed() =>
        OutcomeMetric.Count(Owner, "itemsdispensed", "RWS_NewMod_VendingItems", "RWS_NewMod_VendingItemsDesc",
            MetricCategory.Economy, InstallItems, breakdown: true, dependencies: ["Orion.Hospitality"]);

    internal static ModMetricAdapterRegistration SilverReceived() =>
        OutcomeMetric.Count(Owner, "vendingsilver", "RWS_NewMod_VendingSilver", "RWS_NewMod_VendingSilverDesc",
            MetricCategory.Economy, InstallSilver, breakdown: true, unit: MetricUnit.Silver,
            dependencies: ["Orion.Hospitality"]);

    private static Type MachineType() => OutcomeMetric.Bind("VendingMachines.CompVendingMachine", Assembly);

    private static MethodInfo PaymentMethod(Type machine) => OutcomeMetric.Method(machine, "ReceivePayment",
        typeof(void), typeof(ThingOwner<Thing>), typeof(Thing), typeof(Thing), typeof(int));

    private static void InstallItems(Harmony harmony)
    {
        receivePayment = PaymentMethod(MachineType());
        var driver = OutcomeMetric.Bind("VendingMachines.JobDriver_BuyItem", Assembly);
        OutcomeMetric.Transpile(harmony, OutcomeMetric.Method(driver, "BuyThing", typeof(void), typeof(Toil)),
            typeof(VendingOutcomes), nameof(ObserveDelivery));
    }

    private static IEnumerable<CodeInstruction> ObserveDelivery(IEnumerable<CodeInstruction> instructions)
    {
        var body = instructions.ToList();
        int payment = body.IndexOf(body.Single(code => code.Calls(receivePayment)));
        // The guard uses the accepted quantity. The payment call instead receives the requested quantity.
        int guard = body.FindLastIndex(payment - 1, code => code.opcode == OpCodes.Ble || code.opcode == OpCodes.Ble_S);
        if (guard < 2 || payment - guard != 9 || !body[guard - 2].IsLdloc() ||
            body[guard - 1].opcode != OpCodes.Ldc_I4_0 ||
            body[guard].operand is not Label skip || !body.Skip(payment + 1).Any(code => code.labels.Contains(skip)) ||
            !body[payment - 2].IsLdloc())
            throw new InvalidOperationException("Vending delivery confirmation changed");
        body.InsertRange(payment + 1, [
            new CodeInstruction(OpCodes.Ldarg_1),
            new CodeInstruction(body[payment - 2].opcode, body[payment - 2].operand),
            new CodeInstruction(body[guard - 2].opcode, body[guard - 2].operand),
            new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(VendingOutcomes), nameof(RecordItems)))
        ]);
        return body;
    }

    private static bool GuestAtMachine(Pawn? buyer, Thing? machine) =>
        buyer is { Spawned: true, Faction.IsPlayer: false } && machine is { Spawned: true } &&
        buyer.Map == machine.Map && GameFacts.IsPlayer(machine);

    private static void RecordItems(Toil toil, Thing product, int accepted)
    {
        if (!OutcomeMetric.Observing(Items))
            return;
        try
        {
            var machine = toil.actor.jobs.curJob?.GetTarget(TargetIndex.B).Thing;
            if (accepted > 0 && GuestAtMachine(toil.actor, machine))
                OutcomeMetric.Record(Items, product.def, breakdown: true, amount: accepted);
        }
        catch (Exception error) { LiveMetrics.Fail(Items, error); }
    }

    private static void InstallSilver(Harmony harmony)
    {
        var machine = MachineType();
        silverContainer = AccessTools.DeclaredField(machine, "silverContainer");
        if (silverContainer is null || !typeof(ThingOwner).IsAssignableFrom(silverContainer.FieldType))
            throw new InvalidOperationException("Vending silver container changed");
        OutcomeMetric.Patch(harmony, PaymentMethod(machine), typeof(VendingOutcomes),
            nameof(BeforePayment), nameof(AfterPayment));
    }

    private static long SilverIn(object machine) =>
        silverContainer.GetValue(machine) is ThingOwner cash ? ServiceOutcomes.CountSilver(cash) : 0;

    private static void BeforePayment(ThingComp __instance, ThingOwner<Thing> __0, Thing? __1, out Payment? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Silver) || __1?.def != ThingDefOf.Silver)
            return;
        try
        {
            if (__0.Owner is Pawn_InventoryTracker { pawn: { } buyer } && GuestAtMachine(buyer, __instance.parent))
                __state = new Payment(__instance, __instance.parent.def, SilverIn(__instance));
        }
        catch (Exception error) { LiveMetrics.Fail(Silver, error); }
    }

    private static void AfterPayment(bool __runOriginal, Payment? __state)
    {
        if (!__runOriginal || __state is not { } payment)
            return;
        try
        {
            long gained = SilverIn(payment.Machine) - payment.Before;
            if (gained > 0)
                OutcomeMetric.Record(Silver, payment.Def, breakdown: true, amount: gained);
        }
        catch (Exception error) { LiveMetrics.Fail(Silver, error); }
    }
}
