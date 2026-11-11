namespace RimworldStatistics.DubsBadHygieneAddon;

/// <summary>Source-verified capacity/rate sum over unique pipe networks; invalid means unknown.</summary>
internal sealed class DbhCapacityAccumulator
{
    private readonly HashSet<object> seen = new(ReferenceEqualityComparer.Instance);
    private double total;
    private bool invalid;

    internal bool Add(object? network, float amount)
    {
        if (invalid) return false;
        if (network is null || !float.IsFinite(amount) || amount < 0 || amount > 1e12f)
        {
            invalid = true;
            return false;
        }
        if (seen.Add(network))
        {
            total += amount;
            if (!double.IsFinite(total) || total > 1e12)
            {
                invalid = true;
                return false;
            }
        }
        return true;
    }

    internal double? Value => invalid || seen.Count == 0 ? null : total;
}
