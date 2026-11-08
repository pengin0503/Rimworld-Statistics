namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · membership (ledger P-001…P-003, P-009, N1-C-003, N1-C-004, N4-P-003, P-018, P-027, P-028, N4-P-005, N4-P-006,
/// N5-M-002; evidence #membership). A member is a free, non-slave colonist. The adapters report transitions of observed routes
/// only; a route the game takes without one of them is not recorded, so the totals are those of the verified routes.
/// </summary>
public static class MembershipFamily
{
    public const string Name = "membership";

    public const string FirstJoin = "membership.first_join";
    public const string PrisonerRecruit = "membership.prisoner_recruit";
    public const string HumanBirths = "membership.human_birth";
    public const string Unique = "membership.unique";
    public const string Routes = "membership.route";
    public const string Rejoin = "membership.rejoin";
    public const string JoinAgeMean = "membership.join_age_mean";
    public const string SlavesFreed = "membership.slave_freed";
    public const string RecruitTimeMin = "membership.recruit_time.min";
    public const string RecruitTimeMax = "membership.recruit_time.max";
    public const string Kidnaps = "membership.kidnapped";
    public const string KidnapRescues = "membership.kidnap_rescued";
    public const string CaptiveMax = "membership.captive_max";
    public const string FirstJoinDay = "membership.first_day";

    public const string RoutePrisoner = "prisoner";
    public const string RouteSlave = "slave";
    public const string RouteOther = "other";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string Members = "members";         // distinct ids ever members while recorded (incl. the baseline)
    private const string Joins = "first_joins";        // N and routes; First is the first join tick
    private const string Recruits = "recruits";
    private const string Rejoins = "rejoins";
    private const string Births = "births";
    private const string JoinAge = "join_age";
    private const string Freed = "slaves_freed";
    private const string RecruitTime = "recruit_time";
    private const string KidnapCount = "kidnapped";
    private const string Rescued = "rescued";
    private const string Captive = "captive";
    private const string LeftTracker = "left";
    private const string PrisonerSince = "prisoner_since";
    private const string KidnappedSince = "kidnapped_since";

    /// <summary>The members when recording begins (the starting colonists of a new game): known, not joins.</summary>
    public static void Baseline(FamilyState s, IEnumerable<string> members)
    {
        foreach (var id in members)
            s.Distinct(Members, id);
    }

    /// <summary>The pawn became a member by <paramref name="route"/> (a prisoner recruited, a slave emancipated, any other).</summary>
    public static void Joined(FamilyState s, string id, string route, double? ageYears, int tick)
    {
        if (s.Distinct(Members, id))
        {
            s.Count(Joins);
            s.Mark(Joins, tick);
            s.CountBy(Joins, route);
            if (ageYears is { } age)
                s.Observe(JoinAge, age);
            if (route == RoutePrisoner)
            {
                s.Count(Recruits);
                if (s.Untrack(PrisonerSince, id) is { } since && tick >= since)
                    s.Observe(RecruitTime, tick - since);
            }
            return;
        }
        s.Untrack(PrisonerSince, id);
        if (s.Untrack(LeftTracker, id) is not null)
            s.Distinct(Rejoins, id);
    }

    /// <summary>A member stopped being one (joined another faction, was banished).</summary>
    public static void Left(FamilyState s, string id, int tick)
    {
        if (s.Peek(Members)?.By?.ContainsKey(id) == true)
            s.Track(LeftTracker, id, tick);
    }

    /// <summary>A confirmed capture starts a new recruitment interval, including after an unobserved escape.</summary>
    public static void BecamePrisoner(FamilyState s, string id, int tick)
        => s.Track(PrisonerSince, id, tick);

    /// <summary>A prisoner stopped being one without being recruited (released, escaped, died): its interval is dropped.</summary>
    public static void PrisonerGone(FamilyState s, string id) => s.Untrack(PrisonerSince, id);

    /// <summary>A living player-side human baby was born: a new member, not a join.</summary>
    public static void Born(FamilyState s, string id, int tick)
    {
        if (s.Distinct(Members, id))
            s.Count(Births);
    }

    public static void SlaveFreed(FamilyState s, string id) => s.Count(Freed);

    public static void Kidnapped(FamilyState s, string id, int tick)
    {
        s.Count(KidnapCount);
        s.Track(KidnappedSince, id, tick);
    }

    /// <summary>A tracked kidnapped member came back through a confirmed route (ransom).</summary>
    public static void KidnapReturned(FamilyState s, string id, int tick)
    {
        if (s.Untrack(KidnappedSince, id) is not { } since)
            return;
        s.Count(Rescued);
        if (tick >= since)
            s.Observe(Captive, tick - since);
    }

    /// <summary>A kidnapped member is lost for good (joined the kidnappers, destroyed): its interval is dropped.</summary>
    public static void KidnapLost(FamilyState s, string id) => s.Untrack(KidnappedSince, id);

    private static void Build(FamilyOutputContext c)
    {
        var s = c.State;
        var joins = s.Peek(Joins);
        c.Put(FirstJoin, c.Value(integer: joins?.N ?? 0));
        c.PutBreakdown(Routes, c.Counts(joins), c.Value(integer: joins?.N ?? 0));
        c.PutCount(Recruits, PrisonerRecruit);
        c.PutCount(Births, HumanBirths);
        c.PutDistinct(Members, Unique);
        c.PutDistinct(Rejoins, Rejoin);
        c.PutMean(JoinAge, JoinAgeMean);
        c.PutCount(Freed, SlavesFreed);
        c.PutExtreme(RecruitTime, RecruitTimeMin, max: false, 1.0 / StatFormat.TicksPerDay);
        c.PutExtreme(RecruitTime, RecruitTimeMax, max: true, 1.0 / StatFormat.TicksPerDay);
        c.PutCount(KidnapCount, Kidnaps);
        c.PutCount(Rescued, KidnapRescues);
        c.PutExtreme(Captive, CaptiveMax, max: true, 1.0 / StatFormat.TicksPerDay);
        c.PutFirstDay(Joins, FirstJoinDay);
        // Past the member cap a new pawn cannot be told from a known one: every output that needs that is partial.
        if (s.Peek(Members)?.Overflow == true)
            c.MarkPartial(FirstJoin, Routes, PrisonerRecruit, HumanBirths, Unique, Rejoin, JoinAgeMean, FirstJoinDay, RecruitTimeMin, RecruitTimeMax);
        // An interval dropped at a tracker's cap is a missing sample (the leaving tracker holds members only, so the member set's
        // cap above covers it).
        if (s.TrackerOverflowed(PrisonerSince))
            c.MarkPartial(RecruitTimeMin, RecruitTimeMax);
        if (s.TrackerOverflowed(KidnappedSince))
            c.MarkPartial(KidnapRescues, CaptiveMax);
    }
}
