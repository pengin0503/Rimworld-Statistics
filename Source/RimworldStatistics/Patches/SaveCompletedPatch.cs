using HarmonyLib;
using Verse;

namespace RimworldStatistics.Patches;

// GameDataSaveLoader catches save failures itself. SafeSaver returns only after the file is committed.
[HarmonyPatch(typeof(SafeSaver), nameof(SafeSaver.Save))]
public static class SaveCompletedPatch
{
    public static void Postfix(string path, string documentElementName, bool __runOriginal)
    {
        if (!__runOriginal || documentElementName != "savegame")
            return;
        try
        {
            GameComponent_RimworldStatistics.CurrentOrNull?.RecordSave(path);
            ImportRunner.GameSaveCompleted();
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Could not observe the completed save: {e}", "RWS.save.completed".GetHashCode());
        }
    }
}
