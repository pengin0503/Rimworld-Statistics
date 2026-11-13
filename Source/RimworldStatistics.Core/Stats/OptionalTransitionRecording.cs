namespace RimworldStatistics.Core;

/// <summary>
/// Native counters for verified optional gameplay transitions. Unlike the public
/// event-ID SDK, these have no per-occurrence digest table and therefore do not
/// truncate a repeatable action after 4,096 successful observations.
/// </summary>
public static class OptionalTransitionRecording
{
    /// <summary>One stable breakdown identity: content owner plus original DefName.</summary>
    public static string? QualifiedDefKey(string? ownerPackageId, string? defName)
    {
        if (string.IsNullOrWhiteSpace(ownerPackageId) || string.IsNullOrWhiteSpace(defName)
            || ownerPackageId != ownerPackageId.Trim() || defName != defName.Trim()
            || ownerPackageId.Contains('/') || defName.Contains('/'))
            return null;
        return ownerPackageId.ToLowerInvariant() + "/" + defName;
    }

    /// <summary>
    /// Count only a committed positive integer quantity. With a breakdown, a
    /// missing owner keeps the total but makes the breakdown and scalar partial.
    /// </summary>
    public static bool RecordCount(FamilyState state, string counter, long amount = 1,
        string? key = null, bool withBreakdown = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (amount <= 0)
            return false;
        state.Count(counter, amount);
        if (!withBreakdown)
            return true;
        if (string.IsNullOrEmpty(key))
            state.Acc(counter).Overflow = true;
        else
            state.CountBy(counter, key, amount);
        return true;
    }

    /// <summary>Real, positive committed quantities (pipe net output/accepted storage).</summary>
    public static bool RecordReal(FamilyState state, string counter, double amount)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!double.IsFinite(amount) || amount <= 0)
            return false;
        state.Observe(counter, amount);
        return true;
    }

    public static void PutCount(FamilyOutputContext output, string counter, string metricKey,
        bool withBreakdown = false)
    {
        if (withBreakdown)
            output.CountAndBreakdown(counter, metricKey, metricKey);
        else
            output.PutCount(counter, metricKey);
    }
}
