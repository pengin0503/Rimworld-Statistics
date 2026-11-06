using Verse;

namespace RimworldStatistics;

/// <summary>MOD settings (spec §9.3), stored by the game in its Config folder.</summary>
public sealed class RimworldStatisticsSettings : ModSettings
{
    public bool includeDevMode = false;
    public bool autoImport = true;

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref includeDevMode, "includeDevMode", false);
        Scribe_Values.Look(ref autoImport, "autoImport", true);
    }
}
