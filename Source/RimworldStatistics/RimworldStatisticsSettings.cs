using Verse;
using RimworldStatistics.Core;

namespace RimworldStatistics;

/// <summary>MOD settings (spec §9.3), stored by the game in its Config folder.</summary>
public sealed class RimworldStatisticsSettings : ModSettings
{
    public bool includeDevMode = false;
    public bool autoImport = true;
    public bool experimentalEnabled = false;
    public List<string> experimentalUnitIds = new();
    // Legacy persisted setting: visibility is now unconditional; retain the serialized key for compatibility.
    public bool showRecordedExperimental = false;

    /// <summary>User's desired choices, applied only after restarting RimWorld.</summary>
    public ExperimentalSelection DesiredSelection => new(experimentalEnabled, experimentalUnitIds.ToArray());


    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref includeDevMode, "includeDevMode", false);
        Scribe_Values.Look(ref autoImport, "autoImport", true);
        Scribe_Values.Look(ref experimentalEnabled, "experimentalEnabled", false);
        Scribe_Collections.Look(ref experimentalUnitIds, "experimentalUnitIds", LookMode.Value);
        Scribe_Values.Look(ref showRecordedExperimental, "showRecordedExperimental", false);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
            experimentalUnitIds ??= new List<string>();
    }
}
