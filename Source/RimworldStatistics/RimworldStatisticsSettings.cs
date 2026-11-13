using Verse;
using RimworldStatistics.Core;

namespace RimworldStatistics;

/// <summary>MOD settings (spec §9.3), stored by the game in its Config folder.</summary>
public sealed class RimworldStatisticsSettings : ModSettings
{
    public bool includeDevMode = false;
    public bool autoImport = true;
    // Saved in RimWorld MOD settings; applied the next time the statistics dialog opens.
    public int windowSizePreset = (int)StatisticsWindowSizePreset.Standard;
    // New installs record every supported metric family by default.
    // Existing serialized selections (including explicit opt-outs) are retained.
    public bool experimentalEnabled = true;
    public List<string> experimentalUnitIds = new() { ExperimentalAcquisitionPolicy.AllUnitsToken };
    // Legacy persisted setting: visibility is now unconditional; retain the serialized key for compatibility.
    public bool showRecordedExperimental = false;
    // UI presentation choices only: groups and favourites never enter profile.json.
    public string viewPreferencesJson = "";

    /// <summary>User's desired choices, applied only after restarting RimWorld.</summary>
    public ExperimentalSelection DesiredSelection => new(experimentalEnabled, experimentalUnitIds.ToArray());


    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref includeDevMode, "includeDevMode", false);
        Scribe_Values.Look(ref autoImport, "autoImport", true);
        Scribe_Values.Look(ref windowSizePreset, "windowSizePreset", (int)StatisticsWindowSizePreset.Standard);
        Scribe_Values.Look(ref experimentalEnabled, "experimentalEnabled", true);
        Scribe_Collections.Look(ref experimentalUnitIds, "experimentalUnitIds", LookMode.Value);
        Scribe_Values.Look(ref showRecordedExperimental, "showRecordedExperimental", false);
        Scribe_Values.Look(ref viewPreferencesJson, "viewPreferencesJson", "");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            experimentalUnitIds ??= experimentalEnabled
                ? new List<string> { ExperimentalAcquisitionPolicy.AllUnitsToken }
                : new List<string>();
    }
}
