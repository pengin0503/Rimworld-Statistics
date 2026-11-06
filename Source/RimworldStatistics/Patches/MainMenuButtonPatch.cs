using HarmonyLib;
using RimworldStatistics.UI;
using Verse;

namespace RimworldStatistics.Patches;

/// <summary>
/// Adds the statistics button just before "Options" on the title screen (spec §9.1). The menu draws two lists
/// (the main one and the web links); only the one holding "Options" gets the button. Other mods' entries are kept.
/// </summary>
[HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
public static class MainMenuButtonPatch
{
    public static void Prefix(List<ListableOption> optList)
    {
        if (PatchRunner.IsDisabled(typeof(MainMenuButtonPatch)))
            return;
        try
        {
            if (Current.ProgramState != ProgramState.Entry || optList is null)
                return;
            string options = "Options".Translate();
            string mine = "RWS_MenuButton".Translate();
            var index = optList.FindIndex(o => o?.label == options);
            if (index < 0 || optList.Exists(o => o?.label == mine))
                return;
            ImportRunner.AutoStartOnce();
            optList.Insert(index, new ListableOption(mine, () => Find.WindowStack.Add(new Dialog_RimworldStatistics())));
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(MainMenuButtonPatch), e);
        }
    }
}
