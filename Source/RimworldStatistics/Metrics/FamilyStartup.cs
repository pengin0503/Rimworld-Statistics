using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>
/// One-time actions when a family begins recording (a new game, or a load where the family starts fresh) and at every load:
/// the membership and lifetime baselines, game conditions already running (tracked for their days, never counted as starts),
/// and the count of existing player caravans. A failure closes only the affected family.
/// </summary>
internal static class FamilyStartup
{
    public static void Apply(LiveMetricAccumulator acc, int tick)
    {
        Run(acc, MembershipFamily.Name, tick, (s, fresh) =>
        {
            if (fresh)
                MembershipFamily.Baseline(s, Members().Select(GameFacts.Id));
        });
        Run(acc, LifetimeFamily.Name, tick, (s, fresh) =>
        {
            if (fresh && s.SinceTick == 0)
                LifetimeFamily.Baseline(s, Members().Select(GameFacts.Id).ToList(), tick,
                    id => Members().FirstOrDefault(p => GameFacts.Id(p) == id) is { } p ? GameFacts.Skills(p) : new Dictionary<string, int>());
        });
        Run(acc, DisasterFamily.Name, tick, (s, _) => DisasterFamily.Reconcile(s, ConditionProbe.ActiveNow().ToList(), tick));
        Run(acc, CaravanFamily.Name, tick, (s, _) => CaravanFamily.Concurrent(s, CaravanProbe.PlayerCaravans()));
    }

    private static IEnumerable<Pawn> Members()
        => PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep.Where(GameFacts.IsMember);

    private static void Run(LiveMetricAccumulator acc, string family, int tick, Action<FamilyState, bool> action)
    {
        if (acc.Family(family) is not { } s)
            return;
        try
        {
            var fresh = s.SinceTick == tick && s.Metrics.Count == 0 && s.Active.Count == 0;
            action(s, fresh);
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(family, e);
        }
    }
}
