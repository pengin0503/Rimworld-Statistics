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
    private static string[]? selectableUnitLabels;
    private static LoadedLanguage? selectableUnitLabelsLanguage;

    private static void ApplyLiveSelectionIfChanged()
    {
        var desired = Settings.DesiredSelection;
        if (desired.Enabled == ActiveAcquisitionSelection.Enabled &&
            desired.ExplicitUnitIds.ToHashSet(StringComparer.Ordinal)
                .SetEquals(ActiveAcquisitionSelection.ExplicitUnitIds))
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
        var selectable = selectableUnits ??= AcquisitionUnits.All.Concat(ModProviderAdapters.KnownUnits).ToArray();
        var unitLabels = UnitLabels(selectable);
        var viewRect = new Rect(0f, 0f, inRect.width - 20f, settingsContentHeight > 0 ? settingsContentHeight : 440f + selectable.Length * rowHeight);
        Widgets.BeginScrollView(inRect, ref experimentalScroll, viewRect);
        try
        {
            var listing = new Listing_Standard();
            listing.Begin(viewRect);
            listing.CheckboxLabeled("RWS_Setting_IncludeDevMode".Translate(), ref Settings.includeDevMode);
            listing.CheckboxLabeled("RWS_Setting_AutoImport".Translate(), ref Settings.autoImport);
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
            listing.CheckboxLabeled("RWS_Exp_Enable".Translate(), ref Settings.experimentalEnabled);
            listing.Label("RWS_Exp_Warning".Translate());
            var buttons = listing.GetRect(28f);
            var width = (buttons.width - 12f) / 3f;
            if (Widgets.ButtonText(new Rect(buttons.x, buttons.y, width, buttons.height), "RWS_Exp_SelectAll".Translate()))
                Settings.experimentalUnitIds = new List<string> { ExperimentalAcquisitionPolicy.AllUnitsToken };
            if (Widgets.ButtonText(new Rect(buttons.x + width + 6f, buttons.y, width, buttons.height), "RWS_Exp_SelectNone".Translate()))
                Settings.experimentalUnitIds.Clear();
            if (Widgets.ButtonText(new Rect(buttons.x + (width + 6f) * 2f, buttons.y, width, buttons.height), "RWS_Exp_SelectEvents".Translate()))
                Settings.experimentalUnitIds = selectable
                    .Where(u => u.Kind == AcquisitionKind.Family
                        && (u.RequiredDlc is null || ModsConfig.IsActive(u.RequiredDlc))
                        && (UnitRecordingStates.OwnerPackageId(u.Id) is not { } reqMod || ModsConfig.IsActive(reqMod)))
                    .Select(u => u.Id).ToList();
            // The wildcard marks every current and future unit selected. When a
            // user unticks an individual item, materialize the current catalog
            // to preserve their choice without silently restoring all on restart.
            var autoAll = Settings.experimentalUnitIds.Contains(
                ExperimentalAcquisitionPolicy.AllUnitsToken, StringComparer.Ordinal);
            for (var index = 0; index < selectable.Length; index++)
            {
                var unit = selectable[index];
                if (unit.Kind == AcquisitionKind.Derived)
                    continue;
                var enabled = autoAll ||
                    Settings.experimentalUnitIds.Contains(unit.Id, StringComparer.Ordinal);
                var kind = unit.Kind == AcquisitionKind.Family ? (string)"RWS_Exp_Event".Translate()
                    : (string)"RWS_Exp_Snapshot".Translate();
                // The unit's name as the Coverage tab shows it; its ID only in the tooltip (#320).
                var label = unitLabels[index] + " · " + kind;
                if (UnitRecordingStates.OwnerPackageId(unit.Id) is { } owner && !ModsConfig.IsActive(owner))
                {
                    // The MOD to install: its name, or its package ID when no name is known (an external provider's target).
                    var required = ModProviderNames.Resolve(owner, UI.MetricText.ActiveModTitle) ?? owner;
                    TooltipHandler.TipRegion(listing.Label(label + " · " + (string)"RWS_Mod_RequiresMod".Translate(required)),
                        unit.Id);
                    continue;
                }
                if (unit.RequiredDlc is { } dlc && !ModsConfig.IsActive(dlc))
                {
                    TooltipHandler.TipRegion(listing.Label(label + " · " +
                        (string)"RWS_Exp_RequiresDlc".Translate(UI.MetricText.DlcDisplayName(dlc))), unit.Id);
                    continue;
                }
                var state = UnitRecordingStates.Get(unit.Id);
                if (enabled && state == UnitRecordingState.TargetIncompatible)
                    label += " · " + (string)"RWS_Status_TargetIncompatible".Translate();
                else if (enabled && state == UnitRecordingState.TargetModMissing)
                    label += " · " + (string)"RWS_Status_ModMissing".Translate();
                listing.CheckboxLabeled(label, ref enabled, unit.Id);
                if (autoAll && !enabled)
                {
                    Settings.experimentalUnitIds = selectable
                        .Where(u => u.Kind != AcquisitionKind.Derived && u.Id != unit.Id)
                        .Select(u => u.Id)
                        .Distinct(StringComparer.Ordinal)
                        .ToList();
                    autoAll = false;
                }
                else if (!autoAll && enabled &&
                    !Settings.experimentalUnitIds.Contains(unit.Id, StringComparer.Ordinal))
                    Settings.experimentalUnitIds.Add(unit.Id);
                else if (!autoAll && !enabled)
                    Settings.experimentalUnitIds.RemoveAll(x => x == unit.Id);
            }
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
            UI.MetricText.ActiveModTitle, key => (string)key.Translate()) ?? u.Id).ToArray();
        selectableUnitLabels = MetricAcquisitionLabels.Distinguish(names, i => UnitStatisticLabels(units[i].Id),
            StringComparer.CurrentCulture);
        selectableUnitLabelsLanguage = LanguageDatabase.activeLanguage;
        return selectableUnitLabels;
    }

    private static IEnumerable<string> UnitStatisticLabels(string unitId)
        => MetricRegistry.ImplementedCatalog.Where(d => d.Acquisition == unitId)
            .Concat(ModProviderAdapters.KnownDescriptors.Where(d => d.AcquisitionId == unitId).Select(d => d.ToDefinition()))
            .Select(UI.MetricText.Label);

    private static string WindowSizeLabel(StatisticsWindowSizePreset preset) => preset switch
    {
        StatisticsWindowSizePreset.Large => "RWS_Setting_WindowSizeLarge".Translate(),
        StatisticsWindowSizePreset.ExtraLarge => "RWS_Setting_WindowSizeExtraLarge".Translate(),
        StatisticsWindowSizePreset.FitScreen => "RWS_Setting_WindowSizeFitScreen".Translate(),
        _ => "RWS_Setting_WindowSizeStandard".Translate(),
    };
}
