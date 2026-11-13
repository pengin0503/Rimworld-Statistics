using RimworldStatistics.VaeAchievements;
using RimworldStatistics.VaeAspirations;
using RimworldStatistics.VfeSecurity;
using RimworldStatistics.VfeInsectoids;
using RimworldStatistics.VfeClassical;
using RimworldStatistics.VfeEmpire;
using RimworldStatistics.Vsie;
using RimworldStatistics.GiddyUp;
using RimworldStatistics.CombatExtendedReload;
using RimworldStatistics.VreAndroid;
using RimworldStatistics.VseExpertise;
using RimworldStatistics.VefProcessing;
using RimworldStatistics.Gastronomy;
using RimworldStatistics.VqeDroneFactory;
using RimworldStatistics.VceDeepchem;
using RimworldStatistics.VgeExtraction;
using RimworldStatistics.VnpeGrinder;
using RimworldStatistics.VfeTribals;

namespace RimworldStatistics.Metrics;

/// <summary>
/// Native, game-internal optional MOD families. They share the existing
/// provider catalog and checkpoint storage, but do not allocate SDK event IDs.
/// </summary>
internal static class IntegratedTransitionAdapters
{
    internal static IReadOnlyList<ModMetricAdapterRegistration> Build()
    {
        var result = new List<ModMetricAdapterRegistration>();
        result.AddRange(VfeSecurityStatistics.Build());
        result.AddRange(VaeAchievementsStatistics.Build());
        result.AddRange(VfeTribalsStatistics.Build());
        result.AddRange(VaeAspirationsStatistics.Build());
        result.AddRange(VfeInsectoidsStatistics.Build());
        result.AddRange(VfeClassicalStatistics.Build());
        result.AddRange(VfeEmpireStatistics.Build());
        result.AddRange(VsieStatistics.Build());
        result.AddRange(GiddyUpMountStatistics.Build());
        result.AddRange(CombatExtendedReloadStatistics.Build());
        result.AddRange(VreAndroidAwakeningStatistics.Build());
        result.AddRange(VseExpertiseStatistics.Build());
        result.AddRange(VefProcessingStatistics.Build());
        result.AddRange(GastronomyPaymentStatistics.Build());
        result.AddRange(VqeDroneFactoryStatistics.Build());
        result.AddRange(VceDeepchemStatistics.Build());
        result.AddRange(VgeExtractionStatistics.Build());
        result.AddRange(VnpeGrinderStatistics.Build());
        return result;
    }
}
