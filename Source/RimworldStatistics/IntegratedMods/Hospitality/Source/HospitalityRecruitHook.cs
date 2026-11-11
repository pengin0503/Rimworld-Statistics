using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimworldStatistics.HospitalityAddon;

/// <summary>Counts successful Hospitality Recruit callbacks; never counts UI clicks.</summary>
internal static class HospitalityRecruitHook
{
    private static MethodInfo? recruit;

    internal static bool TryBind()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, "Hospitality", StringComparison.Ordinal));
            var type = asm?.GetType("Hospitality.Utilities.GuestUtility", false);
            var method = type?.GetMethod("Recruit", BindingFlags.Public | BindingFlags.Static,
                binder: null, types: new[] { typeof(Pawn), typeof(int), typeof(bool) }, modifiers: null);
            if (method is null || method.ReturnType != typeof(void)) return false;
            recruit = method;
            return true;
        }
        catch (Exception) { return false; }
    }

    internal static bool TryInstall()
    {
        if (recruit is null) return false;
        try
        {
            var postfix = typeof(HospitalityRecruitHook).GetMethod(nameof(OnRecruited),
                BindingFlags.NonPublic | BindingFlags.Static);
            if (postfix is null) return false;
            new Harmony("pengin0503.rws.hospitalityaddon.recruits")
                .Patch(recruit, postfix: new HarmonyMethod(postfix));
            return true;
        }
        catch (Exception) { return false; }
    }

    private static void OnRecruited(Pawn guest, bool forced)
    {
        if (!HospitalitySdk.RecruitmentConnected || guest is null ||
            Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
            return;
        try
        {
            // Upstream RecruitingSuccess called by Recruit() changes the pawn faction
            // on successful adoption; the guarded postfix fires after it returns.
            if (guest.Faction != Faction.OfPlayer || guest.MapHeld is not { IsPlayerHome: true })
                return;
            var id = HospitalityRecruitEventId.Create(guest.GetUniqueLoadID(),
                Find.TickManager.TicksGame);
            if (id is not null)
                HospitalitySdk.RecordRecruitment(id, forced);
        }
        catch (Exception) { /* Optional target incompatibility must not break RimWorld. */ }
    }
}
