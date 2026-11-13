namespace RimworldStatistics.VfeEmpire;

/// <summary>Honor references, not Def identity, distinguish distinct awards of the same kind.</summary>
internal static class VfeEmpireRules
{
    internal static IReadOnlyList<T> Committed<T>(IEnumerable<T> beforePending,
        IEnumerable<T> afterPending, IEnumerable<T> afterBestowed) where T : class
    {
        var pendingNow = afterPending.ToHashSet(ReferenceEqualityComparer.Instance);
        var bestowedNow = afterBestowed.ToHashSet(ReferenceEqualityComparer.Instance);
        return beforePending.Where(h => !pendingNow.Contains(h) && bestowedNow.Contains(h))
            .Distinct(ReferenceEqualityComparer.Instance).ToArray();
    }
}
