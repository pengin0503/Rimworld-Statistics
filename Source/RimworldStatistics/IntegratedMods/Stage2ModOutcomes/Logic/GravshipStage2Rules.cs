namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Pure eligibility helpers for Vanilla Gravship Expanded (vanillaexpanded.gravship,
/// 1.7/Assemblies/VanillaGravshipExpanded.dll) Stage 2 Gravdata statistics.
/// Strictly counts newly generated Gravdata from flight landing yield and orbital
/// Gravtech Console research while excluding Building_GravshipBlackBox storage round-trips.
/// </summary>
internal static class GravshipStage2Rules
{
    internal const string OwnerPackageId = "vanillaexpanded.gravship";

    internal static double FilterLandingGravdataYield(bool isPlayerEngine, int calculatedYield)
        => isPlayerEngine && calculatedYield > 0 ? calculatedYield : 0d;

    internal static double ComputeConsoleGravdataDelta(
        bool hasPlayerActor,
        bool sameActiveProject,
        float progressBefore,
        float progressAfter)
    {
        if (!hasPlayerActor || !sameActiveProject)
            return 0d;
        var delta = (double)progressAfter - progressBefore;
        return double.IsFinite(delta) && delta > 1e-6 ? delta : 0d;
    }
}
