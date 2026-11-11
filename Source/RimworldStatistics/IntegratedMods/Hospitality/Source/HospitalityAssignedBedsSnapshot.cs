using System.Collections;
using System.Reflection;
using Verse;

namespace RimworldStatistics.HospitalityAddon;

/// <summary>Only when the pinned guest component shape exists; no per-tick scanning.</summary>
internal static class HospitalityAssignedBedsSnapshot
{
    private const BindingFlags InstancePublic = BindingFlags.Public | BindingFlags.Instance;
    private static MethodInfo? getMapComponent;
    private static PropertyInfo? presentGuests;
    private static Type? guestCompType;
    private static PropertyInfo? hasAssignedBed;
    private static FieldInfo? assignedBed;

    internal static bool TryBind()
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, "Hospitality", StringComparison.Ordinal));
            var mapComp = assembly?.GetType("Hospitality.Hospitality_MapComponent", false);
            var guestComp = assembly?.GetType("Hospitality.CompGuest", false);
            var bedType = assembly?.GetType("Hospitality.Building_GuestBed", false);
            if (mapComp is null || guestComp is null || bedType is null ||
                !typeof(MapComponent).IsAssignableFrom(mapComp) ||
                !typeof(ThingComp).IsAssignableFrom(guestComp))
                return false;
            var getter = typeof(Map).GetMethods(InstancePublic).SingleOrDefault(m =>
                m.Name == "GetComponent" && m.IsGenericMethodDefinition &&
                m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0);
            var guests = mapComp.GetProperty("PresentGuests", InstancePublic);
            var hasBed = guestComp.GetProperty("HasBed", InstancePublic);
            var bed = guestComp.GetField("bed", InstancePublic);
            if (getter is null || guests?.GetMethod is null ||
                !typeof(IEnumerable).IsAssignableFrom(guests.PropertyType) ||
                hasBed?.GetMethod is null || hasBed.PropertyType != typeof(bool) ||
                bed?.FieldType != bedType)
                return false;
            getMapComponent = getter.MakeGenericMethod(mapComp);
            presentGuests = guests;
            guestCompType = guestComp;
            hasAssignedBed = hasBed;
            assignedBed = bed;
            return true;
        }
        catch (Exception) { return false; }
    }

    internal static double? Capture()
    {
        if (getMapComponent is null || presentGuests is null || guestCompType is null ||
            hasAssignedBed is null || assignedBed is null) return null;
        try
        {
            var distinct = new HospitalityAssignedBedCounter();
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                distinct.MarkObservedMap();
                var comp = getMapComponent.Invoke(map, null);
                if (comp is null || presentGuests.GetValue(comp) is not IEnumerable guests)
                    return null;
                foreach (var visitor in guests)
                {
                    if (visitor is not Pawn pawn || pawn.AllComps is null) return null;
                    var guestComponent = pawn.AllComps.FirstOrDefault(guestCompType.IsInstanceOfType);
                    if (guestComponent is null) return null;
                    if (hasAssignedBed.GetValue(guestComponent) is not bool occupied)
                        return null;
                    if (occupied && !distinct.Add(assignedBed.GetValue(guestComponent)))
                        return null;
                }
            }
            return distinct.Value;
        }
        catch (Exception) { return null; }
    }
}
