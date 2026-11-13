using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>
/// One-time actions when a family begins recording (a new game, or a load where the family starts fresh) and at every load:
/// the membership and lifetime baselines, game conditions already running (tracked for their days, never counted as starts),
/// the count of existing player caravans, the growing-zone cells, and the sealed ancient shrines. A failure closes only the
/// affected family.
/// </summary>
internal static class FamilyStartup
{
    public static void Apply(LiveMetricAccumulator acc, int tick, IReadOnlySet<string>? onlyFamilies = null)
    {
        // On a hot selection update, initialize only the families newly started;
        // never replay other families' baselines or condition probes.
        if (onlyFamilies is null || onlyFamilies.Contains(CaravanFamily.Name))
            ShrineProbe.Reset();
        void RunSelected(string family, Action<FamilyState, bool> action)
        {
            if (onlyFamilies is null || onlyFamilies.Contains(family))
                Run(acc, family, tick, action);
        }
        RunSelected(MembershipFamily.Name, (s, fresh) =>
        {
            if (fresh)
                MembershipFamily.Baseline(s, Members().Select(GameFacts.Id));
        });
        RunSelected(LifetimeFamily.Name, (s, fresh) =>
        {
            if (fresh && tick == 0)
                LifetimeFamily.Baseline(s, Members().Select(GameFacts.Id).ToList(), tick,
                    id => Members().FirstOrDefault(p => GameFacts.Id(p) == id) is { } p ? GameFacts.Skills(p) : new Dictionary<string, int>());
        });
        RunSelected(DisasterFamily.Name, (s, _) => DisasterFamily.Reconcile(s, ConditionProbe.ActiveNow().ToList(), tick));
        RunSelected(CaravanFamily.Name, (s, _) => CaravanFamily.Concurrent(s, CaravanProbe.PlayerCaravans()));
        RunSelected(CaravanFamily.Name, (s, _) => ShrineProbe.Startup(s, tick));
        RunSelected(FoodFamily.Name, (s, _) => FoodFamily.GrowingCellsBaseline(s, GrowingProbe.Cells(), tick));
    }

    /// <summary>
    /// Before a save writes the checkpoint (game thread): the growing-zone cells are recounted, which checks the tracked count and
    /// restarts the measure after a gap. A few zones per map, once per save.
    /// </summary>
    public static void BeforeSave(LiveMetricAccumulator acc, int tick)
    {
        Run(acc, FoodFamily.Name, tick, (s, _) => FoodFamily.GrowingCellsBaseline(s, GrowingProbe.Cells(), tick));
        // Retire abandoned corpse IDs even if no further slaughter jobs occur.
        Run(acc, ChoreFamily.Name, tick, (s, _) => ChoreFamily.PruneDestroyedCorpses(s, tick));
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
