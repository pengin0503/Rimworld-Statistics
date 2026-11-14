using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.NewModOutcomes;

internal static class ServiceOutcomes
{
    private const string Spa = "mod.adamas.hospitalityspa.guestsilver";
    private const string Store = "mod.adamas.storefront.sales";
    private const string Museum = "mod.biomesteam.biomesfossils.museumvisits";
    private static PropertyInfo spaContainer = null!;
    private static Type spaComp = null!;
    private static Type museumLearning = null!;
    private static Type museumVisit = null!;
    private sealed record Sale(Pawn Buyer, ThingDef Product, ThingDef? Stuff, long Before);
    private sealed record Payment(ThingOwner Cash, Def Facility, long Before);

    internal static ModMetricAdapterRegistration SpaPayments() =>
        OutcomeMetric.Count("Adamas.HospitalitySpa", "guestsilver", "RWS_NewMod_Spa", "RWS_NewMod_SpaDesc",
            MetricCategory.Economy,
            InstallSpa, breakdown: true, unit: MetricUnit.Silver, dependencies: ["Orion.Hospitality", "Dubwise.DubsBadHygiene"]);

    internal static ModMetricAdapterRegistration StorefrontSales() =>
        OutcomeMetric.Count("Adamas.Storefront", "sales", "RWS_NewMod_Storefront", "RWS_NewMod_StorefrontDesc",
            MetricCategory.Economy,
            InstallStorefront, breakdown: true, dependencies: ["Orion.CashRegister"]);

    internal static ModMetricAdapterRegistration MuseumVisits() =>
        OutcomeMetric.Count("BiomesTeam.BiomesFossils", "museumvisits", "RWS_NewMod_Museum", "RWS_NewMod_MuseumDesc",
            MetricCategory.Social,
            InstallMuseum, breakdown: true);

    private static void InstallSpa(Harmony harmony)
    {
        const string assembly = "HospitalitySpa";
        spaComp = OutcomeMetric.Bind(assembly + ".CompVendingMachine", assembly);
        spaContainer = AccessTools.DeclaredProperty(spaComp, "MainContainer");
        if (spaContainer is null || !typeof(ThingOwner).IsAssignableFrom(spaContainer.PropertyType))
            throw new InvalidOperationException("Spa payment container changed");
        var helper = OutcomeMetric.Bind(assembly + ".VendingMachineJobHelper", assembly);
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(helper, "InsertCoin", typeof(bool), typeof(Pawn), typeof(Thing)),
            typeof(ServiceOutcomes), "BeforeSpa", "AfterSpa");
    }

    internal static long CountSilver(ThingOwner container)
        => container.Where(item => item.def == ThingDefOf.Silver).Sum(item => (long)item.stackCount);

    private static void BeforeSpa(Pawn? __0, Thing? __1, out Payment? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Spa) || __0 is not { Faction: { IsPlayer: false } } ||
            __1 is not ThingWithComps facility || !facility.Spawned || !GameFacts.IsPlayer(facility))
            return;
        try
        {
            var comp = facility.AllComps.FirstOrDefault(spaComp.IsInstanceOfType);
            if (comp is not null && spaContainer.GetValue(comp) is ThingOwner cash)
                __state = new Payment(cash, facility.def, CountSilver(cash));
        }
        catch (Exception error) { LiveMetrics.Fail(Spa, error); }
    }

    private static void AfterSpa(bool __runOriginal, bool __result, Payment? __state)
    {
        if (!__runOriginal || !__result || __state is not { } payment)
            return;
        try
        {
            long gained = CountSilver(payment.Cash) - payment.Before;
            if (gained > 0)
                OutcomeMetric.Record(Spa, payment.Facility, breakdown: true, amount: gained);
        }
        catch (Exception error) { LiveMetrics.Fail(Spa, error); }
    }

    private static void InstallStorefront(Harmony harmony)
    {
        var sales = OutcomeMetric.Bind("Storefront.Selling.Toils_Selling", "Storefront");
        var cashRegister = AccessTools.TypeByName("CashRegister.Building_CashRegister");
        if (cashRegister is null || !typeof(Building).IsAssignableFrom(cashRegister))
            throw new InvalidOperationException("Storefront cash register signature changed");
        var sell = OutcomeMetric.Method(sales, "SellThing", typeof(void), typeof(Pawn), typeof(Pawn),
            typeof(Thing), cashRegister, typeof(int));
        OutcomeMetric.Patch(harmony, sell, typeof(ServiceOutcomes), "BeforeSale", "AfterSale");
    }

    private static long Goods(Pawn buyer, ThingDef product, ThingDef? stuff)
    {
        static IEnumerable<Thing> Items(Pawn pawn)
        {
            if (pawn.inventory is { } inventory)
                foreach (var item in inventory.innerContainer)
                    yield return item;
            if (pawn.apparel is { } apparel)
                foreach (var item in apparel.WornApparel)
                    yield return item;
            if (pawn.equipment is { } equipment)
                foreach (var item in equipment.AllEquipmentListForReading)
                    yield return item;
        }
        return Items(buyer).Where(item => item.def == product && item.Stuff == stuff)
            .Sum(item => (long)item.stackCount);
    }

    private static void BeforeSale(Pawn __0, Pawn __1, Thing __2, Building? __3, out Sale? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(Store) || !GameFacts.IsPlayer(__0) || GameFacts.IsPlayer(__1) ||
            !GameFacts.IsPlayer(__3) || __3?.Spawned != true || __1 is not { Spawned: true } || __2 is null)
            return;
        try { __state = new Sale(__1, __2.def, __2.Stuff, Goods(__1, __2.def, __2.Stuff)); }
        catch (Exception error) { LiveMetrics.Fail(Store, error); }
    }

    private static void AfterSale(bool __runOriginal, Sale? __state)
    {
        if (!__runOriginal || __state is not { } sale)
            return;
        try
        {
            if (Goods(sale.Buyer, sale.Product, sale.Stuff) > sale.Before)
                OutcomeMetric.Record(Store, sale.Product, breakdown: true);
        }
        catch (Exception error) { LiveMetrics.Fail(Store, error); }
    }

    private static void InstallMuseum(Harmony harmony)
    {
        museumLearning = OutcomeMetric.Bind("BMT_Fossils.JobDriver_MuseumLearning", "BMT_Fossils");
        museumVisit = OutcomeMetric.Bind("BMT_Fossils.JobDriver_VisitMuseum", "BMT_Fossils");
        OutcomeMetric.Patch(harmony, OutcomeMetric.Method(typeof(JobDriver), "Cleanup", typeof(void), typeof(JobCondition)),
            typeof(ServiceOutcomes), "BeforeMuseum", "AfterMuseum");
    }

    private static void BeforeMuseum(JobDriver __instance, JobCondition __0, out Def? __state)
    {
        __state = null;
        if (__0 != JobCondition.Succeeded || !GameFacts.IsPlayer(__instance.pawn))
            return;
        var type = __instance.GetType();
        if ((type == museumLearning || type == museumVisit) && OutcomeMetric.Observing(Museum))
            __state = __instance.job?.def;
    }

    private static void AfterMuseum(bool __runOriginal, Def? __state)
    {
        if (__runOriginal && __state is { } job)
            OutcomeMetric.Record(Museum, job, breakdown: true);
    }
}
