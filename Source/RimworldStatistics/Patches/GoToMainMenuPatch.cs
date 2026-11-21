using HarmonyLib;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches;

/// <summary>Writes pending record changes and releases live game references when returning to the title screen (spec §6.3).</summary>
[HarmonyPatch(typeof(GenScene), nameof(GenScene.GoToMainMenu))]
public static class GoToMainMenuPatch
{
    public static void Prefix()
    {
        if (PatchRunner.IsDisabled(typeof(GoToMainMenuPatch)))
            return;
        try
        {
            ProfileService.Session.Flush();
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(GoToMainMenuPatch), e);
        }
        finally
        {
            LiveMetrics.ClearForMainMenu();
        }
    }
}
