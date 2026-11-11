namespace RimworldStatistics.DubsBadHygieneAddon;

/// <summary>
/// Adds a given physical DBH storage component only once, even if a pipe network
/// is repeated in the source array. Bad/missing readings make the snapshot unknown.
/// The game-specific reader is intentionally separate from this pure policy.
/// </summary>
internal sealed class DbhStorageAccumulator
{
    private readonly HashSet<object> towers = new(ReferenceEqualityComparer.Instance);
    private double total;
    private bool invalid;

    internal int DistinctTowers => towers.Count;

    internal bool Add(object? tower, float liters)
    {
        if (invalid) return false;
        if (tower is null || !float.IsFinite(liters) || liters < 0 || liters > 1e12f)
        {
            invalid = true;
            return false;
        }
        if (towers.Add(tower))
        {
            total += liters;
            if (!double.IsFinite(total) || total > 1e12)
            {
                invalid = true;
                return false;
            }
        }
        return true;
    }

    // No measured tank is unknown, never an invented zero.
    internal double? Value => invalid || towers.Count == 0 ? null : total;
}
