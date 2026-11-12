using RimworldStatistics.Core;

namespace RimworldStatistics.UI;

/// <summary>Shared UI text for registered metrics; Details remains the source of formatting rules.</summary>
internal static class MetricText
{
    public static string Label(MetricDefinition definition) => DetailsTab.DefinitionLabel(definition);
    public static string Format(MetricDefinition definition, NumericValue value) => DetailsTab.Format(definition, value);
    public static string Tip(NumericRow row, StatsResult stats) => DetailsTab.OverallTip(row, stats);
}
