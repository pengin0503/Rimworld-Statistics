using HarmonyLib;
using Verse;

namespace RimworldStatistics.Patches;

/// <summary>
/// Applies the presentation settings a finished backup restore brought back (#325) on the main thread. The game calls
/// <see cref="UIRoot.UIRootUpdate"/> every frame on the title screen and in play (both roots call the base method), whatever
/// window is open, so the settings arrive even when the statistics window was closed while the restore ran.
/// </summary>
[HarmonyPatch(typeof(UIRoot), nameof(UIRoot.UIRootUpdate))]
public static class RestoredPreferencesPatch
{
    public static void Postfix()
    {
        // Nothing waits almost every frame: a volatile read, before the patch-state lock.
        if (!ProfileService.RestoredPreferencesPending || PatchRunner.IsDisabled(typeof(RestoredPreferencesPatch)))
            return;
        try
        {
            ProfileService.ApplyRestoredPreferences();
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(RestoredPreferencesPatch), e);
        }
    }
}
