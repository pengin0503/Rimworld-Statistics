using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.UI;
using UnityEngine;
using Verse;

namespace RimworldStatistics.Patches;

/// <summary>
/// Adds the statistics button just before "Options" on the title screen (spec §9.1) and in the in-game pause menu.
/// The menu draws two lists (the main one and the web links); only the one holding "Options" gets the button.
/// Other mods' entries are kept, and automatic save import only starts on the title screen.
/// </summary>
[HarmonyPatch(typeof(OptionListingUtility), nameof(OptionListingUtility.DrawOptionListing))]
public static class MainMenuButtonPatch
{
    public static void Prefix(Rect rect, List<ListableOption> optList)
    {
        if (PatchRunner.IsDisabled(typeof(MainMenuButtonPatch)))
            return;
        try
        {
            var state = Current.ProgramState;
            if (state is not (ProgramState.Entry or ProgramState.Playing) || optList is null)
                return;
            string options = "Options".Translate();
            string mine = "RWS_MenuButton".Translate();
            var index = -1;
            var hasMine = false;
            for (var i = 0; i < optList.Count; i++)
            {
                var label = optList[i]?.label;
                if (index < 0 && label == options)
                    index = i;
                if (label == mine)
                {
                    hasMine = true;
                    break;
                }
            }
            if (index < 0)
                return;
            if (hasMine)
            {
                if (state == ProgramState.Playing)
                    FitInGameMenuOptions(rect.height, optList);
                else if (state == ProgramState.Entry)
                    ImportRunner.AutoStartOnce();
                return;
            }

            Action open = state == ProgramState.Playing
                ? InGameStatisticsLauncher.OpenFromMenu
                : static () => Find.WindowStack.Add(new Dialog_RimworldStatistics());
            optList.Insert(index, new ListableOption(mine, open));
            if (state == ProgramState.Playing)
                FitInGameMenuOptions(rect.height, optList);

            if (state == ProgramState.Entry)
                ImportRunner.AutoStartOnce();
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(MainMenuButtonPatch), e);
        }
    }

    private static void FitInGameMenuOptions(float availableHeight, List<ListableOption> optList)
    {
        var fitted = MenuOptionHeightFitter.FitMinHeight(availableHeight, optList.Count);
        if (fitted >= MenuOptionHeightFitter.DefaultMinHeight)
            return;
        for (var i = 0; i < optList.Count; i++)
        {
            var opt = optList[i];
            if (opt is not null && opt.GetType() == typeof(ListableOption) && opt.minHeight > fitted)
                opt.minHeight = fitted;
        }
    }
}
