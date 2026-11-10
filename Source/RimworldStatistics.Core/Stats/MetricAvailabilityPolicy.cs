namespace RimworldStatistics.Core;

/// <summary>
/// What the selected profile actually proves about availability, without
/// guessing why an unrecorded metric is missing. Counts refer to independent
/// non-excluded colonies, never to the number of .rws files.
/// </summary>
public enum MetricAvailability
{
    NoApplicableColonies,
    NoMeasuredColonies,
    SomeMeasuredColonies,
    AllMeasuredColonies,
}

public static class MetricAvailabilityPolicy
{
    public static MetricAvailability Classify(NumericRow row)
    {
        if (row.Targets <= 0)
            return MetricAvailability.NoApplicableColonies;
        // An old or malformed row may carry a numeric payload but still be
        // explicitly marked Unknown. The count alone is not capture proof.
        if (row.Valid <= 0 || row.Value.Coverage == MetricCoverage.Unknown)
            return MetricAvailability.NoMeasuredColonies;
        return row.Valid < row.Targets
            ? MetricAvailability.SomeMeasuredColonies
            : MetricAvailability.AllMeasuredColonies;
    }

    /// <summary>Observed numbers may still cover only a limited interval.</summary>
    public static bool IsPeriodIncomplete(NumericRow row)
        => row.Value.Coverage == MetricCoverage.Partial
            || row.Value.SinceTick is > 0;
}
