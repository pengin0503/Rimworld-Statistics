namespace RimworldStatistics.Core;

/// <summary>One non-currency line of a deal (from the deal's tradeables before it executes).</summary>
public sealed class TradeLine
{
    public required string Def { get; init; }
    public required int Count { get; init; }

    /// <summary>The line's whole contract price in silver.</summary>
    public required double Total { get; init; }

    public required bool PlayerSells { get; init; }
}

/// <summary>
/// Phase C · trade (ledger T-001…T-008, N2-E-004, N2-E-009…N2-E-011, N4-R-001…N4-R-003; evidence #trade). One call per deal
/// that TradeDeal.TryExecute completed outside gift mode; a deal without a non-currency line is not a deal.
/// </summary>
public static class TradeFamily
{
    public const string Name = "trade";
    public const string SalesTotal = "trade.sales.total";
    public const string BuysTotal = "trade.buys.total";
    public const string Deals = "trade.count";
    public const string SalesPeak = "trade.sales.peak";
    public const string BuysPeak = "trade.buys.peak";
    public const string TopSold = "trade.sales.top_item";
    public const string OrbitalDeals = "trade.orbital.count";
    public const string VisitingCaravanDeals = "trade.visiting_caravan.count";
    public const string SalesPeakUnit = "trade.sales.peak_unit";
    public const string PartnerFactionDefs = "trade.partner_faction_defs";
    public const string SoldDefs = "trade.sales.unique_defs";
    public const string BoughtDefs = "trade.buys.unique_defs";
    public const string SalesUnits = "trade.sales.units";
    public const string BuysUnits = "trade.buys.units";
    public const string TopBought = "trade.buys.top_item";

    public const string Orbital = "orbital";
    public const string VisitingCaravan = "visiting_caravan";
    public const string Settlement = "settlement";
    public const string OtherTrader = "other";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Deal = "deals";            // N; By trader kind
    private const string Sold = "sold";              // Sum silver; By def units
    private const string Bought = "bought";
    private const string DealSold = "deal_sold";     // per-deal extremes
    private const string DealBought = "deal_bought";
    private const string UnitSale = "unit_sale";
    private const string SoldUnits = "sold_units";
    private const string BoughtUnits = "bought_units";
    private const string Partners = "partners";

    public static void Traded(FamilyState s, string traderKind, string? factionDef, IReadOnlyList<TradeLine> lines)
    {
        var real = lines.Where(l => l.Count > 0).ToList();
        if (real.Count == 0)
            return;
        s.Count(Deal);
        s.CountBy(Deal, traderKind);
        s.Distinct(Partners, factionDef ?? MetricKeys.UnknownBucket);
        double sold = 0, bought = 0;
        foreach (var l in real)
        {
            if (l.PlayerSells)
            {
                sold += l.Total;
                s.CountBy(Sold, l.Def, l.Count);
                s.Count(SoldUnits, l.Count);
                s.Observe(UnitSale, l.Total / l.Count);
            }
            else
            {
                bought += l.Total;
                s.CountBy(Bought, l.Def, l.Count);
                s.Count(BoughtUnits, l.Count);
            }
        }
        if (real.Any(l => l.PlayerSells))
        {
            AddSum(s, Sold, sold);
            s.Observe(DealSold, sold);
        }
        if (real.Any(l => !l.PlayerSells))
        {
            AddSum(s, Bought, bought);
            s.Observe(DealBought, bought);
        }
    }

    private static void AddSum(FamilyState s, string acc, double v)
    {
        var a = s.Acc(acc);
        a.Sum = (a.Sum ?? 0) + v;
    }

    private static void Build(FamilyOutputContext c)
    {
        var s = c.State;
        c.PutSum(Sold, SalesTotal);
        c.PutSum(Bought, BuysTotal);
        var deals = s.Peek(Deal);
        c.Put(Deals, c.Value(integer: deals?.N ?? 0));
        long Kind(string k) => deals?.By != null && deals.By.TryGetValue(k, out var v) ? (long)v : 0;
        c.Put(OrbitalDeals, c.Value(integer: Kind(Orbital)));
        c.Put(VisitingCaravanDeals, c.Value(integer: Kind(VisitingCaravan)));
        c.PutExtreme(DealSold, SalesPeak, max: true);
        c.PutExtreme(DealBought, BuysPeak, max: true);
        c.PutExtreme(UnitSale, SalesPeakUnit, max: true);
        c.PutTop(Sold, TopSold);
        c.PutTop(Bought, TopBought);
        c.PutCount(SoldUnits, SalesUnits);
        c.PutCount(BoughtUnits, BuysUnits);
        c.Put(SoldDefs, c.Value(integer: s.Peek(Sold)?.By?.Count ?? 0, partial: s.Peek(Sold)?.Overflow == true));
        c.Put(BoughtDefs, c.Value(integer: s.Peek(Bought)?.By?.Count ?? 0, partial: s.Peek(Bought)?.Overflow == true));
        var partners = s.Peek(Partners);
        var known = partners?.By?.Keys.Count(k => k != MetricKeys.UnknownBucket) ?? 0;
        c.PutBreakdown(PartnerFactionDefs, c.Counts(partners), c.Value(integer: known, partial: partners?.Overflow == true));
    }
}
