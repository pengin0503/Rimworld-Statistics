using System.Globalization;

namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · raid (ledger B-004, B-005, B-008=N2-C-002, B-016, B-023…B-028, N1-B-007…N1-B-010, N2-C-001, N2-C-004, N2-C-011,
/// N2-C-012, N3-X-011, N5-B-008, N5-M-001; evidence #raid). A raid is tracked by the pawns generated for it: it ends when the
/// last of them is lost from its lord (killed, downed, captured, exited); its outcome is "retreated" when one exited alive,
/// "wiped" when all were killed, otherwise another outcome. A raid whose map vanished has no outcome. Kills per raid count
/// while it is active.
/// </summary>
public static class RaidFamily
{
    public const string Name = "raid";

    public const string PawnsMax = "raid.pawns.max";
    public const string ByArrivalMode = "raid.arrival_mode";
    public const string Raids = ByArrivalMode;   // the breakdown's scalar is the number of raids
    public const string LongestGap = "raid.longest_gap";
    public const string KillsMax = "raid.kills.max";
    public const string Infestations = "raid.infestation.count";
    public const string MechClusters = "raid.cluster.count";
    public const string Manhunters = "raid.manhunter.count";
    public const string Sieges = "raid.strategy.siege";
    public const string Breaches = "raid.strategy.breach";
    public const string Drops = "raid.arrival.drop";
    public const string PointsMax = "raid.points.max";
    public const string DurationMean = "raid.duration.mean";
    public const string RepelledNoDown = "raid.repelled.no_down";
    public const string RepelledRate = "raid.repelled.rate";
    public const string MeanGap = "raid.gap.mean";
    public const string DurationMax = "raid.duration.max";
    public const string Retreated = "raid.retreated";
    public const string Wiped = "raid.wiped";
    public const string ByFaction = "raid.factions";
    public const string DurationMinRepelled = "raid.duration.min_repelled";
    public const string FirstDay = "raid.first_day";

    /// <summary>The fixed sets of 1.7.4985 (evidence #raid; Core and Royalty IncidentDefs, RaidStrategyDefs, PawnsArrivalModeDefs).</summary>
    public static readonly IReadOnlySet<string> InfestationIncidents = new HashSet<string>(StringComparer.Ordinal) { "Infestation", "DeepDrillInfestation" };
    public static readonly IReadOnlySet<string> MechClusterIncidents = new HashSet<string>(StringComparer.Ordinal) { "MechCluster" };
    public static readonly IReadOnlySet<string> ManhunterIncidents = new HashSet<string>(StringComparer.Ordinal) { "ManhunterPack" };
    public static readonly IReadOnlySet<string> SiegeStrategies = new HashSet<string>(StringComparer.Ordinal) { "Siege" };
    public static readonly IReadOnlySet<string> BreachStrategies = new HashSet<string>(StringComparer.Ordinal) { "ImmediateAttackBreaching", "ImmediateAttackBreachingSmart" };
    public static readonly IReadOnlySet<string> DropModes = new HashSet<string>(StringComparer.Ordinal) { "EdgeDrop", "EdgeDropGroups", "CenterDrop", "RandomDrop" };

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Seq = "seq";
    private const string Arrivals = "arrivals";
    private const string Pawns = "pawns";
    private const string Points = "points";
    private const string Modes = "modes";
    private const string Factions = "factions";
    private const string Siege = "siege";
    private const string Breach = "breach";
    private const string Drop = "drop";
    private const string Infestation = "infestation";
    private const string Cluster = "cluster";
    private const string Manhunter = "manhunter";
    private const string Decided = "decided";
    private const string Undecided = "undecided";
    private const string Duration = "duration";
    private const string Repelled = "repelled";
    private const string RepelledDuration = "repelled_duration";
    private const string NoDown = "no_down";
    private const string Retreat = "retreated";
    private const string Wipe = "wiped";
    private const string RaidKills = "raid_kills";

    // Active trackers, keyed by the raid's sequence number (as text) or a pawn id.
    private const string Member = "member";
    private const string Start = "start";
    private const string Alive = "alive";
    private const string Size = "size";
    private const string Dead = "dead";
    private const string Killed = "killed";
    private const string Flags = "flags";
    private const string MapOf = "map";
    private const string MapCodes = "map_codes";
    private const string MapSeq = "map_seq";   // N: the last map code handed out (codes are never reused)

    /// <summary>The per-raid trackers: a key dropped at the cap of any of them loses a raid's outcome.</summary>
    private static readonly string[] RaidTrackers = { Member, Start, Alive, Size, Dead, Killed, Flags, MapOf, MapCodes };

    private const long FlagRetreat = 1;
    private const long FlagColonistDowned = 2;

    private static string Id(long seq) => seq.ToString(CultureInfo.InvariantCulture);

    /// <summary>The map's code (0 when it could not be tracked at the cap: no raid there can be told apart by map).</summary>
    private static long MapCode(FamilyState s, string mapId)
    {
        if (s.Tracked(MapCodes, mapId) is { } code)
            return code;
        var seq = s.Acc(MapSeq);
        var used = s.Active.TryGetValue(MapCodes, out var m) && m.Count > 0 ? m.Values.Max() : 0;
        var next = Math.Max(seq.N ?? 0, used) + 1;
        if (!s.Track(MapCodes, mapId, next))
            return 0;
        seq.N = next;
        return next;
    }

    public static void Arrived(FamilyState s, RaidArrival a, int tick)
    {
        var seq = s.Acc(Seq);
        seq.N = (seq.N ?? 0) + 1;
        var id = Id(seq.N.Value);
        s.Gap(Arrivals, tick);
        s.Observe(Pawns, a.PawnIds.Count);
        s.Observe(Points, a.Points);
        s.Count(Modes);
        s.CountBy(Modes, a.ArrivalMode ?? MetricKeys.UnknownBucket);
        s.CountBy(Factions, a.FactionDef ?? MetricKeys.UnknownBucket);
        if (a.Strategy is { } strategy && SiegeStrategies.Contains(strategy))
            s.Count(Siege);
        if (a.Strategy is { } b && BreachStrategies.Contains(b))
            s.Count(Breach);
        if (a.ArrivalMode is { } mode && DropModes.Contains(mode))
            s.Count(Drop);

        if (a.PawnIds.Count == 0)
            return;
        // Past the cap the raid is not tracked at all (its outcome outputs are partial, Build), rather than half-tracked.
        if (!s.Track(Start, id, tick))
            return;
        s.Track(Alive, id, 0);
        s.Track(Size, id, a.PawnIds.Count);
        s.Track(Dead, id, 0);
        s.Track(Killed, id, 0);
        s.Track(Flags, id, 0);
        s.Track(MapOf, id, MapCode(s, a.MapId));
        var alive = 0;
        foreach (var pawn in a.PawnIds.Distinct(StringComparer.Ordinal))
            if (s.Track(Member, pawn, seq.N.Value))
                alive++;
        s.Track(Alive, id, alive);
        if (alive == 0)
            End(s, id, tick, decided: false);
    }

    /// <summary>An incident other than a raid succeeded (infestations, mech clusters, manhunter packs are counted by def).</summary>
    public static void Incident(FamilyState s, string def)
    {
        if (InfestationIncidents.Contains(def))
            s.Count(Infestation);
        else if (MechClusterIncidents.Contains(def))
            s.Count(Cluster);
        else if (ManhunterIncidents.Contains(def))
            s.Count(Manhunter);
    }

    public static bool IsMember(FamilyState s, string pawnId) => s.Tracked(Member, pawnId) is not null;

    /// <summary>A tracked raider was killed by the player's side (called with the death, before the lord reports the loss).</summary>
    public static void MemberKilledByPlayer(FamilyState s, string pawnId)
    {
        if (s.Tracked(Member, pawnId) is { } seq && s.Tracked(Killed, Id(seq)) is { } k)
            s.Track(Killed, Id(seq), k + 1);
    }

    public static void MemberLost(FamilyState s, string pawnId, RaidLoss loss, int tick)
    {
        if (s.Untrack(Member, pawnId) is not { } seq)
            return;
        var id = Id(seq);
        if (s.Tracked(Alive, id) is not { } alive)
            return;
        if (loss == RaidLoss.Exited)
            s.Track(Flags, id, (s.Tracked(Flags, id) ?? 0) | FlagRetreat);
        if (loss == RaidLoss.Killed)
            s.Track(Dead, id, (s.Tracked(Dead, id) ?? 0) + 1);
        s.Track(Alive, id, alive - 1);
        if (alive - 1 <= 0)
            End(s, id, tick, decided: true);
    }

    /// <summary>A free colonist went down on <paramref name="mapId"/>: every active raid there was not repelled without downs.</summary>
    public static void ColonistDowned(FamilyState s, string mapId)
    {
        if (s.Tracked(MapCodes, mapId) is not { } code)
            return;
        foreach (var id in ActiveOn(s, code))
            s.Track(Flags, id, (s.Tracked(Flags, id) ?? 0) | FlagColonistDowned);
    }

    /// <summary>The map was removed: its raids end without a known outcome, and its code is released.</summary>
    public static void MapRemoved(FamilyState s, string mapId, int tick)
    {
        if (s.Tracked(MapCodes, mapId) is not { } code)
            return;
        foreach (var id in ActiveOn(s, code))
            End(s, id, tick, decided: false);
        s.Untrack(MapCodes, mapId);
    }

    private static List<string> ActiveOn(FamilyState s, long code)
        => s.Active.TryGetValue(MapOf, out var maps) ? maps.Where(kv => kv.Value == code).Select(kv => kv.Key).ToList() : new List<string>();

    private static void End(FamilyState s, string id, int tick, bool decided)
    {
        var start = s.Untrack(Start, id) ?? tick;
        var size = s.Untrack(Size, id) ?? 0;
        var dead = s.Untrack(Dead, id) ?? 0;
        var flags = s.Untrack(Flags, id) ?? 0;
        s.Observe(RaidKills, s.Untrack(Killed, id) ?? 0);
        s.Untrack(Alive, id);
        s.Untrack(MapOf, id);
        if (long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var seq) && s.Active.TryGetValue(Member, out var members))
            foreach (var pawn in members.Where(kv => kv.Value == seq).Select(kv => kv.Key).ToList())
                s.Untrack(Member, pawn);
        if (!decided)
        {
            s.Count(Undecided);
            return;
        }
        var duration = Math.Max(0, tick - start);
        s.Count(Decided);
        s.Observe(Duration, duration);
        var retreated = (flags & FlagRetreat) != 0;
        var wiped = !retreated && size > 0 && dead == size;
        if (retreated)
            s.Count(Retreat);
        if (wiped)
            s.Count(Wipe);
        if (retreated || wiped)
        {
            s.Count(Repelled);
            s.Observe(RepelledDuration, duration);
            if ((flags & FlagColonistDowned) == 0)
                s.Count(NoDown);
        }
    }

    private static void Build(FamilyOutputContext c)
    {
        const double Days = 1.0 / StatFormat.TicksPerDay;
        var s = c.State;
        var pawns = s.Peek(Pawns);
        c.Put(PawnsMax, pawns?.Max is { } pm ? c.Value(integer: (long)pm, samples: (int)(pawns.N ?? 0)) : c.Value(coverage: MetricCoverage.NotApplicable));
        var modes = s.Peek(Modes);
        c.PutBreakdown(ByArrivalMode, c.Counts(modes), c.Value(integer: modes?.N ?? 0, partial: modes?.Overflow == true));
        c.PutBreakdown(ByFaction, c.Counts(s.Peek(Factions)), c.Value(integer: s.Peek(Modes)?.N ?? 0));
        c.PutExtreme(Arrivals, LongestGap, max: true, Days);
        c.PutMean(Arrivals, MeanGap, Days);
        c.PutFirstDay(Arrivals, FirstDay);
        var kills = s.Peek(RaidKills);
        c.Put(KillsMax, kills?.Max is { } km ? c.Value(integer: (long)km, samples: (int)(kills.N ?? 0)) : c.Value(coverage: MetricCoverage.NotApplicable));
        c.PutCount(Infestation, Infestations);
        c.PutCount(Cluster, MechClusters);
        c.PutCount(Manhunter, Manhunters);
        c.PutCount(Siege, Sieges);
        c.PutCount(Breach, Breaches);
        c.PutCount(Drop, Drops);
        c.PutExtreme(Points, PointsMax, max: true);
        c.PutMean(Duration, DurationMean, Days);
        c.PutExtreme(Duration, DurationMax, max: true, Days);
        c.PutExtreme(RepelledDuration, DurationMinRepelled, max: false, Days);
        c.PutCount(NoDown, RepelledNoDown);
        c.PutCount(Retreat, Retreated);
        c.PutCount(Wipe, Wiped);
        var decided = s.Peek(Decided)?.N ?? 0;
        var repelled = s.Peek(Repelled)?.N ?? 0;
        c.Put(RepelledRate, decided == 0
            ? c.Value(numerator: 0, denominator: 0)
            : c.Value(real: repelled / (double)decided, numerator: repelled, denominator: decided));
        // A raid (or a raider, or a map) dropped at a tracker's cap has no outcome, or a wrong one: every output built from the
        // tracked raids is partial, "no decided raid" included.
        if (RaidTrackers.Any(s.TrackerOverflowed))
            c.MarkPartial(KillsMax, DurationMean, DurationMax, DurationMinRepelled, RepelledNoDown, Retreated, Wiped, RepelledRate);
    }
}

/// <summary>Holds a lord's removal during Pawn.Kill until the pawn's death can be confirmed.</summary>
public sealed class RaidDeathScope(string pawnId, bool killedByPlayer)
{
    private int? lossTick;

    public void DeferKilledLoss(int tick) => lossTick ??= tick;

    public void Complete(FamilyState state, bool dead)
    {
        if (dead && killedByPlayer)
            RaidFamily.MemberKilledByPlayer(state, pawnId);
        if (lossTick is { } tick)
            RaidFamily.MemberLost(state, pawnId, dead ? RaidLoss.Killed : RaidLoss.Other, tick);
    }
}
