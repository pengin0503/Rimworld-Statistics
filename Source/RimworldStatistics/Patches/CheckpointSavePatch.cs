using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;

namespace RimworldStatistics.Patches;

/// <summary>
/// The save scope of the checkpoint (numeric expansion design §3 step 2): the prefix prepares this save's checkpoint, the
/// component writes it, <see cref="SaveCompletedPatch"/> commits it after the save succeeded, and the finalizer ends the scope
/// whatever happened, passing any exception on unchanged. Applied only while at least one family records.
/// </summary>
[MetricAdapter(MetricAdapters.CheckpointUnit)]
[HarmonyPatch(typeof(SafeSaver), nameof(SafeSaver.Save))]
public static class CheckpointSavePatch
{
    public static void Prefix(string documentElementName)
    {
        if (documentElementName != CheckpointSaveSession.SaveGameElement || PatchRunner.IsDisabled(typeof(CheckpointSavePatch))
            || PatchRunner.IsDisabled(typeof(SaveCompletedPatch)))
            return;
        try
        {
            LiveMetrics.FlushMissed();
            LiveMetrics.BeforeSave();
            LiveMetrics.Session?.Enter(documentElementName, Environment.CurrentManagedThreadId, UnityData.IsInMainThread,
                Find.TickManager.TicksGame, DateTime.UtcNow, () => Guid.NewGuid().ToString("N"));
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(CheckpointSavePatch), e);
        }
    }

    public static Exception? Finalizer(Exception? __exception, string documentElementName)
    {
        try
        {
            LiveMetrics.Session?.Exit(documentElementName, Environment.CurrentManagedThreadId);
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(CheckpointSavePatch), e);
        }
        return __exception;
    }

    /// <summary>The postfix part: the committed draft of this save, or null (called by <see cref="SaveCompletedPatch"/>).</summary>
    internal static SaveDraft? Complete(string path, string documentElementName, bool runOriginal)
    {
        if (PatchRunner.IsDisabled(typeof(CheckpointSavePatch)))
            return null;
        try
        {
            return LiveMetrics.Session?.Complete(documentElementName, Environment.CurrentManagedThreadId, runOriginal, File.Exists(path));
        }
        catch (Exception e)
        {
            PatchRunner.Disable(typeof(CheckpointSavePatch), e);
            return null;
        }
    }

    /// <summary>The text the component writes into this save; null leaves what was loaded (see the component).</summary>
    internal static bool TryGetText(out string? text)
    {
        text = null;
        if (PatchRunner.IsDisabled(typeof(CheckpointSavePatch)) || LiveMetrics.Session is not { } session)
            return false;
        text = session.TextToWrite(Environment.CurrentManagedThreadId);
        return true;
    }
}
