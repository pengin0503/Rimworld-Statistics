using HarmonyLib;
using Verse;

namespace RimworldStatistics.Patches;

/// <summary>
/// Polls background precomputation (<see cref="PrecomputeService"/>) on the main thread. The game calls
/// <see cref="UIRoot.UIRootUpdate"/> every frame on the title screen and in play, whatever window is open. With the setting
/// off (the default) the postfix returns after reading the setting.
/// </summary>
[HarmonyPatch(typeof(UIRoot), nameof(UIRoot.UIRootUpdate))]
public static class PrecomputePatch
{
    public static void Postfix()
    {
        if (!PrecomputeService.Wanted || PatchRunner.IsDisabled(typeof(PrecomputePatch)))
            return;
        try
        {
            PrecomputeService.Poll();
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(PrecomputePatch), e);
        }
    }
}
