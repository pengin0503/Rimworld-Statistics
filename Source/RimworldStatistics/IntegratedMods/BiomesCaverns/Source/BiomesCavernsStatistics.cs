using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.BiomesCaverns;

/// <summary>
/// Biomes! Caverns: records a SaveSnapshot of active player home maps
/// located in BiomeDefs owned by BiomesTeam.BiomesCaverns.
/// </summary>
internal static class BiomesCavernsStatistics
{
    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.biomes_caverns",
            OwnerPackageId = BiomesCavernsRules.OwnerPackageId,
            FamilyId = "biomes",
            MetricName = "home_maps",
            Revision = 1,
            Category = MetricCategory.Colony,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.SaveSnapshot,
            Overall = OverallAggregation.Max,
            HasBreakdown = true,
            Selection = ColonySelection.ObservationMaxByBreakdown,
            RequiredPackages =
            [
                new ModPackageRequirement(BiomesCavernsRules.OwnerPackageId),
                new ModPackageRequirement(BiomesCavernsRules.CorePackageId),
                new ModPackageRequirement(BiomesCavernsRules.LandformsPackageId),
            ],
            LabelKey = "RWS_BiomesCaverns_HomeMaps",
            DescriptionKey = "RWS_BiomesCaverns_HomeMapsDesc",
        }, CaptureSnapshot: CaptureBiomes),
    ];

    private static MetricEntry? CaptureBiomes()
    {
        var maps = Find.Maps;
        if (maps is null)
            return null;

        return BiomesCavernsRules.HomeBiomes(maps
            .Where(map => map is { IsPlayerHome: true, IsPocketMap: false } &&
                          map.Tile.Valid &&
                          map.Tile.Layer is { IsRootSurface: true })
            .Select(map => (map.uniqueID, map.Biome?.modContentPack?.PackageId, map.Biome?.defName)));
    }
}
