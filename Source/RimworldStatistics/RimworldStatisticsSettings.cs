using Verse;
using RimworldStatistics.Core;

namespace RimworldStatistics;

/// <summary>MOD settings (spec §9.3), stored by the game in its Config folder.</summary>
public sealed class RimworldStatisticsSettings : ModSettings
{
    public bool includeDevMode = true;
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
    // Processing resources. The defaults keep exactly what the MOD did before these settings existed; they change only how
    // much work runs at once and what is kept in memory, never what is read, recorded or shown.
    public int importThreadMode = (int)ImportThreadMode.Standard;
    public int importThreadsManual = DefaultManualImportThreads;
    public bool precomputeStatistics = false;
    public int memoryUseMode = (int)MemoryUseMode.Standard;
    // S20: semicolon-separated save file name patterns kept out of every import. Lives in MOD settings, not in
    // profile.json, so restoring a profile does not restore an old ignore list.
    public string importExclusionPatterns = "";

    /// <summary>The parsed, trimmed and deduplicated exclusion patterns (<see cref="SaveNameExclusions"/>).</summary>
    public IReadOnlyList<string> ImportExclusionPatterns
        => SaveNameExclusions.Normalize((importExclusionPatterns ?? "").Split(';'));

    /// <summary>Adds an exact file name to the ignore list; the caller writes the settings.</summary>
    public void AddImportExclusion(string fileName)
    {
        if (!ImportEngine.IsSaveFile(fileName))
            return;
        importExclusionPatterns = string.Join("; ", SaveNameExclusions.Normalize(
            (importExclusionPatterns ?? "").Split(';').Append(fileName)));
    }

    /// <summary>What the manual thread count starts at: the standard count of this PC, so choosing Manual changes nothing yet.</summary>
    public static int DefaultManualImportThreads => ResourcePolicy.StandardImportThreads(Environment.ProcessorCount);

    public ImportThreadMode ImportThreads => ResourcePolicy.ParseImportThreadMode(importThreadMode);

    public MemoryUseMode MemoryUse => ResourcePolicy.ParseMemoryUseMode(memoryUseMode);

    /// <summary>The cache sizes these settings stand for.</summary>
    public CacheLimits CacheLimits => ResourcePolicy.Caches(MemoryUse, precomputeStatistics);

    private ExperimentalSelection? cachedDesiredSelection;
    private bool cachedDesiredEnabled;
    private string[]? cachedDesiredUnitIds;

    /// <summary>User's desired acquisition choices; UI edits are applied to the current game immediately.</summary>
    public ExperimentalSelection DesiredSelection
    {
        get
        {
            var units = experimentalUnitIds;
            if (cachedDesiredSelection is { } cached
                && cachedDesiredEnabled == experimentalEnabled
                && cachedDesiredUnitIds is { } cachedUnits
                && SequenceEquals(units, cachedUnits))
            {
                return cached;
            }
            var copied = units is { Count: > 0 } ? units.ToArray() : Array.Empty<string>();
            cachedDesiredEnabled = experimentalEnabled;
            cachedDesiredUnitIds = copied;
            cachedDesiredSelection = new ExperimentalSelection(experimentalEnabled, copied);
            return cachedDesiredSelection;
        }
    }

    private static bool SequenceEquals(List<string>? list, string[] array)
    {
        var count = list?.Count ?? 0;
        if (count != array.Length)
            return false;
        for (var i = 0; i < count; i++)
            if (!string.Equals(list![i], array[i], StringComparison.Ordinal))
                return false;
        return true;
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Values.Look(ref includeDevMode, "includeDevMode", true);
        Scribe_Values.Look(ref autoImport, "autoImport", true);
        Scribe_Values.Look(ref windowSizePreset, "windowSizePreset", (int)StatisticsWindowSizePreset.Standard);
        Scribe_Values.Look(ref experimentalEnabled, "experimentalEnabled", true);
        Scribe_Collections.Look(ref experimentalUnitIds, "experimentalUnitIds", LookMode.Value);
        Scribe_Values.Look(ref showRecordedExperimental, "showRecordedExperimental", false);
        Scribe_Values.Look(ref viewPreferencesJson, "viewPreferencesJson", "");
        Scribe_Values.Look(ref importThreadMode, "importThreadMode", (int)ImportThreadMode.Standard);
        Scribe_Values.Look(ref importThreadsManual, "importThreadsManual", DefaultManualImportThreads);
        Scribe_Values.Look(ref precomputeStatistics, "precomputeStatistics", false);
        Scribe_Values.Look(ref memoryUseMode, "memoryUseMode", (int)MemoryUseMode.Standard);
        Scribe_Values.Look(ref importExclusionPatterns, "importExclusionPatterns", "");
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            if (!Enum.IsDefined(typeof(StatisticsWindowSizePreset), windowSizePreset))
                windowSizePreset = (int)StatisticsWindowSizePreset.Standard;
            importThreadMode = (int)ResourcePolicy.ParseImportThreadMode(importThreadMode);
            memoryUseMode = (int)ResourcePolicy.ParseMemoryUseMode(memoryUseMode);
            importThreadsManual = Math.Clamp(importThreadsManual, 1, ResourcePolicy.MaxImportThreads);
            viewPreferencesJson ??= "";
            importExclusionPatterns ??= "";
            if (experimentalUnitIds is null)
            {
                experimentalUnitIds = experimentalEnabled
                    ? new List<string> { ExperimentalAcquisitionPolicy.AllUnitsToken }
                    : new List<string>();
            }
            else
            {
                experimentalUnitIds = experimentalUnitIds
                    .Where(id => !string.IsNullOrWhiteSpace(id))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
            }
            cachedDesiredSelection = null;
            cachedDesiredUnitIds = null;
        }
    }
}
