using System.Reflection;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics.Providers;

/// <summary>
/// Read-only snapshot adapter for the source-audited Hospitality (Continued) 1.7
/// MapComponent. No dependency on the Hospitality DLL and no Harmony patch.
/// The data represent guests/group counts at successful save time, never
/// lifetime visit totals. Unknown/missing components produce no reading.
/// Source: Zaljerem/Hospitality Source/Hospitality_MapComponent.cs.
/// </summary>
internal static class HospitalityMetricProvider
{
    private const string Owner = "Orion.Hospitality";
    private static MethodInfo? getComponent;
    private static PropertyInfo? guests;
    private static PropertyInfo? groups;
    private static PropertyInfo? guestsCount;
    private static PropertyInfo? groupsCount;

    private static readonly ModMetricDescriptor GuestDescriptor = new()
    {
        OwnerPackageId = Owner, ProviderId = "rws.hospitality",
        FamilyId = "visitors", MetricName = "guests",
        Source = MetricObservationSource.SaveSnapshot,
        Category = MetricCategory.Social, Unit = MetricUnit.Count,
        Overall = OverallAggregation.Sum,
        LabelKey = "RWS_ModHospitality_PresentGuests",
        DescriptionKey = "RWS_ModHospitality_PresentGuestsDesc",
    };

    private static readonly ModMetricDescriptor GroupDescriptor = GuestDescriptor with
    {
        MetricName = "groups",
        LabelKey = "RWS_ModHospitality_PresentGroups",
        DescriptionKey = "RWS_ModHospitality_PresentGroupsDesc",
    };

    internal static IReadOnlyList<ModMetricAdapterRegistration> Registrations() =>
    [
        new(GuestDescriptor, CaptureSnapshot: () => Capture(guests, guestsCount, GuestDescriptor),
            ValidateBinding: ValidateBinding, AutoEnableOnInstall: true),
        new(GroupDescriptor, CaptureSnapshot: () => Capture(groups, groupsCount, GroupDescriptor),
            ValidateBinding: ValidateBinding, AutoEnableOnInstall: true),
    ];

    private static void ValidateBinding()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
        var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
            string.Equals(a.GetName().Name, "Hospitality", StringComparison.Ordinal));
        var component = assembly?.GetType("Hospitality.Hospitality_MapComponent", throwOnError: false);
        if (component is null || !typeof(MapComponent).IsAssignableFrom(component))
            throw new MissingMemberException("Hospitality map component is absent or incompatible");

        var getter = typeof(Map).GetMethods(flags).SingleOrDefault(m =>
            m.Name == "GetComponent" && m.IsGenericMethodDefinition &&
            m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0);
        if (getter is null)
            throw new MissingMethodException("Map.GetComponent<T>() is unavailable");

        var guestProperty = component.GetProperty("PresentGuests", flags);
        var groupProperty = component.GetProperty("PresentLords", flags);
        var guestCount = guestProperty?.PropertyType.GetProperty("Count", flags);
        var groupCount = groupProperty?.PropertyType.GetProperty("Count", flags);
        if (guestProperty?.GetMethod is null || groupProperty?.GetMethod is null ||
            guestCount?.GetMethod is null || groupCount?.GetMethod is null ||
            guestCount.PropertyType != typeof(int) || groupCount.PropertyType != typeof(int))
            throw new MissingMemberException("Hospitality present-guest/group collection shape changed");
        // Publish the reflection handles only after all shape checks succeed.
        getComponent = getter.MakeGenericMethod(component);
        guests = guestProperty;
        groups = groupProperty;
        guestsCount = guestCount;
        groupsCount = groupCount;
    }

    private static MetricEntry? Capture(PropertyInfo? collection, PropertyInfo? countProperty,
        ModMetricDescriptor descriptor)
    {
        if (getComponent is null || collection is null || countProperty is null)
            throw new InvalidOperationException("Hospitality snapshot binding was not validated");

        long total = 0;
        var homeMaps = 0;
        foreach (var map in Find.Maps)
        {
            if (!map.IsPlayerHome)
                continue;
            homeMaps++;
            var component = getComponent.Invoke(map, null);
            var value = component is null ? null : collection.GetValue(component);
            if (value is null)
                return null; // Distinguish unavailable from an actually empty guest list.
            var count = (int)(countProperty.GetValue(value)
                ?? throw new InvalidOperationException("Hospitality guest count is null"));
            if (count < 0)
                throw new InvalidOperationException("Hospitality guest count is negative");
            total += count;
        }
        return homeMaps == 0 ? null : MetricEntry.Of(descriptor.Revision, new MetricValue
        {
            IntegerValue = total, Coverage = MetricCoverage.SnapshotOnly,
        });
    }
}
