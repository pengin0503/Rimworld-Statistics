using RimworldStatistics.VaeAchievements;
using RimworldStatistics.VfeSecurity;
using RimworldStatistics.VfeTribals;

namespace RimworldStatistics.Metrics;

/// <summary>
/// Native, game-internal optional MOD families. They share the existing
/// provider catalog and checkpoint storage, but do not allocate SDK event IDs.
/// </summary>
internal static class IntegratedTransitionAdapters
{
    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        var result = new List<ModMetricAdapterRegistration>();
        result.AddRange(VfeSecurityStatistics.Build());
        result.AddRange(VaeAchievementsStatistics.Build());
        result.AddRange(VfeTribalsStatistics.Build());
        return result;
    }
}
