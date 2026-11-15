namespace RimworldStatistics.VreInsector;

internal enum MetapodOutcome
{
    None,
    EvolutionOrChange,
    Reversion,
}

/// <summary>
/// Distinguishes geneline evolution/change completions from full reversions
/// back to baseline Insector on MetapodHediff.Complete(). Upstream does not clear
/// Gene_GenelineEvolution.geneline during reversion, so reversion checks all
/// GenelineGeneDef genes removed rather than requiring geneline == null.
/// </summary>
internal static class VreInsectorRules
{
    internal static MetapodOutcome ClassifyCompletion(
        bool livePlayerPawn,
        bool metapodRemoved,
        bool hasTargetGeneline,
        bool hasEvolutionGeneAfter,
        bool targetGenelineMatchedAfter,
        bool genelineChanged,
        int beforeGenelineGeneCount,
        int afterGenelineGeneCount,
        bool genelineGenesChanged)
    {
        if (!livePlayerPawn || !metapodRemoved)
            return MetapodOutcome.None;

        if (hasTargetGeneline)
        {
            return hasEvolutionGeneAfter && targetGenelineMatchedAfter && (genelineChanged || genelineGenesChanged)
                ? MetapodOutcome.EvolutionOrChange
                : MetapodOutcome.None;
        }

        return beforeGenelineGeneCount > 0 && afterGenelineGeneCount == 0
            ? MetapodOutcome.Reversion
            : MetapodOutcome.None;
    }
}
