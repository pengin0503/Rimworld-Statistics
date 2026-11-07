namespace RimworldStatistics.Core;

/// <summary>Phase C · diplomacy (ledger T-009…T-011, N2-E-007, N2-E-008; evidence #diplomacy).</summary>
public static class DiplomacyFamily
{
    public const string Name = "diplomacy";
    public const string GiftGoodwill = "diplomacy.gift_goodwill";
    public const string AllyToHostile = "diplomacy.ally_to_hostile";
    public const string Reconciled = "diplomacy.reconciled";
    public const string AidRequests = "diplomacy.aid_requested";
    public const string AidArrivals = "diplomacy.aid_arrived";

    /// <summary>RimWorld.FactionRelationKind names.</summary>
    public const string Hostile = "Hostile";
    public const string Neutral = "Neutral";
    public const string Ally = "Ally";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Gifts = "gift_goodwill";
    private const string Betrayed = "ally_to_hostile";
    private const string Peace = "reconciled";
    private const string Requests = "aid_requested";
    private const string Arrivals = "aid_arrived";
    private const string Seen = "relation_seen";    // active: faction|from|to → tick, only for the current tick
    private const string SeenTick = "relation_tick";

    /// <summary>A gift deal: the player's goodwill with the receiving faction before and after it.</summary>
    public static void Gift(FamilyState s, int before, int after)
    {
        var a = s.Acc(Gifts);
        a.Sum = (a.Sum ?? 0) + Math.Max(0, after - before);
    }

    /// <summary>
    /// The player's relation kind with a faction changed. The game notifies both sides of a direct change; the same faction and
    /// transition in the same tick is one change.
    /// </summary>
    public static void RelationChanged(FamilyState s, string factionId, string from, string to, int tick)
    {
        if (from == to)
            return;
        if (s.Tracked(SeenTick, "t") != tick)
        {
            s.Active.Remove(Seen);
            s.Track(SeenTick, "t", tick);
        }
        var key = factionId + "|" + from + "|" + to;
        if (s.Tracked(Seen, key) is not null)
            return;
        // Past the cap the other side's notification could not be recognized: not counting keeps the totals lower bounds.
        if (!s.Track(Seen, key, tick))
            return;
        if (from == Ally && to == Hostile)
            s.Count(Betrayed);
        if (from == Hostile && to is Neutral or Ally)
            s.Count(Peace);
    }

    public static void AidRequested(FamilyState s) => s.Count(Requests);
    public static void AidArrived(FamilyState s) => s.Count(Arrivals);

    private static void Build(FamilyOutputContext c)
    {
        c.PutSum(Gifts, GiftGoodwill);
        c.PutCount(Betrayed, AllyToHostile);
        c.PutCount(Peace, Reconciled);
        c.PutCount(Requests, AidRequests);
        c.PutCount(Arrivals, AidArrivals);
        if (c.State.TrackerOverflowed(Seen))
            c.MarkPartial(AllyToHostile, Reconciled);
    }
}
