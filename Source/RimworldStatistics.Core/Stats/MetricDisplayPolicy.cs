using System.Collections.Frozen;

namespace RimworldStatistics.Core;

/// <summary>Visibility is distinct from the active game-session recording policy.</summary>
public static class MetricDisplayPolicy
{
    /// <summary>
    /// The regular Details window displays every implemented output (including
    /// experimental, unobserved and historical optional-MOD definitions).
    /// This does not enable acquisition units or install any game-side hooks.
    /// </summary>
    public static IReadOnlySet<string> AllDetailsVisibleUnits { get; } =
        AcquisitionUnits.All.Select(u => u.Id)
            .Append(MetricRegistry.OptionalModVisibilityId)
            .ToFrozenSet(StringComparer.Ordinal);

    public static IReadOnlyList<MetricDefinition> SelectDefinitions(
        IReadOnlyCollection<MetricDefinition> catalog, IReadOnlySet<string> visibleUnitIds)
        => catalog.Where(d => d.Published || d.Acquisition is { } id && visibleUnitIds.Contains(id)).ToArray();
}
