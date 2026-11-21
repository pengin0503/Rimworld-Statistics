using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using Verse;

namespace RimworldStatistics.RimMarket;

/// <summary>
/// RimMarket:
/// 1) Counts silver earned by selling colony items at player-owned stalls via
///    ColonyStalls.Sell (bypassing TradeDeal.TryExecute), broken down by sold item Def.
/// 2) Counts stall fees and revenue shares actually collected from visiting merchant
///    caravans via MarketManager.PayFee and MarketUtility.CollectShare using positive
///    MarketManager.TotalFees deltas, broken down by visiting faction Def.
/// </summary>
internal static class RimMarketStatistics
{
    private const string AssemblyName = "RimMarket";
    private const string StallSalesUnit = "mod.forti.rimmarket.stall_sales";
    private const string MarketFeesUnit = "mod.forti.rimmarket.market_fees";

    private static Type? buildingStallType;
    private static Func<object, bool>? getColonyStall;
    private static Func<object, object?>? getManagerInstance;
    private static Func<object, int>? getTotalFees;
    private static Func<object, ThingComp?>? getVisitStall;
    private static Func<object, Map?>? getVisitMap;
    private static Func<object, Faction?>? getVisitFaction;

    private sealed record FeeAttempt(object Manager, int BeforeTotalFees, FactionDef? FactionDef);

    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        OutcomeMetric.Count(
            owner: RimMarketRules.OwnerPackageId,
            family: "stall_sales",
            label: "RWS_RimMarket_StallSales",
            description: "RWS_RimMarket_StallSalesDesc",
            category: MetricCategory.Economy,
            install: InstallStallSales,
            breakdown: true,
            unit: MetricUnit.Silver),
        OutcomeMetric.Count(
            owner: RimMarketRules.OwnerPackageId,
            family: "market_fees",
            label: "RWS_RimMarket_MarketFees",
            description: "RWS_RimMarket_MarketFeesDesc",
            category: MetricCategory.Economy,
            install: InstallMarketFees,
            breakdown: true,
            unit: MetricUnit.Silver),
    ];

    private static void InstallStallSales(Harmony harmony)
    {
        var stallsType = OutcomeMetric.Bind("RimMarket.Market.ColonyStalls", AssemblyName);
        var stallType = OutcomeMetric.Bind("RimMarket.Buildings.Building_MarketStall", AssemblyName);
        if (!typeof(Building).IsAssignableFrom(stallType))
            throw new InvalidOperationException("RimMarket.Buildings.Building_MarketStall type changed");

        var isColonyStall = AccessTools.PropertyGetter(stallType, "ColonyStall");
        if (isColonyStall?.ReturnType != typeof(bool))
            throw new InvalidOperationException("RimMarket.Buildings.Building_MarketStall.ColonyStall contract changed");

        var sell = AccessTools.DeclaredMethod(stallsType, "Sell",
            [typeof(Pawn), stallType, typeof(Thing).MakeByRefType(), typeof(int).MakeByRefType()]);
        if (sell?.ReturnType != typeof(bool))
            throw new InvalidOperationException("RimMarket.Market.ColonyStalls.Sell signature changed");

        buildingStallType = stallType;
        getColonyStall = OptionalTransitionCallSite.CompileGetter<bool>(isColonyStall);
        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(sell, typeof(RimMarketStatistics), postfix: nameof(AfterStallSell));
        }
        catch
        {
            buildingStallType = null;
            getColonyStall = null;
            throw;
        }
    }

    private static void InstallMarketFees(Harmony harmony)
    {
        var managerType = OutcomeMetric.Bind("RimMarket.Market.MarketManager", AssemblyName);
        var utilityType = OutcomeMetric.Bind("RimMarket.Market.MarketUtility", AssemblyName);
        var visitType = OutcomeMetric.Bind("RimMarket.Market.StallVisit", AssemblyName);
        var compStallType = OutcomeMetric.Bind("RimMarket.Buildings.CompMarketStall", AssemblyName);
        if (!typeof(ThingComp).IsAssignableFrom(compStallType))
            throw new InvalidOperationException("RimMarket.Buildings.CompMarketStall type changed");

        var instanceGetter = AccessTools.PropertyGetter(managerType, "Instance");
        var feesGetter = AccessTools.PropertyGetter(managerType, "TotalFees");
        var stallField = AccessTools.DeclaredField(visitType, "stall");
        var mapField = AccessTools.DeclaredField(visitType, "map");
        var factionField = AccessTools.DeclaredField(visitType, "faction");

        if (instanceGetter is null || !instanceGetter.IsStatic || instanceGetter.ReturnType != managerType ||
            feesGetter?.ReturnType != typeof(int) ||
            stallField?.FieldType != compStallType ||
            mapField?.FieldType != typeof(Map) ||
            factionField?.FieldType != typeof(Faction))
            throw new InvalidOperationException("RimMarket fee tracking contract changed");

        var payFee = OutcomeMetric.Method(managerType, "PayFee", typeof(void), visitType, typeof(Pawn));
        var collectShare = OutcomeMetric.Method(utilityType, "CollectShare", typeof(void), visitType);

        getManagerInstance = OptionalTransitionCallSite.CompileGetter<object?>(instanceGetter);
        getTotalFees = OptionalTransitionCallSite.CompileGetter<int>(feesGetter);
        getVisitStall = OptionalTransitionCallSite.CompileFieldGetter<ThingComp?>(stallField);
        getVisitMap = OptionalTransitionCallSite.CompileFieldGetter<Map?>(mapField);
        getVisitFaction = OptionalTransitionCallSite.CompileFieldGetter<Faction?>(factionField);

        var patcher = new ScopedHarmonyPatcher(harmony);
        try
        {
            patcher.Patch(payFee, typeof(RimMarketStatistics), nameof(BeforePayFee), nameof(AfterFeeMethod));
            patcher.Patch(collectShare, typeof(RimMarketStatistics), nameof(BeforeCollectShare), nameof(AfterFeeMethod));
        }
        catch
        {
            ClearFeeBindings();
            throw;
        }
    }

    private static void ClearFeeBindings()
    {
        getManagerInstance = null;
        getTotalFees = null;
        getVisitStall = null;
        getVisitMap = null;
        getVisitFaction = null;
    }

    private static void AfterStallSell(bool __runOriginal, bool __result, Pawn? __0, Building? __1, Thing? __2, int __3)
    {
        if (!__runOriginal || !__result || __3 <= 0 || !OutcomeMetric.Observing(StallSalesUnit) ||
            __0 is null || __1 is null || __2 is null || buildingStallType is null || getColonyStall is null ||
            !buildingStallType.IsInstanceOfType(__1))
            return;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is null)
                return;

            var isColonyStall = getColonyStall(__1);
            var item = __2.GetInnerIfMinified() ?? __2;
            var itemDef = item.def;
            var key = RimMarketRules.ResolveStallSaleBreakdown(
                succeeded: true,
                isColonyStall: isColonyStall,
                buildingSpawned: __1.Spawned,
                buildingIsPlayer: __1.Faction == player,
                buyerIsNonPlayer: __0.Faction is null || __0.Faction != player,
                price: __3,
                defPackageId: itemDef?.modContentPack?.PackageId,
                defName: itemDef?.defName);

            if (key is not null && itemDef is not null)
                OutcomeMetric.Record(StallSalesUnit, itemDef, breakdown: true, amount: __3);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(StallSalesUnit, e);
        }
    }

    private static void BeforePayFee(object? __instance, object? __0, out FeeAttempt? __state)
    {
        __state = CaptureFeeAttempt(__instance, __0);
    }

    private static void BeforeCollectShare(object? __0, out FeeAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(MarketFeesUnit) || getManagerInstance is null)
            return;

        try
        {
            var manager = getManagerInstance(null!);
            __state = CaptureFeeAttempt(manager, __0);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(MarketFeesUnit, e);
        }
    }

    private static FeeAttempt? CaptureFeeAttempt(object? manager, object? visit)
    {
        if (!OutcomeMetric.Observing(MarketFeesUnit) || manager is null || visit is null ||
            getTotalFees is null || getVisitStall is null || getVisitMap is null || getVisitFaction is null)
            return null;

        try
        {
            var player = Faction.OfPlayerSilentFail;
            if (player is null)
                return null;

            var stallComp = getVisitStall(visit);
            var map = getVisitMap(visit);
            var marketIsPlayer = stallComp?.parent?.Faction == player || map?.ParentFaction == player;
            if (!marketIsPlayer)
                return null;

            var before = getTotalFees(manager);
            var faction = getVisitFaction(visit);
            return new FeeAttempt(manager, before, faction?.def);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(MarketFeesUnit, e);
            return null;
        }
    }

    private static void AfterFeeMethod(bool __runOriginal, FeeAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(MarketFeesUnit) || getTotalFees is null)
            return;

        try
        {
            var after = getTotalFees(__state.Manager);
            var delta = RimMarketRules.ResolveMarketFeeDelta(
                visitValid: true,
                marketIsPlayer: true,
                totalFeesBefore: __state.BeforeTotalFees,
                totalFeesAfter: after);

            if (delta > 0)
                OutcomeMetric.Record(MarketFeesUnit, __state.FactionDef, breakdown: true, amount: delta);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(MarketFeesUnit, e);
        }
    }
}
