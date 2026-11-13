namespace RimworldStatistics.VfeEmpire;

/// <summary>Honor references, not Def identity, distinguish distinct awards of the same kind.</summary>
internal static class VfeEmpireRules
{
    internal static IReadOnlyList<T> Committed<T>(IEnumerable<T> beforePending,
        IEnumerable<T> afterPending, IEnumerable<T> afterBestowed) where T : class
    {
        var pendingNow = new HashSet<T>(afterPending, ReferenceEqualityComparer.Instance);
        var bestowedNow = new HashSet<T>(afterBestowed, ReferenceEqualityComparer.Instance);
        return beforePending.Where(h => !pendingNow.Contains(h) && bestowedNow.Contains(h))
            .Distinct<T>(ReferenceEqualityComparer.Instance).ToArray();
    }
}
