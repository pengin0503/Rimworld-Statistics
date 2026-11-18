namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Pure eligibility helpers for Alpha Genes (sarg.alphagenes, 1.7/Assemblies/AlphaGenes.dll)
/// Stage 2 statistics:
/// 1. Gene absorptions via CompGeneSyphon.Apply(LocalTargetInfo, LocalTargetInfo)
/// 2. Teratogenic carcinoma-to-mutation conversions via CompTeratogenicHediff.Apply(LocalTargetInfo, LocalTargetInfo)
/// </summary>
internal static class AlphaGenesStage2Rules
{
    internal const string OwnerPackageId = "sarg.alphagenes";

    internal static bool IsEligibleSyphonCasterBefore(bool isPlayerCaster, bool isCasterAlive, bool hasGeneTracker)
        => isPlayerCaster && isCasterAlive && hasGeneTracker;

    internal static int ComputeNewlyAddedGeneCount(int countBefore, int countAfter)
        => countAfter > countBefore ? countAfter - countBefore : 0;

    internal static bool ShouldRecordTeratogenicMutation(
        bool isPlayerCaster,
        bool isTargetAlive,
        int carcinomaCountBefore,
        int carcinomaCountAfter,
        bool hasMutationHediffAfter)
        => isPlayerCaster &&
           isTargetAlive &&
           carcinomaCountBefore > 0 &&
           carcinomaCountAfter < carcinomaCountBefore &&
           hasMutationHediffAfter;
}
