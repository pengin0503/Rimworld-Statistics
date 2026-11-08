using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Trade (evidence #trade): TradeDeal.TryExecute's postfix runs after Reset(), so the prefix copies the non-currency lines as
/// the deal stands (the currency count it limits afterwards is not a line); a deal counts only when TryExecute returns true.
/// Gift mode is a diplomacy event: the receiving faction's goodwill before and after.
/// </summary>
[MetricAdapter(TradeFamily.Name, DiplomacyFamily.Name)]
[HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
public static class TradeExecuteMetricPatch
{
    public sealed class Before
    {
        public bool Gift;
        public int Goodwill;
        public List<TradeLine> Lines = new();
    }

    public static void Prefix(List<Tradeable> ___tradeables, out Before? __state)
    {
        __state = null;
        if (!GameFacts.AnyRecording(TradeFamily.Name, DiplomacyFamily.Name) || TradeSession.trader == null)
            return;
        var b = new Before { Gift = TradeSession.giftMode, Goodwill = TradeSession.trader.Faction?.PlayerGoodwill ?? 0 };
        if (!b.Gift && ___tradeables != null)
            foreach (var t in ___tradeables)
            {
                if (t == null || t.IsCurrency || t.ThingDef == null || t.ActionToDo == TradeAction.None)
                    continue;
                var sells = t.ActionToDo == TradeAction.PlayerSells;
                b.Lines.Add(new TradeLine
                {
                    Def = t.ThingDef.defName, Count = Math.Abs(t.CountToTransfer), PlayerSells = sells,
                    Total = sells ? t.CurTotalCurrencyCostForDestination : t.CurTotalCurrencyCostForSource,
                });
            }
        __state = b;
    }

    public static void Postfix(bool __result, Before? __state)
    {
        if (__state == null || !__result || TradeSession.trader == null)
            return;
        if (__state.Gift)
        {
            var after = TradeSession.trader.Faction?.PlayerGoodwill ?? __state.Goodwill;
            GameFacts.Record(DiplomacyFamily.Name, s => DiplomacyFamily.Gift(s, __state.Goodwill, after));
            return;
        }
        var kind = TradeSession.trader switch
        {
            TradeShip => TradeFamily.Orbital,
            Pawn => TradeFamily.VisitingCaravan,
            Settlement => TradeFamily.Settlement,
            _ => TradeFamily.OtherTrader,
        };
        var faction = TradeSession.trader.Faction?.def?.defName;
        GameFacts.Record(TradeFamily.Name, s => TradeFamily.Traded(s, kind, faction, __state.Lines));
    }
}

/// <summary>The player's relation kind with a faction changed (Faction.Notify_RelationKindChanged, called on either side).</summary>
[MetricAdapter(DiplomacyFamily.Name)]
[HarmonyPatch(typeof(Faction), nameof(Faction.Notify_RelationKindChanged))]
public static class RelationKindMetricPatch
{
    public static void Postfix(Faction __instance, Faction other, FactionRelationKind previousKind)
    {
        var player = Faction.OfPlayer;
        if (player == null || other == null)
            return;
        var npc = __instance == player ? other : other == player ? __instance : null;
        if (npc == null)
            return;
        var id = npc.GetUniqueLoadID();
        var from = previousKind.ToString();
        var to = player.RelationKindWith(npc).ToString();
        var tick = GameFacts.Tick;
        GameFacts.Record(DiplomacyFamily.Name, s => DiplomacyFamily.RelationChanged(s, id, from, to, tick));
    }
}

/// <summary>An accepted military aid request (the dialog's CallForAid, which then starts the friendly raid).</summary>
[MetricAdapter(DiplomacyFamily.Name)]
[HarmonyPatch(typeof(FactionDialogMaker), "CallForAid")]
public static class CallForAidMetricPatch
{
    public static void Prefix(Faction faction)
    {
        if (faction != null)
            GameFacts.Record(DiplomacyFamily.Name, DiplomacyFamily.AidRequested);
    }
}

[MetricAdapter(DiplomacyFamily.Name)]
[HarmonyPatch(typeof(IncidentWorker), nameof(IncidentWorker.TryExecute))]
public static class AidArrivedMetricPatch
{
    public static void Postfix(bool __result, IncidentWorker __instance)
    {
        if (__result && __instance is IncidentWorker_RaidFriendly)
            GameFacts.Record(DiplomacyFamily.Name, DiplomacyFamily.AidArrived);
    }
}
