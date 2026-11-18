using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.SeventhWaveModOutcomes;

/// <summary>
/// Biomes! Islands (BiomesTeam.BiomesIslands, 1.7/Assemblies/BiomesIslands.dll):
/// records a SaveSnapshot of active player home surface maps located in BiomeDefs
/// owned by BiomesTeam.BiomesIslands.
/// </summary>
internal static class BiomesIslandsOutcomes
{
    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.biomes_islands",
            OwnerPackageId = BiomesIslandsRules.OwnerPackageId,
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
                new ModPackageRequirement(BiomesIslandsRules.OwnerPackageId),
                new ModPackageRequirement(BiomesIslandsRules.CorePackageId),
            ],
            LabelKey = "RWS_BiomesIslands_HomeMaps",
            DescriptionKey = "RWS_BiomesIslands_HomeMapsDesc",
        }, CaptureSnapshot: CaptureBiomes),
    ];

    internal static IEnumerable<(int MapId, string? BiomePackageId, string? BiomeDefName)> EnumerateHomeSurfaceMaps()
    {
        var maps = Find.Maps;
        if (maps is null)
            return [];

        return maps
            .Where(map => map is { IsPlayerHome: true, IsPocketMap: false } &&
                          map.Tile.Valid &&
                          map.Tile.Layer is { IsRootSurface: true })
            .Select(map => (map.uniqueID, map.Biome?.modContentPack?.PackageId, map.Biome?.defName));
    }

    private static MetricEntry? CaptureBiomes() =>
        Find.Maps is null ? null : BiomesIslandsRules.HomeBiomes(EnumerateHomeSurfaceMaps());
}
