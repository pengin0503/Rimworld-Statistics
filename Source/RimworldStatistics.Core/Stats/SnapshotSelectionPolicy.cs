namespace RimworldStatistics.Core;

public static class SnapshotSelectionPolicy
{
    public static bool UsesLastCapture(MetricDefinition d)
        => d.ProviderId is not null || d.Acquisition is "factions" or OutpostSnapshot.Unit or VpeStatistics.SnapshotUnit;
}
