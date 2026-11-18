namespace RimworldStatistics.Stage2ModOutcomes;

internal static class AlteredCarbonStage2Rules
{
    internal const string OwnerPackageId = "hlx.UltratechAlteredCarbon";
    internal const string BiotechPackageId = "Ludeon.RimWorld.Biotech";
    internal const string FrameworkPackageId = "OskarPotocki.VanillaFactionsExpanded.Core";

    internal static bool ShouldRecordSleeveGrowth(
        bool playerHomeVat,
        bool wasGrowingStateBefore,
        bool isInactiveStateAfter,
        bool innerSleeveAliveAfter)
        => playerHomeVat && wasGrowingStateBefore && isInactiveStateAfter && innerSleeveAliveAfter;

    internal static bool ShouldRecordNeedlecast(
        bool wasNeedlecastedBefore,
        bool targetAliveBefore,
        bool isNeedlecastedAfter,
        bool targetAliveAfter,
        bool targetIsPlayerAfter)
        => !wasNeedlecastedBefore && targetAliveBefore && isNeedlecastedAfter && targetAliveAfter && targetIsPlayerAfter;
}
