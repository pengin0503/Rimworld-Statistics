using RimworldStatistics.VfeSecurity;

namespace RimworldStatistics.Metrics;

/// <summary>
/// Native, game-internal optional MOD families. They share the existing
/// provider catalog and checkpoint storage, but do not allocate SDK event IDs.
/// </summary>
internal static class IntegratedTransitionAdapters
{
    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
        => VfeSecurityStatistics.Build();
}
