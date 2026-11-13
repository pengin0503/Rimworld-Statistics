using RimworldStatistics.VaeAchievements;
using RimworldStatistics.VaeAspirations;
using RimworldStatistics.VfeSecurity;
using RimworldStatistics.VfeInsectoids;
using RimworldStatistics.VfeClassical;
using RimworldStatistics.VfeDeserters;
using RimworldStatistics.VfeEmpire;
using RimworldStatistics.Vsie;
using RimworldStatistics.GiddyUp;
using RimworldStatistics.CombatExtendedReload;
using RimworldStatistics.VreAndroid;
using RimworldStatistics.VseExpertise;
using RimworldStatistics.VefProcessing;
using RimworldStatistics.Gastronomy;
using RimworldStatistics.VqeDroneFactory;
using RimworldStatistics.VqeGenerator;
using RimworldStatistics.VceDeepchem;
using RimworldStatistics.VgeExtraction;
using RimworldStatistics.VnpeGrinder;
using RimworldStatistics.VfeMedieval2;
using RimworldStatistics.VfeSecurityArtillery;
using RimworldStatistics.VfeFactory;
using RimworldStatistics.VfePirates;
using RimworldStatistics.VqeAncients;
using RimworldStatistics.VfeSettlers;
using RimworldStatistics.RimatomicsByproducts;
using RimworldStatistics.CombatExtendedEvents;
using RimworldStatistics.VfePirateHiring;
using RimworldStatistics.VgeHybridOutcomes;
using RimworldStatistics.VfeTribals;
using RimworldStatistics.Rimefeller;
using RimworldStatistics.PrisonExpandedParole;

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
        result.AddRange(VfeDesertersStatistics.Build());
        result.AddRange(VfeEmpireStatistics.Build());
        result.AddRange(VsieStatistics.Build());
        result.AddRange(GiddyUpMountStatistics.Build());
        result.AddRange(CombatExtendedReloadStatistics.Build());
        result.AddRange(VreAndroidAwakeningStatistics.Build());
        result.AddRange(VseExpertiseStatistics.Build());
        result.AddRange(VefProcessingStatistics.Build());
        result.AddRange(GastronomyPaymentStatistics.Build());
        result.AddRange(VqeDroneFactoryStatistics.Build());
        result.Add(VqeGeneratorStatistics.Build());
        result.AddRange(VceDeepchemStatistics.Build());
        result.AddRange(VgeExtractionStatistics.Build());
        result.AddRange(VnpeGrinderStatistics.Build());
        result.AddRange(VfeMedieval2Statistics.Build());
        result.AddRange(VfeSecurityArtilleryStatistics.Build());
        result.AddRange(VfeFactoryStatistics.Build());
        result.AddRange(VfePiratesStatistics.Build());
        result.AddRange(VqeAncientsStatistics.Build());
        result.AddRange(VfeSettlersStatistics.Build());
        result.AddRange(RimatomicsByproductStatistics.Build());
        result.AddRange(CombatExtendedEventStatistics.Build());
        result.AddRange(VfePirateHiringStatistics.Build());
        result.AddRange(VgeHybridOutcomeStatistics.Build());
        result.AddRange(PrisonExpandedParoleStatistics.Build());
        result.AddRange(RimefellerExtractionStatistics.Build());
        result.AddRange(RimefellerFuelUsageStatistics.Build());
        result.AddRange(PrisonExpandedCompletionStatistics.Build());
        return result;
    }
}
