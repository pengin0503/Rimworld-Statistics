using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.UI;
using UnityEngine;
using Verse;

namespace RimworldStatistics.Patches;

/// <summary>
/// Handles keyboard shortcuts on the title screen after <see cref="WindowStack.WindowStackOnGUI"/> has run,
/// so focused text fields and key-rebinding dialogs consume the key event first.
/// </summary>
[HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.UIRootOnGUI))]
public static class UIRootEntryShortcutPatch
{
    public static void Postfix()
    {
        if (PatchRunner.IsDisabled(typeof(UIRootEntryShortcutPatch)))
            return;
        try
        {
            StatisticsShortcutHandler.HandleOnGUI();
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(UIRootEntryShortcutPatch), e);
        }
    }
}

/// <summary>
/// Handles keyboard shortcuts during gameplay after <see cref="WindowStack.WindowStackOnGUI"/> has run.
/// </summary>
[HarmonyPatch(typeof(UIRoot_Play), nameof(UIRoot_Play.UIRootOnGUI))]
public static class UIRootPlayShortcutPatch
{
    public static void Postfix()
    {
        if (PatchRunner.IsDisabled(typeof(UIRootPlayShortcutPatch)))
            return;
        try
        {
            StatisticsShortcutHandler.HandleOnGUI();
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(UIRootPlayShortcutPatch), e);
        }
    }
}

internal static class StatisticsShortcutHandler
{
    public static void HandleOnGUI()
    {
        var ev = Event.current;
        if (ev is null || ev.type != EventType.KeyDown || ev.keyCode == KeyCode.None)
            return;

        StatisticsShortcutKind? triggered = null;
        if (RimworldStatisticsKeyBindingDefOf.ToggleLifetimeStatistics?.KeyDownEvent == true)
            triggered = StatisticsShortcutKind.LifetimeStatistics;
        else if (RimworldStatisticsKeyBindingDefOf.ToggleStatistics?.KeyDownEvent == true)
            triggered = StatisticsShortcutKind.ContextDefault;

        if (triggered is not { } kind)
            return;

        var windowStack = Find.WindowStack;
        if (windowStack is null)
            return;

        var mode = Current.ProgramState switch
        {
            ProgramState.Entry => StatisticsShortcutProgramMode.Entry,
            ProgramState.Playing => StatisticsShortcutProgramMode.Playing,
            _ => StatisticsShortcutProgramMode.Other,
        };

        Window? topStatWindow = null;
        var windows = windowStack.Windows;
        for (var i = windows.Count - 1; i >= 0; i--)
        {
            if (IsStatisticsWindow(windows[i]))
            {
                topStatWindow = windows[i];
                break;
            }
        }

        var hasTextFocus = GUIUtility.keyboardControl != 0 || windowStack.AnySearchWidgetFocused;
        var cutscene = mode == StatisticsShortcutProgramMode.Playing && WorldComponent_GravshipController.CutsceneInProgress;
        var otherBlocking = mode == StatisticsShortcutProgramMode.Entry
            ? windowStack.NonImmediateDialogWindowOpen
            : windowStack.AnyWindowAbsorbingAllInput;

        var context = new StatisticsShortcutContext(
            ProgramMode: mode,
            HasKeyboardTextFocus: hasTextFocus,
            LongEventPending: LongEventHandler.AnyEventNowOrWaiting,
            CutsceneInProgress: cutscene,
            AnyStatisticsWindowOpen: topStatWindow is not null,
            TopStatisticsWindowGetsInput: topStatWindow is not null && windowStack.GetsInput(topStatWindow),
            OtherBlockingDialogOpen: otherBlocking);

        var action = StatisticsShortcutPolicy.Resolve(kind, context);
        if (action == StatisticsShortcutAction.None)
            return;

        ev.Use();
        switch (action)
        {
            case StatisticsShortcutAction.CloseStatisticsWindows:
                CloseAllStatisticsWindows(windowStack);
                break;
            case StatisticsShortcutAction.OpenTitleStatistics:
                ImportRunner.AutoStartOnce();
                windowStack.Add(new Dialog_RimworldStatistics());
                break;
            case StatisticsShortcutAction.OpenInGameSummary:
                InGameStatisticsLauncher.OpenFromMenu();
                break;
            case StatisticsShortcutAction.OpenInGameAllStatistics:
                InGameStatisticsLauncher.OpenAllStatistics();
                break;
        }
    }

    internal static bool IsStatisticsWindow(Window? window) => window is
        Dialog_RimworldStatistics
        or Dialog_ColonySummaryReport
        or Dialog_ColonyChronicle
        or Dialog_ColonySaveLineage
        or Dialog_MetricChart
        or Dialog_ColonyMetricHistory
        or Dialog_MetricScatterPlot
        or Dialog_ColonyGroups
        or Dialog_HistoricalCounterIntervals
        or ColonyPickerDialog
        or MetricPickerDialog;

    private static void CloseAllStatisticsWindows(WindowStack windowStack)
    {
        var toClose = new List<Window>();
        var windows = windowStack.Windows;
        for (var i = windows.Count - 1; i >= 0; i--)
        {
            if (IsStatisticsWindow(windows[i]))
                toClose.Add(windows[i]);
        }
        for (var i = 0; i < toClose.Count; i++)
            toClose[i].Close(doCloseSound: i == 0);
    }
}
