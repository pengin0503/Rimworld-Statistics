using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Opens the in-game colony summary report on the main thread without generating a campaign id,
/// scanning save files, or triggering a game save.
/// </summary>
internal static class InGameStatisticsLauncher
{
    public static void OpenFromMenu()
    {
        if (Current.ProgramState == ProgramState.Playing)
            Find.MainTabsRoot?.EscapeCurrentTab(playSound: false);

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

        try
        {
            ProfileService.PrepareForDialog();
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Could not start loading statistics: {e}", "RWS.prepare".GetHashCode());
        }

        Find.WindowStack.Add(Dialog_ColonySummaryReport.CreateForInGame(campaignId, fallbackFactionName, fallbackWorldName));
    }
}
