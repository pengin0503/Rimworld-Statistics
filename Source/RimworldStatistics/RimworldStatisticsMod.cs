using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Api;
using RimworldStatistics.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics;

public sealed class RimworldStatisticsMod : Mod
{
    public const string HarmonyId = "pengin0503.rimworldstatistics";

    internal static string InstalledVersion { get; private set; } = "?";

    public static RimworldStatisticsSettings Settings { get; private set; } = new();
    public static RimworldStatisticsMod? Instance { get; private set; }

    /// <summary>Last applied recording selection; the compatible patches remain installed.</summary>
    public static ExperimentalSelection ActiveAcquisitionSelection { get; private set; } =
        new(false, Array.Empty<string>());

    private static Vector2 experimentalScroll;
    private static AcquisitionUnit[]? selectableUnits;
    private static UnitSection[]? selectableSections;
    private static string[]? selectableUnitLabels;
    private static LoadedLanguage? selectableUnitLabelsLanguage;
    // U1: settings-only view state. It never enters the settings file, so collapsing or searching changes no choice.
    private static string unitSearch = "";
    private static readonly HashSet<string> collapsedUnitSections = new(StringComparer.Ordinal);

    internal enum UnitSectionKind
    {
        Events,
        Snapshots,
        Mod,
    }

    /// <summary>One collapsible block of the settings unit list: the events, the snapshots, or one optional MOD's units.</summary>
    internal sealed record UnitSection(string Key, UnitSectionKind Kind, int[] Indices, string? OwnerPackageId, string? OwnerUnitId);

    private static void ApplyLiveSelectionIfChanged()
    {
        var desired = Settings.DesiredSelection;
        if (desired.Enabled == ActiveAcquisitionSelection.Enabled
            && (ReferenceEquals(desired.ExplicitUnitIds, ActiveAcquisitionSelection.ExplicitUnitIds)
                || desired.ExplicitUnitIds.ToHashSet(StringComparer.Ordinal)
                    .SetEquals(ActiveAcquisitionSelection.ExplicitUnitIds)))
            return;

        // Runtime capture selection is separate from one-time Harmony patch
        // installation. Always update both built-in and optional MOD units.
        MetricGates.ConfigureProductionSelection(
            ExperimentalAcquisitionPolicy.Resolve(desired, AcquisitionUnits.All));
        MetricGates.ConfigureOptionalUnits(ModProviderAdapters.KnownUnits, desired);
        ActiveAcquisitionSelection = desired;
        LiveMetrics.RefreshSelection();
    }

    public RimworldStatisticsMod(ModContentPack content)
        : base(content)
    {
        Instance = this;
        Settings = GetSettings<RimworldStatisticsSettings>();
        try
        {
            InstalledVersion = System.Xml.Linq.XDocument.Load(Path.Combine(content.RootDir, "About", "About.xml"))
                .Root?.Element("modVersion")?.Value ?? "?";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            Log.Warning($"[RimworldStatistics] Could not read the installed mod version: {error.Message}");
        }
        ActiveAcquisitionSelection = Settings.DesiredSelection;
        MetricGates.ConfigureProductionSelection(
            ExperimentalAcquisitionPolicy.Resolve(ActiveAcquisitionSelection, AcquisitionUnits.All));
        if (!StatisticsApi.InstallHost(ExternalSdkHost.Instance))
            Log.Error("[RimworldStatistics] External SDK host could not be installed; SDK calls remain unavailable.");
        else
            IntegratedModStartup.Register(); // Before SDK registration freezes during startup.
        PatchRunner.ApplyAll(new Harmony(HarmonyId));
    }

    public override string SettingsCategory() => "RWS_SettingsCategory".Translate();

    private float settingsContentHeight;

    public override void DoSettingsWindowContents(Rect inRect)
    {
        const float rowHeight = 27f;
        var selectable = SelectableUnits();
        var sections = SelectableUnitSections;
        var unitLabels = UnitLabels(selectable);
        var viewRect = new Rect(0f, 0f, inRect.width - 20f, settingsContentHeight > 0
            ? settingsContentHeight : 440f + selectable.Length * rowHeight + sections.Length * 24f);
        Widgets.BeginScrollView(inRect, ref experimentalScroll, viewRect);
        try
        {
            var listing = new Listing_Standard();
            listing.Begin(viewRect);
            listing.CheckboxLabeled("RWS_Setting_IncludeDevMode".Translate(), ref Settings.includeDevMode);
            listing.CheckboxLabeled("RWS_Setting_AutoImport".Translate(), ref Settings.autoImport);
            listing.Gap(8f);
            listing.Label("RWS_Setting_ImportExclusions".Translate());
            Settings.importExclusionPatterns = Widgets.TextField(listing.GetRect(28f),
                Settings.importExclusionPatterns ?? "") ?? "";
            listing.Label("RWS_Setting_ImportExclusionsHelp".Translate());
            listing.Gap(8f);
            listing.Label("RWS_Setting_WindowSize".Translate());
            var sizeButton = listing.GetRect(30f);
            if (Widgets.ButtonText(new Rect(sizeButton.x, sizeButton.y, Math.Min(sizeButton.width, 320f), sizeButton.height),
                    WindowSizeLabel((StatisticsWindowSizePreset)Settings.windowSizePreset)))
            {
                Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
                {
                    new(WindowSizeLabel(StatisticsWindowSizePreset.Standard),
                        () => Settings.windowSizePreset = (int)StatisticsWindowSizePreset.Standard),
                    new(WindowSizeLabel(StatisticsWindowSizePreset.Large),
                        () => Settings.windowSizePreset = (int)StatisticsWindowSizePreset.Large),
                    new(WindowSizeLabel(StatisticsWindowSizePreset.ExtraLarge),
                        () => Settings.windowSizePreset = (int)StatisticsWindowSizePreset.ExtraLarge),
                    new(WindowSizeLabel(StatisticsWindowSizePreset.FitScreen),
                        () => Settings.windowSizePreset = (int)StatisticsWindowSizePreset.FitScreen),
                }));
            }
            listing.Label("RWS_Setting_WindowSizeHelp".Translate());
            listing.Gap(8f);
            DrawDisplayFormatSettings(listing);
            listing.Gap(8f);
            listing.Label("RWS_Setting_ShortcutsHeading".Translate());
            listing.Label("RWS_Setting_ShortcutsSummary".Translate(
                UI.RimworldStatisticsKeyBindingDefOf.DescribeBinding(UI.RimworldStatisticsKeyBindingDefOf.ToggleStatistics),
                UI.RimworldStatisticsKeyBindingDefOf.DescribeBinding(UI.RimworldStatisticsKeyBindingDefOf.ToggleLifetimeStatistics)));
            var keyBindButton = listing.GetRect(30f);
            if (Widgets.ButtonText(
                    new Rect(keyBindButton.x, keyBindButton.y, Math.Min(keyBindButton.width, 320f), keyBindButton.height),
                    "RWS_Setting_ConfigureKeyBindings".Translate()))
            {
                Find.WindowStack.Add(new RimWorld.Dialog_KeyBindings());
            }
            listing.Gap(8f);
            DrawResourceSettings(listing);
            listing.Gap(8f);
            listing.CheckboxLabeled("RWS_Exp_Enable".Translate(), ref Settings.experimentalEnabled);
            listing.Label("RWS_Exp_Warning".Translate());
            var buttons = listing.GetRect(28f);
            var width = (buttons.width - 18f) / 4f;
            if (Widgets.ButtonText(new Rect(buttons.x, buttons.y, width, buttons.height), "RWS_Exp_SelectAll".Translate()))
                Settings.experimentalUnitIds = new List<string> { ExperimentalAcquisitionPolicy.AllUnitsToken };
            if (Widgets.ButtonText(new Rect(buttons.x + width + 6f, buttons.y, width, buttons.height), "RWS_Exp_SelectNone".Translate()))
                Settings.experimentalUnitIds.Clear();
            if (Widgets.ButtonText(new Rect(buttons.x + (width + 6f) * 2f, buttons.y, width, buttons.height), "RWS_Exp_SelectEvents".Translate()))
                Settings.experimentalUnitIds = selectable
                    .Where(u => u.Kind == AcquisitionKind.Family && UnitRequirementsMet(u))
                    .Select(u => u.Id).ToList();
            // U1: only units that can actually record here, written as explicit IDs (not the wildcard).
            if (Widgets.ButtonText(new Rect(buttons.x + (width + 6f) * 3f, buttons.y, width, buttons.height),
                    "RWS_Exp_Recommended".Translate()))
                Settings.experimentalUnitIds = RecommendedUnitIds(selectable).ToList();
            listing.Gap(4f);
            listing.Label("RWS_Exp_Search".Translate());
            var nextSearch = Widgets.TextField(listing.GetRect(28f), unitSearch) ?? "";
            if (nextSearch != unitSearch)
                unitSearch = nextSearch;
            var term = unitSearch.Trim();
            var anyDrawn = false;
            foreach (var section in sections)
            {
                var matching = section.Indices
                    .Where(index => UnitSearch.Matches(unitLabels[index], selectable[index].Id, term))
                    .ToArray();
                if (term.Length > 0 && matching.Length == 0)
                    continue;
                anyDrawn = true;
                var ids = Settings.experimentalUnitIds;
                var sectionAutoAll = ids.Contains(ExperimentalAcquisitionPolicy.AllUnitsToken, StringComparer.Ordinal);
                var selectedCount = sectionAutoAll
                    ? section.Indices.Length
                    : section.Indices.Count(index => ids.Contains(selectable[index].Id, StringComparer.Ordinal));
                // A search always reveals its matches; the persisted collapse state is only used without a term.
                var collapsed = term.Length == 0 && collapsedUnitSections.Contains(section.Key);
                var headerRect = listing.GetRect(24f);
                Widgets.DrawHighlightIfMouseover(headerRect);
                Widgets.Label(headerRect, (collapsed ? "▸ " : "▾ ") + UnitSectionLabel(section)
                    + " (" + selectedCount + "/" + section.Indices.Length + ")");
                if (Widgets.ButtonInvisible(headerRect))
                {
                    if (!collapsedUnitSections.Remove(section.Key))
                        collapsedUnitSections.Add(section.Key);
                    settingsContentHeight = 0f;
                }
                if (collapsed)
                    continue;
                foreach (var index in matching)
                {
                    var unit = selectable[index];
                    var enabled = Settings.experimentalUnitIds.Contains(
                            ExperimentalAcquisitionPolicy.AllUnitsToken, StringComparer.Ordinal)
                        || Settings.experimentalUnitIds.Contains(unit.Id, StringComparer.Ordinal);
                    var kind = unit.Kind == AcquisitionKind.Family ? (string)"RWS_Exp_Event".Translate()
                        : (string)"RWS_Exp_Snapshot".Translate();
                    // The unit's name as the Coverage tab shows it; its ID only in the tooltip (#320).
                    var label = unitLabels[index] + " · " + kind;
                    if (UnitRecordingStates.OwnerPackageId(unit.Id) is { } owner && !ModsConfig.IsActive(owner))
                    {
                        // The MOD to install: its name, or its package ID when no name is known (an external provider's target).
                        var persistedName = ModProviderAdapters.FirstDescriptorFor(unit.Id)?.OwnerDisplayName;
                        var required = MetricAcquisitionLabels.SafeModDisplayName(owner, UI.MetricText.ActiveModTitle, persistedName) ?? owner;
                        var text = label + " · " + (string)"RWS_Mod_RequiresMod".Translate(required);
                        var rowRect = listing.GetRect(Text.LineHeight);
                        Widgets.Label(rowRect, text);
                        UI.UiTooltips.Tip(rowRect, text, unit.Id);
                        continue;
                    }
                    if (unit.RequiredDlc is { } dlc && !ModsConfig.IsActive(dlc))
                    {
                        var text = label + " · " + (string)"RWS_Exp_RequiresDlc".Translate(UI.MetricText.DlcDisplayName(dlc));
                        var rowRect = listing.GetRect(Text.LineHeight);
                        Widgets.Label(rowRect, text);
                        UI.UiTooltips.Tip(rowRect, text, unit.Id);
                        continue;
                    }
                    var state = UnitRecordingStates.Get(unit.Id);
                    if (enabled && state == UnitRecordingState.TargetIncompatible)
                        label += " · " + (string)"RWS_Status_TargetIncompatible".Translate();
                    else if (enabled && state == UnitRecordingState.TargetModMissing)
                        label += " · " + (string)"RWS_Status_ModMissing".Translate();
                    var checkRect = listing.GetRect(Text.LineHeight);
                    Widgets.CheckboxLabeled(checkRect, label, ref enabled);
                    UI.UiTooltips.Tip(checkRect, "RWS_Tip_SettingsUnit".Translate(), unit.Id);
                    // The same mutation path as the Coverage tab's Recording menu (U2): it materializes
                    // the wildcard when one row is turned off, and touches no hidden row while filtering.
                    SetUnitSelected(unit.Id, enabled);
                }
            }
            if (!anyDrawn)
                listing.Label("RWS_Details_NoMatch".Translate());
            // Apply immediately, on this UI (game) thread. No restart or
            // reinstallation of Harmony patches is necessary.
            ApplyLiveSelectionIfChanged();
            listing.Label("RWS_Exp_Help".Translate());
            settingsContentHeight = listing.CurHeight;
            listing.End();
        }
        finally
        {
            Widgets.EndScrollView();
        }
    }

    /// <summary>Every unit the settings window can list: built-in units plus the optional providers' units.</summary>
    internal static AcquisitionUnit[] SelectableUnits()
    {
        if (selectableUnits is not null)
            return selectableUnits;
        selectableUnits = AcquisitionUnits.All.Concat(ModProviderAdapters.KnownUnits).ToArray();
        selectableSections = BuildUnitSections(selectableUnits);
        return selectableUnits;
    }

    internal static UnitSection[] SelectableUnitSections => selectableSections ??= BuildUnitSections(SelectableUnits());

    internal static string[] SelectableUnitLabels => UnitLabels(SelectableUnits());

    /// <summary>U1: events, snapshots, then one section per optional MOD provider, in a stable order.</summary>
    private static UnitSection[] BuildUnitSections(AcquisitionUnit[] units)
    {
        var events = new List<int>();
        var snapshots = new List<int>();
        var mods = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var index = 0; index < units.Length; index++)
        {
            var unit = units[index];
            if (unit.Kind == AcquisitionKind.Derived)
                continue;
            if (unit.Id.StartsWith("mod.", StringComparison.Ordinal))
            {
                var owner = UnitRecordingStates.OwnerPackageId(unit.Id) ?? unit.Id;
                if (!mods.TryGetValue(owner, out var indices))
                    mods[owner] = indices = new List<int>();
                indices.Add(index);
                continue;
            }
            if (unit.Kind == AcquisitionKind.Family)
                events.Add(index);
            else if (unit.Kind == AcquisitionKind.Snapshot)
                snapshots.Add(index);
        }
        var sections = new List<UnitSection>(2 + mods.Count);
        if (events.Count > 0)
            sections.Add(new UnitSection("events", UnitSectionKind.Events, events.ToArray(), null, null));
        if (snapshots.Count > 0)
            sections.Add(new UnitSection("snapshots", UnitSectionKind.Snapshots, snapshots.ToArray(), null, null));
        foreach (var (owner, indices) in mods.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            sections.Add(new UnitSection("mod:" + owner, UnitSectionKind.Mod, indices.ToArray(), owner, units[indices[0]].Id));
        return sections.ToArray();
    }

    internal static string UnitSectionLabel(UnitSection section) => section.Kind switch
    {
        UnitSectionKind.Events => (string)"RWS_Exp_SectionEvents".Translate(),
        UnitSectionKind.Snapshots => (string)"RWS_Exp_SectionSnapshots".Translate(),
        _ => (string)"RWS_Mod_Provider".Translate(ModSectionName(section)),
    };

    private static string ModSectionName(UnitSection section)
    {
        var persisted = section.OwnerUnitId is { } unitId
            ? ModProviderAdapters.FirstDescriptorFor(unitId)?.OwnerDisplayName : null;
        return MetricAcquisitionLabels.SafeModDisplayName(
                section.OwnerPackageId, UI.MetricText.ActiveModTitle, persisted)
            ?? section.OwnerPackageId
            ?? (string)"RWS_Status_ExternalMod".Translate();
    }

    /// <summary>Whether the unit's DLC and owner MOD are present, the same test the unit rows apply inline (U1).</summary>
    internal static bool UnitRequirementsMet(AcquisitionUnit unit)
        => (unit.RequiredDlc is null || ModsConfig.IsActive(unit.RequiredDlc))
            && (UnitRecordingStates.OwnerPackageId(unit.Id) is not { } owner || ModsConfig.IsActive(owner));

    /// <summary>Units the settings window shows as a checkbox and the Coverage menu may toggle.</summary>
    internal static bool IsUnitToggleable(AcquisitionUnit unit)
        => unit.Kind != AcquisitionKind.Derived && UnitRequirementsMet(unit);

    private static List<string> RecommendedUnitIds(AcquisitionUnit[] units)
        => ExperimentalAcquisitionPolicy.RecommendedSelection(
            units,
            dlc => ModsConfig.IsActive(dlc),
            id => UnitRecordingStates.OwnerPackageId(id) is not { } owner || ModsConfig.IsActive(owner)).ToList();

    /// <summary>
    /// The one mutation path shared by the settings rows and the Coverage tab's Recording menu. Turning one unit off
    /// while the wildcard is stored materializes the current catalog (minus that unit) so the opt-out survives a
    /// restart; it never touches hidden rows while the settings search filters them.
    /// </summary>
    internal static void SetUnitSelected(string unitId, bool selected)
    {
        var ids = Settings.experimentalUnitIds;
        var autoAll = ids.Contains(ExperimentalAcquisitionPolicy.AllUnitsToken, StringComparer.Ordinal);
        if (autoAll)
        {
            if (selected)
                return;
            Settings.experimentalUnitIds = SelectableUnits()
                .Where(u => u.Kind != AcquisitionKind.Derived && u.Id != unitId)
                .Select(u => u.Id)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        else if (selected)
        {
            if (ids.Contains(unitId, StringComparer.Ordinal))
                return;
            ids.Add(unitId);
        }
        else if (ids.RemoveAll(x => x == unitId) == 0)
        {
            return;
        }
        ApplyLiveSelectionIfChanged();
    }

    /// <summary>
    /// The units' names, as the Coverage tab names them (#320). They are worked out again only when the language changes:
    /// the units and the loaded MODs stay the same while the game runs.
    /// </summary>
    private static string[] UnitLabels(AcquisitionUnit[] units)
    {
        if (selectableUnitLabels is { } cached && cached.Length == units.Length
            && ReferenceEquals(selectableUnitLabelsLanguage, LanguageDatabase.activeLanguage))
            return cached;
        var names = units.Select(u => MetricAcquisitionLabels.UnitLabel(u.Id, UnitRecordingStates.OwnerPackageId(u.Id),
            UI.MetricText.ActiveModTitle, key => (string)key.Translate(),
            ModProviderAdapters.FirstDescriptorFor(u.Id)?.OwnerDisplayName) ?? u.Id).ToArray();
        selectableUnitLabels = MetricAcquisitionLabels.Distinguish(names, i => UnitStatisticLabels(units[i].Id),
            StringComparer.CurrentCulture);
        selectableUnitLabelsLanguage = LanguageDatabase.activeLanguage;
        return selectableUnitLabels;
    }

    private static IEnumerable<string> UnitStatisticLabels(string unitId)
        => MetricRegistry.ImplementedCatalog.Where(d => d.Acquisition == unitId)
            .Concat(ModProviderAdapters.KnownDescriptors.Where(d => d.AcquisitionId == unitId).Select(d => d.ToDefinition()))
            .Select(UI.MetricText.Label);

    private static CacheLimits? appliedCacheLimits;

    /// <summary>
    /// PR-6a U10: presentation-only display choices stored in the existing view-preferences JSON. They change the next
    /// statistics window, like the window size, and never touch the statistics profile.
    /// </summary>
    private static void DrawDisplayFormatSettings(Listing_Standard listing)
    {
        listing.Label("RWS_Setting_DisplayFormat".Translate());
        var preferences = UI.Dialog_RimworldStatistics.ActivePreferences;
        listing.Label("RWS_Setting_DayFormat".Translate());
        var dayFormat = (DisplayDayFormat)preferences.DayFormat;
        var dayButton = listing.GetRect(30f);
        if (Widgets.ButtonText(new Rect(dayButton.x, dayButton.y, Math.Min(dayButton.width, 320f), dayButton.height),
                DayFormatLabel(dayFormat)))
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new(DayFormatLabel(DisplayDayFormat.DecimalDays), () =>
                    UI.Dialog_RimworldStatistics.UpdateActivePreferences(p => p.DayFormat = (int)DisplayDayFormat.DecimalDays)),
                new(DayFormatLabel(DisplayDayFormat.YearsAndDays), () =>
                    UI.Dialog_RimworldStatistics.UpdateActivePreferences(p => p.DayFormat = (int)DisplayDayFormat.YearsAndDays)),
            }));
        }
        listing.Label("RWS_Setting_DayFormatHelp".Translate());
        var grouping = preferences.NumberGrouping;
        listing.CheckboxLabeled("RWS_Setting_NumberGrouping".Translate(), ref grouping);
        if (grouping != preferences.NumberGrouping)
            UI.Dialog_RimworldStatistics.UpdateActivePreferences(p => p.NumberGrouping = grouping);
        listing.Label("RWS_Setting_NumberGroupingHelp".Translate());
    }

    private static string DayFormatLabel(DisplayDayFormat format) => format == DisplayDayFormat.YearsAndDays
        ? "RWS_Setting_DayFormatYears".Translate()
        : "RWS_Setting_DayFormatDecimal".Translate();

    /// <summary>
    /// The processing-resource settings: how many saves an import reads at once, background precomputation and how much the
    /// caches keep. Standard everywhere is what the MOD did before they existed.
    /// </summary>
    private static void DrawResourceSettings(Listing_Standard listing)
    {
        var processors = Environment.ProcessorCount;
        listing.Label("RWS_Setting_ResourcesHeading".Translate());

        listing.Label("RWS_Setting_ImportThreads".Translate());
        var mode = Settings.ImportThreads;
        var threadButton = listing.GetRect(30f);
        if (Widgets.ButtonText(new Rect(threadButton.x, threadButton.y, Math.Min(threadButton.width, 420f), threadButton.height),
                ImportThreadModeLabel(mode, processors)))
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new(ImportThreadModeLabel(ImportThreadMode.Standard, processors),
                    () => Settings.importThreadMode = (int)ImportThreadMode.Standard),
                new(ImportThreadModeLabel(ImportThreadMode.More, processors),
                    () => Settings.importThreadMode = (int)ImportThreadMode.More),
                new(ImportThreadModeLabel(ImportThreadMode.Manual, processors),
                    () => Settings.importThreadMode = (int)ImportThreadMode.Manual),
            }));
        }
        if (mode == ImportThreadMode.Manual)
        {
            var max = ResourcePolicy.MaxManualImportThreads(processors);
            var current = Math.Clamp(Settings.importThreadsManual, 1, max);
            var chosen = listing.SliderLabeled("RWS_Setting_ImportThreadsCount".Translate(current), current, 1f, max);
            Settings.importThreadsManual = Math.Clamp((int)Math.Round(chosen), 1, max);
        }
        listing.Label("RWS_Setting_ImportThreadsHelp".Translate(processors));

        listing.Gap(6f);
        listing.CheckboxLabeled("RWS_Setting_Precompute".Translate(), ref Settings.precomputeStatistics);
        listing.Label("RWS_Setting_PrecomputeHelp".Translate());

        listing.Gap(6f);
        listing.Label("RWS_Setting_MemoryUse".Translate());
        var memoryButton = listing.GetRect(30f);
        if (Widgets.ButtonText(new Rect(memoryButton.x, memoryButton.y, Math.Min(memoryButton.width, 420f), memoryButton.height),
                MemoryUseLabel(Settings.MemoryUse)))
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new(MemoryUseLabel(MemoryUseMode.Standard), () => Settings.memoryUseMode = (int)MemoryUseMode.Standard),
                new(MemoryUseLabel(MemoryUseMode.More), () => Settings.memoryUseMode = (int)MemoryUseMode.More),
            }));
        }
        listing.Label("RWS_Setting_MemoryUseHelp".Translate(ResourcePolicy.ChartCacheMiB(ResourcePolicy.StandardCaches),
            ResourcePolicy.ChartCacheMiB(ResourcePolicy.MoreCaches)));

        // Applied at once, without waiting for the record: only what the caches keep changes.
        var limits = Settings.CacheLimits;
        if (appliedCacheLimits != limits)
        {
            appliedCacheLimits = limits;
            ProfileService.ApplyResourceSettings();
        }
    }

    private static string ImportThreadModeLabel(ImportThreadMode mode, int processors)
    {
        var count = ResourcePolicy.ImportThreads(mode, Settings.importThreadsManual, processors);
        return mode switch
        {
            ImportThreadMode.More => "RWS_Setting_ImportThreadsMore".Translate(count),
            ImportThreadMode.Manual => "RWS_Setting_ImportThreadsManual".Translate(count),
            _ => "RWS_Setting_ImportThreadsStandard".Translate(count),
        };
    }

    private static string MemoryUseLabel(MemoryUseMode mode) => mode == MemoryUseMode.More
        ? "RWS_Setting_MemoryUseMore".Translate()
        : "RWS_Setting_MemoryUseStandard".Translate();

    private static string WindowSizeLabel(StatisticsWindowSizePreset preset) => preset switch
    {
        StatisticsWindowSizePreset.Large => "RWS_Setting_WindowSizeLarge".Translate(),
        StatisticsWindowSizePreset.ExtraLarge => "RWS_Setting_WindowSizeExtraLarge".Translate(),
        StatisticsWindowSizePreset.FitScreen => "RWS_Setting_WindowSizeFitScreen".Translate(),
        _ => "RWS_Setting_WindowSizeStandard".Translate(),
    };
}
