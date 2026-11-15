using System.Reflection;
using HarmonyLib;
using RimWorld.Planet;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using Verse;

namespace RimworldStatistics.PopularModOutcomes;

internal static class LandformOutcomes
{
    private const string Owner = PopularOutcomeRules.GeologicalLandforms;
    private static MethodInfo? tryGet;
    private static FieldInfo forms = null!;
    private static Type dataType = null!;

    internal static ModMetricAdapterRegistration Build() => new(new ModMetricDescriptor
    {
        OwnerPackageId = Owner, ProviderId = "rws.geological_landforms", FamilyId = "geological_landforms",
        MetricName = "home_maps", Revision = 1, Category = MetricCategory.Colony, Unit = MetricUnit.Count,
        Source = MetricObservationSource.SaveSnapshot, Overall = OverallAggregation.Max,
        HasBreakdown = true, Selection = ColonySelection.ObservationMaxByBreakdown,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_LandformHomes", DescriptionKey = "RWS_LandformHomesDesc",
    }, CaptureSnapshot: Capture);

    private static MetricEntry? Capture()
    {
        // Lunar can load this assembly after ordinary mod initialization.
        var lookup = tryGet ?? Bind();
        var component = Find.World?.GetComponent(dataType);
        if (component is null)
            return null;

        // The upstream generation patch commits tile data for real maps and
        // skips preview generation. Only read data for a loaded player home.
        return PopularOutcomeRules.Landforms(Find.Maps
            .Where(map => map.IsPlayerHome && !map.IsPocketMap && map.Tile.Valid && map.Tile.Layer.IsRootSurface)
            .Select(map =>
            {
                object?[] arguments = [(int)map.Tile, null];
                var ids = lookup.Invoke(component, arguments) is true && arguments[1] is { } stored
                    ? (List<string>?)forms.GetValue(stored) ?? (IReadOnlyList<string>)Array.Empty<string>()
                    : null;
                return (map.uniqueID, ids);
            }));
    }

    private static MethodInfo Bind()
    {
        const string assembly = "GeologicalLandforms";
        var data = OutcomeMetric.Bind(assembly + ".LandformData", assembly);
        var tileData = OutcomeMetric.Bind(assembly + ".LandformData+TileData", assembly);
        var lookup = OutcomeMetric.Method(data, "TryGet", typeof(bool), typeof(int), tileData.MakeByRefType());
        forms = AccessTools.DeclaredField(tileData, "Landforms") is { } field && field.FieldType == typeof(List<string>)
            ? field : throw new InvalidOperationException("Geological Landforms tile data changed");
        dataType = data;
        return tryGet = lookup;
    }
}
