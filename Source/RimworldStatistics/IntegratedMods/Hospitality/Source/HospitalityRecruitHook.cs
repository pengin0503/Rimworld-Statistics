using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Metrics;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.HospitalityAddon;

/// <summary>Counts successful Hospitality Recruit callbacks; never counts UI clicks.</summary>
internal static class HospitalityRecruitHook
{
    private const string Unit = "mod.orion.hospitality.recruits";
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
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] Hospitality recruit binding failed ({e.GetType().Name}): {e.Message}",
                "RWS.hospitality.recruit.bind".GetHashCode());
            return false;
        }
    }

    internal static bool TryInstall()
    {
        if (recruit is null) return false;
        try
        {
            var patcher = new ScopedHarmonyPatcher(new Harmony("pengin0503.rws.hospitalityaddon.recruits"));
            patcher.Patch(recruit, typeof(HospitalityRecruitHook), postfix: nameof(OnRecruited));
            return true;
        }
        catch (Exception e)
        {
            Log.WarningOnce(
                $"[RimworldStatistics] Hospitality recruit patch failed ({e.GetType().Name}): {e.Message}",
                "RWS.hospitality.recruit.install".GetHashCode());
            return false;
        }
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
            if (!GameFacts.IsPlayerFaction(guest.Faction) || guest.MapHeld is not { IsPlayerHome: true })
                return;
            var id = HospitalityRecruitEventId.Create(guest.GetUniqueLoadID(),
                Find.TickManager.TicksGame);
            if (id is not null)
                HospitalitySdk.RecordRecruitment(id, forced);
        }
        catch (Exception e) { LiveMetrics.Fail(Unit, e); }
    }
}
