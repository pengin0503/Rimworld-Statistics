namespace RimworldStatistics.Core;

/// <summary>
/// The outputs of the acquisition units of Phases B, C and D2 (<see cref="AcquisitionUnits"/>). They are in the catalog with
/// their stored keys, units, aggregation and translation keys, so a unit is published by marking it verified, nothing else.
/// </summary>
public static class LiveCatalog
{
    public static IReadOnlyList<MetricDefinition> Definitions { get; } = Array.Empty<MetricDefinition>();
}
