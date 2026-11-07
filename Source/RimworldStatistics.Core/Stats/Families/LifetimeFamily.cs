namespace RimworldStatistics.Core;

/// <summary>
/// Phase C · lifetime (ledger P-010=N1-C-002, P-020, P-021, P-023, P-024; evidence #lifetime). Tenure needs a recorded join; skill
/// gains need the levels at that join. Skill changes come from SkillRecord.Learn only (other level changes are not observed).
/// </summary>
public static class LifetimeFamily
{
    public const string Name = "lifetime";
    public const string LifeMean = "population.life.mean";
    public const string TenureMean = "membership.tenure_mean";
    public const string FirstSkill20 = "population.skill.first_20";
    public const string JoinGainPeak = "population.skill.join_gain_peak";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string JoinTick = "joined";   // active: id → join tick
    private const string Base = "base";         // active: "id/skill" → level at join
    private const string Tenure = "tenure";
    private const string Life = "life";
    private const string Twenty = "skill20";    // distinct "id/skill"
    private const string Gain = "gain";

    private static string SkillKey(string id, string skill) => id + "/" + skill;

    /// <summary>The members when recording begins at the game's start (tick 0): their tenure and skill gains start now.</summary>
    public static void Baseline(FamilyState s, IEnumerable<string> members, int tick, Func<string, IReadOnlyDictionary<string, int>> skills)
    {
        foreach (var id in members)
            JoinedAt(s, id, tick, skills(id));
    }

    public static void Joined(FamilyState s, string id, int tick, IReadOnlyDictionary<string, int> skills) => JoinedAt(s, id, tick, skills);

    private static void JoinedAt(FamilyState s, string id, int tick, IReadOnlyDictionary<string, int> skills)
    {
        if (s.Tracked(JoinTick, id) is not null)
            return;
        // Past the cap the pawn gets no tenure and no baseline: without its join tick a repeated join could not be told apart
        // and would move the baseline up. Both outputs are partial from then on (Build).
        if (!s.Track(JoinTick, id, tick))
            return;
        foreach (var (skill, level) in skills)
            s.Track(Base, SkillKey(id, skill), level);
    }

    public static void Left(FamilyState s, string id, int tick) => End(s, id, tick);

    public static void Died(FamilyState s, string id, bool freeColonist, double? ageYears, int tick)
    {
        if (!freeColonist)
            return;
        if (ageYears is { } age)
            s.Observe(Life, age);
        End(s, id, tick);
    }

    private static void End(FamilyState s, string id, int tick)
    {
        if (s.Untrack(JoinTick, id) is { } since && tick >= since)
            s.Observe(Tenure, tick - since);
        if (s.Active.TryGetValue(Base, out var bases))
            foreach (var key in bases.Keys.Where(k => k.StartsWith(id + "/", StringComparison.Ordinal)).ToList())
                s.Untrack(Base, key);
    }

    /// <summary>A skill level changed through learning.</summary>
    public static void SkillChanged(FamilyState s, string id, string skill, int before, int after)
    {
        if (after >= 20 && before < 20)
            s.Distinct(Twenty, SkillKey(id, skill));
        if (after > before && s.Tracked(Base, SkillKey(id, skill)) is { } baseline && after > baseline)
            s.Observe(Gain, after - baseline);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutMean(Life, LifeMean);
        c.PutMean(Tenure, TenureMean, 1.0 / StatFormat.TicksPerDay);
        c.PutDistinct(Twenty, FirstSkill20);
        var g = c.State.Peek(Gain);
        c.Put(JoinGainPeak, g?.Max is { } max ? c.Value(integer: (long)max, samples: (int)(g.N ?? 0)) : c.Value(coverage: MetricCoverage.NotApplicable));
        // A pawn dropped at a tracker's cap is a missing tenure sample or a skill whose gain cannot be seen.
        if (c.State.TrackerOverflowed(JoinTick))
            c.MarkPartial(TenureMean, JoinGainPeak);
        if (c.State.TrackerOverflowed(Base))
            c.MarkPartial(JoinGainPeak);
    }
}
