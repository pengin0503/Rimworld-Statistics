using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.Gastronomy;

/// <summary>
/// Actual Gastronomy meal silver transferred to a player restaurant. Two
/// synchronous owner scopes guarantee that RestaurantDebt.PayDebt's integer
/// argument is the amount returned by the single successful silver transfer,
/// not a snapshot of cash, an unpaid order or Hospitality shop income.
/// </summary>
internal static class GastronomyPaymentStatistics
{
    private const string Owner = "Orion.Gastronomy";
    private const string CashRegister = "Orion.CashRegister";
    private const string Unit = "mod.orion.gastronomy.mealpayment";
    private const string Key = Unit + ".silver_collected";
    private static FieldInfo? driverPawnField;
    [ThreadStatic] private static DineScope? activeDine;
    [ThreadStatic] private static PaymentScope? activePayment;

    private sealed class DineScope
    {
        internal required Pawn Patron { get; init; }
        internal required ThingOwner Recipient { get; init; }
        internal required DineScope? Parent { get; init; }
    }

    private sealed class PaymentScope
    {
        internal required Pawn Patron { get; init; }
        internal required PaymentScope? Parent { get; init; }
        internal int AmountPaid;
    }

    private static readonly LiveFamily Family = new()
    {
        Name = Unit,
        Revision = 1,
        Build = output => OptionalTransitionRecording.PutCount(output, "silver", Key),
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => [new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.mealpayment",
            OwnerPackageId = Owner,
            FamilyId = "mealpayment",
            MetricName = "silver_collected",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Category = MetricCategory.Economy,
            Unit = MetricUnit.Silver,
            Overall = OverallAggregation.Sum,
            Selection = ColonySelection.Representative,
            HasBreakdown = false,
            RequiredPackages = [new ModPackageRequirement(Owner), new ModPackageRequirement(CashRegister)],
            LabelKey = "RWS_Gastronomy_SilverCollected",
            DescriptionKey = "RWS_Gastronomy_SilverCollectedDesc",
        }, Family, Install)];

    private static void Install(Harmony harmony)
    {
        var dining = AccessTools.TypeByName("Gastronomy.Dining.JobDriver_Dine");
        var utility = AccessTools.TypeByName("Gastronomy.Dining.DiningUtility");
        var debt = AccessTools.TypeByName("Gastronomy.Restaurant.RestaurantDebt");
        if (dining is null || utility is null || debt is null ||
            !string.Equals(dining.Assembly.GetName().Name, "Gastronomy",
                StringComparison.OrdinalIgnoreCase) ||
            !ReferenceEquals(dining.Assembly, utility.Assembly) ||
            !ReferenceEquals(dining.Assembly, debt.Assembly) ||
            !typeof(JobDriver).IsAssignableFrom(dining))
            throw new InvalidOperationException("Gastronomy 1.7 payment assembly is absent or changed");

        var onFood = AccessTools.DeclaredMethod(dining, "OnTransferredFood",
            [typeof(Thing), typeof(ThingOwner), typeof(Thing).MakeByRefType()]);
        var pay = AccessTools.DeclaredMethod(utility, "PayForMeal",
            [typeof(Pawn), typeof(ThingOwner), typeof(Thing).MakeByRefType()]);
        var payDebt = AccessTools.DeclaredMethod(debt, "PayDebt", [typeof(Pawn), typeof(int)]);
        var pawn = AccessTools.Field(typeof(JobDriver), "pawn");
        if (onFood?.ReturnType != typeof(void) || pay?.ReturnType != typeof(void) ||
            payDebt?.ReturnType != typeof(void) || pawn?.FieldType != typeof(Pawn))
            throw new InvalidOperationException("Gastronomy 1.7 payment signatures do not match");

        driverPawnField = pawn;
        try
        {
            harmony.Patch(onFood,
                prefix: new HarmonyMethod(typeof(GastronomyPaymentStatistics), nameof(BeforeDineTransfer)),
                finalizer: new HarmonyMethod(typeof(GastronomyPaymentStatistics), nameof(FinishDineTransfer)));
            harmony.Patch(pay,
                prefix: new HarmonyMethod(typeof(GastronomyPaymentStatistics), nameof(BeforePayment)),
                postfix: new HarmonyMethod(typeof(GastronomyPaymentStatistics), nameof(AfterPayment)),
                finalizer: new HarmonyMethod(typeof(GastronomyPaymentStatistics), nameof(FinishPayment)));
            harmony.Patch(payDebt,
                prefix: new HarmonyMethod(typeof(GastronomyPaymentStatistics), nameof(BeforeDebtPayment)));
        }
        catch
        {
            harmony.Unpatch(onFood, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(pay, HarmonyPatchType.All, harmony.Id);
            harmony.Unpatch(payDebt, HarmonyPatchType.All, harmony.Id);
            driverPawnField = null;
            throw;
        }
    }

    private static bool CanObserve()
        => MetricGates.IsRecording(Unit)
           && Current.ProgramState == ProgramState.Playing
           && Scribe.mode == LoadSaveMode.Inactive
           && Find.TickManager is not null;

    private static void BeforeDineTransfer(object __instance, ThingOwner __1, out DineScope? __state)
    {
        __state = null;
        if (!CanObserve() || __1 is null)
            return;
        try
        {
            var patron = driverPawnField?.GetValue(__instance) as Pawn;
            if (patron is null || patron.Faction == Faction.OfPlayer ||
                patron.MapHeld?.IsPlayerHome != true || patron.inventory is null)
                return;
            __state = new DineScope { Patron = patron, Recipient = __1, Parent = activeDine };
            activeDine = __state;
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishDineTransfer(Exception? __exception, DineScope? __state)
    {
        if (__state is not null && ReferenceEquals(__state, activeDine))
            activeDine = __state.Parent;
        return __exception;
    }

    private static void BeforePayment(Pawn __0, ThingOwner __1, out PaymentScope? __state)
    {
        __state = null;
        if (activeDine is not { } dine || !CanObserve() ||
            !ReferenceEquals(__0, dine.Patron) || !ReferenceEquals(__1, dine.Recipient))
            return;
        __state = new PaymentScope { Patron = __0, Parent = activePayment };
        activePayment = __state;
    }

    private static void BeforeDebtPayment(Pawn __0, int __1)
    {
        if (activePayment is not { } scope ||
            !ReferenceEquals(scope.Patron, __0) || __1 <= 0)
            return;
        // The pinned PayForMeal implementation passes precisely the successful
        // transfer's integer return value into PayDebt. This is not the debt owed.
        try { scope.AmountPaid = checked(scope.AmountPaid + __1); }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static void AfterPayment(bool __runOriginal, PaymentScope? __state)
    {
        if (!__runOriginal || __state is null || !ReferenceEquals(activePayment, __state)
            || !CanObserve() || __state.AmountPaid <= 0)
            return;
        try
        {
            var actual = GastronomyPaymentRules.Collected(int.MaxValue,
                __state.AmountPaid, playerRestaurant: true);
            if (actual > 0)
                GameFacts.Record(Unit, s => OptionalTransitionRecording.RecordCount(
                    s, "silver", actual));
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }

    private static Exception? FinishPayment(Exception? __exception, PaymentScope? __state)
    {
        if (__state is not null && ReferenceEquals(activePayment, __state))
            activePayment = __state.Parent;
        return __exception;
    }
}
