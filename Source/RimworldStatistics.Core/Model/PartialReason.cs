namespace RimworldStatistics.Core;

/// <summary>
/// Why a <see cref="MetricValue"/> is <see cref="MetricCoverage.Partial"/> (S32). The coverage itself is unchanged: this is
/// additive diagnostic data, written only when known, so profiles and checkpoints written before it read back unchanged.
/// </summary>
public enum PartialReason
{
    /// <summary>
    /// The recorded range starts after the colony start (the MOD was added to a running game), so events before it were not
    /// seen. <see cref="MetricValue.SinceTick"/> holds the start.
    /// </summary>
    UnrecordedInterval,

    /// <summary>
    /// The totals mix the colony-worker attribution rules (#392): work by prisoners in the colony's custody was not counted
    /// before the rule changed, so the value is a lower bound.
    /// </summary>
    MixedRule,

    /// <summary>The family itself weakened the value (#307): a cap was reached, an event could not be classified, a route was not counted.</summary>
    WeakenedByFamily,

    /// <summary>A history summary built from a recorder series with missing (invalid) points: the value skips those points.</summary>
    IncompleteSeries,

    /// <summary>Partial, but no more specific evidence is available (e.g. an old save-file salvage or an older checkpoint).</summary>
    Unknown,
}

/// <summary>
/// The one classifier that derives <see cref="MetricValue.PartialReason"/> where the data allows (S32). It is applied to every
/// family value (with the family's rule-mixing state), to history summaries (with their missing-point flag) and to the final
/// save-file metrics, so the priority is decided in one place.
/// </summary>
/// <remarks>
/// Priority when several reasons apply: <see cref="PartialReason.MixedRule"/> &gt; <see cref="PartialReason.WeakenedByFamily"/>
/// &gt; <see cref="PartialReason.UnrecordedInterval"/> &gt; <see cref="PartialReason.IncompleteSeries"/> &gt;
/// <see cref="PartialReason.Unknown"/>. Only a Partial value has a reason: Complete, SnapshotOnly, Unknown and NotApplicable
/// values keep none. Nothing displayed is derived from this in this PR; it is data and diagnostics only.
/// </remarks>
public static class PartialReasonClassifier
{
    /// <summary>
    /// The reason for <paramref name="value"/>, or null when it is not Partial. <paramref name="mixedRule"/> and
    /// <paramref name="incompleteSeries"/> are facts the caller knows about how the value was built.
    /// </summary>
    public static PartialReason? Classify(MetricValue value, bool mixedRule = false, bool incompleteSeries = false)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Coverage != MetricCoverage.Partial)
            return null;
        if (mixedRule)
            return PartialReason.MixedRule;
        if (value.Weakened == true)
            return PartialReason.WeakenedByFamily;
        if (value.SinceTick is > 0)
            return PartialReason.UnrecordedInterval;
        if (incompleteSeries)
            return PartialReason.IncompleteSeries;
        return PartialReason.Unknown;
    }

    /// <summary>
    /// Sets the reason when the classifier finds one at least as strong as the existing one, so a later, weaker fact never
    /// downgrades a value already classified by a stronger one.
    /// </summary>
    public static void Apply(MetricValue value, bool mixedRule = false, bool incompleteSeries = false)
    {
        var reason = Classify(value, mixedRule, incompleteSeries);
        if (reason is null)
            return;
        if (value.PartialReason is { } existing && Priority(existing) >= Priority(reason.Value))
            return;
        value.PartialReason = reason;
    }

    private static int Priority(PartialReason reason) => reason switch
    {
        PartialReason.MixedRule => 4,
        PartialReason.WeakenedByFamily => 3,
        PartialReason.UnrecordedInterval => 2,
        PartialReason.IncompleteSeries => 1,
        _ => 0,
    };
}
