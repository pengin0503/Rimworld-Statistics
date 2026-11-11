using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI.Group;

namespace RimworldStatistics.HospitalityAddon;

/// <summary>
/// Exact-shape optional hook for the 1.7 Hospitality (Continued) arrival callback.
/// No target DLL reference, per-tick scanning, or pre-install lifetime reconstruction.
/// </summary>
internal static class HospitalityArrival
{
    private const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
    private static MethodInfo? onLordSpawned;
    private static MethodInfo? lordIdMethod;
    private static Type? visitJobType;
    private static FieldInfo? recoveredOnMap;

    internal static bool TryBind()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, "Hospitality", StringComparison.Ordinal));
            var guests = asm?.GetType("Hospitality.Utilities.GuestUtility", throwOnError: false);
            var visit = asm?.GetType("Hospitality.LordJob_VisitColony", throwOnError: false);
            var method = guests?.GetMethod("OnLordSpawned", BindingFlags.Public | BindingFlags.Static,
                binder: null, types: new[] { typeof(Lord) }, modifiers: null);
            var id = typeof(Lord).GetMethod("GetUniqueLoadID", PublicInstance, binder: null,
                types: Type.EmptyTypes, modifiers: null);
            var recovered = visit?.GetField("recoveredOnMap", PublicInstance);
            if (method is null || method.ReturnType != typeof(void) ||
                visit is null || !typeof(LordJob).IsAssignableFrom(visit) ||
                recovered?.FieldType != typeof(bool) || id?.ReturnType != typeof(string))
                return false;

            onLordSpawned = method;
            visitJobType = visit;
            recoveredOnMap = recovered;
            lordIdMethod = id;
            return true;
        }
        catch (Exception) { return false; }
    }

    internal static bool TryInstall()
    {
        if (onLordSpawned is null) return false;
        try
        {
            var postfix = typeof(HospitalityArrival).GetMethod(nameof(AfterLordSpawned),
                BindingFlags.NonPublic | BindingFlags.Static);
            if (postfix is null) return false;
            new Harmony("pengin0503.rws.hospitalityaddon.visits").Patch(
                onLordSpawned, postfix: new HarmonyMethod(postfix));
            return true;
        }
        catch (Exception) { return false; }
    }

    // Invoked only by a confirmed target callback, not on a timer or save snapshot.
    private static void AfterLordSpawned(Lord lord)
    {
        if (!HospitalitySdk.EventsConnected || lord is null ||
            Scribe.mode != LoadSaveMode.Inactive || Current.ProgramState != ProgramState.Playing)
            return;
        try
        {
            if (lord.Map is not { IsPlayerHome: true } map ||
                lord.LordJob is not { } job || visitJobType is null ||
                !visitJobType.IsInstanceOfType(job) ||
                recoveredOnMap?.GetValue(job) is not false)
                return;

            // A recovered visitor assigned a temporary lord must not be counted
            // as an arrival; the upstream 1.7 source marks recoveredOnMap=true.
            var groupSize = lord.ownedPawns?.Count ?? 0;
            if (groupSize <= 0) return;
            var id = HospitalityVisitEventId.Create(map.uniqueID, lordIdMethod?.Invoke(lord, null) as string);
            if (id is null) return;
            HospitalitySdk.RecordArrival(id, groupSize);
        }
        catch (Exception) { /* Optional source changes must not break the game. */ }
    }
}
