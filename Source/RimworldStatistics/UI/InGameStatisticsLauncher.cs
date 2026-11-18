using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Opens in-game colony summary, chronicle, or full lifetime statistics windows on the main thread
/// without generating a campaign id, scanning save files, or triggering a game save.
/// </summary>
internal static class InGameStatisticsLauncher
{
    public static void OpenFromMenu()
    {
        if (Current.ProgramState == ProgramState.Playing)
            Find.MainTabsRoot?.EscapeCurrentTab(playSound: false);

        var (campaignId, fallbackFactionName, fallbackWorldName) = ReadCurrentCampaignContext();
        PrepareProfileSession();

        if (Find.WindowStack?.WindowOfType<Dialog_ColonySummaryReport>() is { } existing)
        {
            Find.WindowStack.Notify_ManuallySetFocus(existing);
            return;
        }

        Find.WindowStack?.Add(Dialog_ColonySummaryReport.CreateForInGame(campaignId, fallbackFactionName, fallbackWorldName));
    }

    public static void OpenAllStatistics()
    {
        if (Current.ProgramState == ProgramState.Playing)
            Find.MainTabsRoot?.EscapeCurrentTab(playSound: false);

        PrepareProfileSession();

        if (Find.WindowStack?.WindowOfType<Dialog_RimworldStatistics>() is { } existing)
        {
            Find.WindowStack.Notify_ManuallySetFocus(existing);
            return;
        }

        Find.WindowStack?.Add(new Dialog_RimworldStatistics());
    }

    public static void OpenCurrentChronicle(StatsView view, string colonyKey)
    {
        if (Find.WindowStack?.WindowOfType<Dialog_ColonyChronicle>() is { } existing)
            existing.Close(doCloseSound: false);

        Find.WindowStack?.Add(new Dialog_ColonyChronicle(view, colonyKey));
    }

    private static (string? CampaignId, string? FallbackFactionName, string? FallbackWorldName) ReadCurrentCampaignContext()
    {
        var campaignId = GameComponent_RimworldStatistics.CurrentOrNull?.campaignId;
        string? fallbackFactionName = null;
        string? fallbackWorldName = null;
        try
        {
            var display = LiveStateReader.ReadDisplay();
            fallbackFactionName = display.FactionName;
            fallbackWorldName = display.WorldName;
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Could not read live colony display info: {e}", "RWS.ingame.display".GetHashCode());
        }
        return (campaignId, fallbackFactionName, fallbackWorldName);
    }

    private static void PrepareProfileSession()
    {
        try
        {
            ProfileService.PrepareForDialog();
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Could not start loading statistics: {e}", "RWS.prepare".GetHashCode());
        }
    }
}
