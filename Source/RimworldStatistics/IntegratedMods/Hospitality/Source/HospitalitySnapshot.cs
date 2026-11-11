using System.Reflection;
using Verse;

namespace RimworldStatistics.HospitalityAddon;

/// <summary>
/// Reflection-only, source-audited compatible Hospitality map-component shape.
/// No Hospitality DLL reference and no polling. Missing values are unknown.
/// </summary>
internal static class HospitalitySnapshot
{
    private static MethodInfo? componentGetter;
    private static PropertyInfo? guestCollection;
    private static PropertyInfo? groupCollection;
    private static PropertyInfo? guestCount;
    private static PropertyInfo? groupCount;

    internal static bool TryBind()
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, "Hospitality", StringComparison.Ordinal));
            var component = assembly?.GetType("Hospitality.Hospitality_MapComponent", throwOnError: false);
            if (component is null || !typeof(MapComponent).IsAssignableFrom(component))
                return false;
            var getter = typeof(Map).GetMethods(flags).SingleOrDefault(m =>
                m.Name == "GetComponent" && m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0);
            var guests = component.GetProperty("PresentGuests", flags);
            var groups = component.GetProperty("PresentLords", flags);
            var countGuests = guests?.PropertyType.GetProperty("Count", flags);
            var countGroups = groups?.PropertyType.GetProperty("Count", flags);
            if (getter is null || guests?.GetMethod is null || groups?.GetMethod is null ||
                countGuests?.GetMethod is null || countGroups?.GetMethod is null ||
                countGuests.PropertyType != typeof(int) || countGroups.PropertyType != typeof(int))
                return false;
            // Install all binding handles only once all required contracts are checked.
            componentGetter = getter.MakeGenericMethod(component);
            guestCollection = guests;
            groupCollection = groups;
            guestCount = countGuests;
            groupCount = countGroups;
            return true;
        }
        catch (Exception) { return false; }
    }

    internal static double? Guests() => Read(guestCollection, guestCount);
    internal static double? Groups() => Read(groupCollection, groupCount);

    private static double? Read(PropertyInfo? list, PropertyInfo? count)
    {
        if (componentGetter is null || list is null || count is null)
            return null;
        var homes = 0;
        long total = 0;
        foreach (var map in Find.Maps)
        {
            if (!map.IsPlayerHome) continue;
            homes++;
            var component = componentGetter.Invoke(map, null);
            if (component is null) return null;
            var collection = list.GetValue(component);
            if (collection is null) return null;
            if (count.GetValue(collection) is not int n || n < 0)
                throw new InvalidOperationException("Hospitality collection count unavailable");
            total += n;
        }
        return homes == 0 ? null : total;
    }
}
