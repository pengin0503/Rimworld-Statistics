using RimworldStatistics.Core;
using RimWorld;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Reads the running game into the same shapes SaveReader produces from a file (research §5), so a save observed
/// both ways yields the same fingerprint. Call it on the thread that runs the game's own logic at that moment: the
/// main thread in play, or the long-event thread while a game loads or starts. Never from the import thread.
/// </summary>
public static class LiveStateReader
{
    public static string GameVersion => VersionControl.CurrentVersionStringWithRev;

    public static ColonyIdentity ReadIdentity(string? campaignId)
    {
        var info = Find.World?.info;
        var game = Find.GameInfo;
        var wealth = WealthRecords();
        return new ColonyIdentity
        {
            SeedString = info?.seedString,
            PersistentRandomValue = info?.persistentRandomValue ?? 0,
            // PlanetTile.ToString() is "tileId,layerId", the 1.6+ save format.
            StartingTile = game.startingTile.Valid ? ColonyIdentity.NormalizeTile(game.startingTile.ToString()) : null,
            GameStartAbsTick = Find.TickManager.gameStartAbsTick,
            FirstWealthBits = wealth is { Count: > 0 } && IsValidWealth(wealth[0]) ? BitConverter.SingleToUInt32Bits(wealth[0]) : null,
            StartingPawnIds = game.startingAndOptionalPawns.Where(p => p != null).Select(p => p.GetUniqueLoadID()).ToList(),
            ModCampaignId = campaignId,
        };
    }

    public static ObservedValues ReadValues()
    {
        var sr = Find.StoryWatcher.statsRecord;
        var wealth = WealthRecords();
        if (wealth != null && !wealth.All(IsValidWealth))
            wealth = null;
        var realPlayTime = Find.GameInfo.RealPlayTimeInteracting;
        return new ObservedValues
        {
            TicksGame = Find.TickManager.TicksGame,
            RealPlayTime = double.IsFinite(realPlayTime) && realPlayTime >= 0 ? realPlayTime : 0,
            NumRaidsEnemy = sr.numRaidsEnemy,
            NumThreatsQueued = sr.numThreatBigs,   // saved under the label "numThreatsQueued"
            ColonistsKilled = sr.colonistsKilled,
            ColonistsLaunched = sr.colonistsLaunched,
            GreatestPopulation = sr.greatestPopulation,
            WealthMax = wealth is { Count: > 0 } ? wealth.Max() : 0,
            WealthLast = wealth is { Count: > 0 } ? wealth[^1] : 0,
            WealthPoints = wealth?.Count ?? 0,
        };
    }

    public static DisplayInfo ReadDisplay()
    {
        var player = Faction.OfPlayerSilentFail;
        var storyteller = Find.Storyteller;
        return new DisplayInfo
        {
            // Faction.Name falls back to the def label when unnamed; a save has no name then, so match that.
            FactionName = player is { HasName: true } ? player.Name : null,
            WorldName = Find.World?.info?.name,
            Scenario = Find.Scenario?.name,
            Storyteller = storyteller?.def?.defName,
            Difficulty = storyteller?.difficultyDef?.defName,
            Permadeath = Find.GameInfo.permadeathMode,
        };
    }

    /// <param name="lineageTypes">Types in this save's lineageEndings; empty when a pre-install save is first loaded.</param>
    public static TraceInputs ReadTraceInputs(IReadOnlyCollection<EndingType> lineageTypes)
    {
        var quests = Find.QuestManager.QuestsListForReading;
        bool Succeeded(string root) => quests.Any(q => q?.root?.defName == root && q.State == QuestState.EndedSuccess);
        return new TraceInputs
        {
            ColonistsLaunched = Find.StoryWatcher.statsRecord.colonistsLaunched,
            RoyalAscentSucceeded = Succeeded("EndGame_RoyalAscent"),
            MechhiveSucceeded = Succeeded("Gravcore_Mechhive"),
            MechanoidsDeactivated = Faction.OfMechanoids?.deactivated ?? false,
            AnomalyLevelDef = Find.Anomaly?.LevelDef?.defName,
            LineageTypes = lineageTypes,
        };
    }

    private static bool IsValidWealth(float value) => float.IsFinite(value) && value >= 0;

    /// <summary>The Wealth_Total history series (all player home maps), or null if the recorder is missing.</summary>
    private static List<float>? WealthRecords()
    {
        var groups = Find.History?.Groups();
        if (groups is null)
            return null;
        foreach (var group in groups)
            foreach (var recorder in group.recorders)
                if (recorder?.def?.defName == "Wealth_Total")
                    return recorder.records;
        return null;
    }
}
