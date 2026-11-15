using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.VleLandmarks;

/// <summary>
/// Vanilla Landmarks Expanded: records a SaveSnapshot of active player home maps
/// containing LandmarkDefs or TileMutatorDefs owned by VanillaExpanded.VExplorationE,
/// deduplicated per home map without mutating world generation or map previews.
/// </summary>
internal static class VleLandmarksStatistics
{
    internal static IReadOnlyList<ModMetricAdapterRegistration> Build() =>
    [
        new ModMetricAdapterRegistration(new ModMetricDescriptor
        {
            ProviderId = "rws.vle_landmarks",
            OwnerPackageId = VleLandmarksRules.OwnerPackageId,
            FamilyId = "landmarks",
            MetricName = "home_maps",
            Revision = 1,
            Category = MetricCategory.Colony,
            Unit = MetricUnit.Count,
            Source = MetricObservationSource.SaveSnapshot,
            Overall = OverallAggregation.Max,
            HasBreakdown = true,
            Selection = ColonySelection.ObservationMaxByBreakdown,
            RequiredDlc = AcquisitionUnits.Odyssey,
            RequiredPackages =
            [
                new ModPackageRequirement(VleLandmarksRules.OwnerPackageId),
                new ModPackageRequirement(VleLandmarksRules.FrameworkPackageId),
            ],
            LabelKey = "RWS_VleLandmarks_HomeMaps",
            DescriptionKey = "RWS_VleLandmarks_HomeMapsDesc",
        }, CaptureSnapshot: Capture),
    ];

    private static MetricEntry? Capture()
    {
        var maps = Find.Maps;
        if (maps is null)
            return null;

        return VleLandmarksRules.HomeLandmarks(maps
            .Where(map => map is { IsPlayerHome: true, IsPocketMap: false } &&
                          map.Tile.Valid &&
                          map.Tile.Layer is { IsRootSurface: true })
            .Select(map => (map.uniqueID, ReadOwnedFeatures(map))));
    }

    private static IReadOnlyList<string>? ReadOwnedFeatures(Map map)
    {
        var tile = map.TileInfo;
        if (tile is null)
            return null;

        var features = new List<string>();
        var landmarkDef = tile.Landmark?.def;
        if (landmarkDef is not null &&
            string.Equals(landmarkDef.modContentPack?.PackageId, VleLandmarksRules.OwnerPackageId, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(landmarkDef.defName))
        {
            features.Add(landmarkDef.defName);
        }

        var mutators = tile.Mutators;
        if (mutators is not null)
        {
            foreach (var mutator in mutators)
            {
                if (mutator is not null &&
                    string.Equals(mutator.modContentPack?.PackageId, VleLandmarksRules.OwnerPackageId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(mutator.defName))
                {
                    features.Add(mutator.defName);
                }
            }
        }

        return features;
    }
}
