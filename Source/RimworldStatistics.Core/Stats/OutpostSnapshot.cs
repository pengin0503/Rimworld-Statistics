namespace RimworldStatistics.Core;

/// <summary>GEN-OUT-001: active VEF Outposts at one successful save; never a production/event counter.</summary>
public static class OutpostSnapshot
{
    public const string Unit = "outpost_state";
    public const string Active = "exploration.outposts.active";

    /// <summary>Recognize the VEF Outposts.Outpost base and its subclasses without referencing a third-party DLL.</summary>
    public static bool IsOutpostType(Type? type)
    {
        for (var current = type; current != null; current = current.BaseType)
            if (string.Equals(current.FullName, "Outposts.Outpost", StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>Count of live player-owned outposts in a representative saved state.</summary>
    public static Dictionary<string, MetricEntry> Build(int activeCount)
    {
        if (activeCount < 0)
            throw new ArgumentOutOfRangeException(nameof(activeCount));
        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [Active] = MetricEntry.Of(MetricKeys.Revision(Active),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = activeCount }),
        };
    }
}
