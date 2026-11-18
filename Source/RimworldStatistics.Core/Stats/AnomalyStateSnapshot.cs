namespace RimworldStatistics.Core;

/// <summary>
/// Phase D2 Anomaly: discovered entity codex ThingDefs, monolith highest level reached,
/// and living entities currently contained on player holding platforms at a successful save.
/// </summary>
public static class AnomalyStateSnapshot
{
    public const string Unit = "anomaly_state";
    public const string DiscoveredEntities = "anomaly.codex.discovered";
    public const string MonolithHighestLevel = "anomaly.monolith.highest_level";
    public const string HeldEntitiesPeak = "anomaly.holding.entities_peak";

    public static Dictionary<string, MetricEntry> Build(
        int? discoveredEntityDefs,
        int? monolithHighestLevel,
        int? heldEntities,
        bool partial = false)
    {
        if (discoveredEntityDefs is < 0)
        {
            partial = true;
            discoveredEntityDefs = null;
        }
        if (monolithHighestLevel is < 0)
        {
            partial = true;
            monolithHighestLevel = null;
        }
        if (heldEntities is < 0)
        {
            partial = true;
            heldEntities = null;
        }

        var c = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        MetricEntry Scalar(string key, int? value) => MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
        {
            Coverage = value.HasValue ? c : (partial ? MetricCoverage.Unknown : MetricCoverage.NotApplicable),
            IntegerValue = value,
        });

        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [DiscoveredEntities] = Scalar(DiscoveredEntities, discoveredEntityDefs),
            [MonolithHighestLevel] = Scalar(MonolithHighestLevel, monolithHighestLevel),
            [HeldEntitiesPeak] = Scalar(HeldEntitiesPeak, heldEntities),
        };
    }
}
