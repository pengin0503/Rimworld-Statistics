namespace RimworldStatistics.Core;

public enum StatisticsShortcutKind
{
    ContextDefault = 0,
    LifetimeStatistics = 1,
}

public enum StatisticsShortcutProgramMode
{
    Other = 0,
    Entry = 1,
    Playing = 2,
}

public enum StatisticsShortcutAction
{
    None = 0,
    CloseStatisticsWindows = 1,
    OpenTitleStatistics = 2,
    OpenInGameSummary = 3,
    OpenInGameAllStatistics = 4,
}

public readonly record struct StatisticsShortcutContext(
    StatisticsShortcutProgramMode ProgramMode,
    bool HasKeyboardTextFocus,
    bool LongEventPending,
    bool CutsceneInProgress,
    bool AnyStatisticsWindowOpen,
    bool TopStatisticsWindowGetsInput,
    bool OtherBlockingDialogOpen);

/// <summary>
/// Pure state policy for opening and closing Rimworld Statistics windows via keyboard shortcuts,
/// ensuring text fields, key-rebinding dialogs, loading transitions, and cutscenes are never interrupted.
/// </summary>
public static class StatisticsShortcutPolicy
{
    public static StatisticsShortcutAction Resolve(StatisticsShortcutKind kind, StatisticsShortcutContext context)
    {
        if (context.ProgramMode == StatisticsShortcutProgramMode.Other
            || context.HasKeyboardTextFocus
            || context.LongEventPending)
        {
            return StatisticsShortcutAction.None;
        }

        if (context.ProgramMode == StatisticsShortcutProgramMode.Playing && context.CutsceneInProgress)
            return StatisticsShortcutAction.None;

        if (context.AnyStatisticsWindowOpen)
        {
            return context.TopStatisticsWindowGetsInput
                ? StatisticsShortcutAction.CloseStatisticsWindows
                : StatisticsShortcutAction.None;
        }

        if (context.OtherBlockingDialogOpen)
            return StatisticsShortcutAction.None;

        return context.ProgramMode switch
        {
            StatisticsShortcutProgramMode.Entry => StatisticsShortcutAction.OpenTitleStatistics,
            StatisticsShortcutProgramMode.Playing => kind == StatisticsShortcutKind.LifetimeStatistics
                ? StatisticsShortcutAction.OpenInGameAllStatistics
                : StatisticsShortcutAction.OpenInGameSummary,
            _ => StatisticsShortcutAction.None,
        };
    }
}
