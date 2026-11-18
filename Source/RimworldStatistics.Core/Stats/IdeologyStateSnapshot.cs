namespace RimworldStatistics.Core;

/// <summary>
/// Phase D2 Ideology save-time state snapshot: fluid ideology reform count, connected Gauranlen trees peak,
/// and collected ideology relics peak. Non-applicable states (classic/fixed ideo for fluid reforms, ideos without
/// relic precepts for collected relics) emit <see cref="MetricCoverage.NotApplicable"/> rather than a false zero.
/// </summary>
public static class IdeologyStateSnapshot
{
    public const string Unit = "ideology_state";

    public const string FluidReforms = "ideology.fluid.reforms";
    public const string ConnectedGauranlenTreesPeak = "ideology.gauranlen.connected_peak";
    public const string CollectedRelicsPeak = "ideology.relics.collected_peak";

    public static Dictionary<string, MetricEntry> Build(
        int? fluidReforms,
        int? connectedGauranlenTrees,
        int? collectedRelics,
        bool partial = false)
    {
        var coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);

        PutOptional(FluidReforms, fluidReforms);
        PutOptional(ConnectedGauranlenTreesPeak, connectedGauranlenTrees);
        PutOptional(CollectedRelicsPeak, collectedRelics);

        return result;

        void PutOptional(string key, int? value)
        {
            if (value is { } n && n >= 0)
            {
                result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
                {
                    Coverage = coverage,
                    Weakened = partial ? true : null,
                    IntegerValue = n,
                });
            }
            else
            {
                result[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
                {
                    Coverage = MetricCoverage.NotApplicable,
                });
            }
        }
    }
}
