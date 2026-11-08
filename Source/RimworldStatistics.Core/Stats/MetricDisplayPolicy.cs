namespace RimworldStatistics.Core;

/// <summary>Visibility is distinct from the active game-session recording policy.</summary>
public static class MetricDisplayPolicy
{
    public static IReadOnlyList<MetricDefinition> SelectDefinitions(
        IReadOnlyCollection<MetricDefinition> catalog, IReadOnlySet<string> visibleUnitIds)
        => catalog.Where(d => d.Published || d.Acquisition is { } id && visibleUnitIds.Contains(id)).ToArray();
}
