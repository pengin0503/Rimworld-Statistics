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

    /// <summary>Frozen at startup. Changes to desired settings never hot-patch Harmony.</summary>
    public static ExperimentalSelection ActiveAcquisitionSelection { get; private set; } =
        new(false, Array.Empty<string>());

    private static Vector2 experimentalScroll;
    private static AcquisitionUnit[]? selectableUnits;

    private static bool SelectionNeedsRestart()
    {
        var desired = Settings.DesiredSelection;
        return desired.Enabled != ActiveAcquisitionSelection.Enabled ||
            !desired.ExplicitUnitIds.ToHashSet(StringComparer.Ordinal)
                .SetEquals(ActiveAcquisitionSelection.ExplicitUnitIds);
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
            listing.CheckboxLabeled("RWS_Exp_Enable".Translate(), ref Settings.experimentalEnabled);
            listing.Label("RWS_Exp_Warning".Translate());
            var buttons = listing.GetRect(28f);
            var width = (buttons.width - 12f) / 3f;
            if (Widgets.ButtonText(new Rect(buttons.x, buttons.y, width, buttons.height), "RWS_Exp_SelectAll".Translate()))
                Settings.experimentalUnitIds = selectable
                    .Where(u => u.Kind != AcquisitionKind.Derived && (u.RequiredDlc is null || ModsConfig.IsActive(u.RequiredDlc)))
                    .Select(u => u.Id).ToList();
            if (Widgets.ButtonText(new Rect(buttons.x + width + 6f, buttons.y, width, buttons.height), "RWS_Exp_SelectNone".Translate()))
                Settings.experimentalUnitIds.Clear();
            if (Widgets.ButtonText(new Rect(buttons.x + (width + 6f) * 2f, buttons.y, width, buttons.height), "RWS_Exp_SelectEvents".Translate()))
                Settings.experimentalUnitIds = selectable
                    .Where(u => u.Kind == AcquisitionKind.Family && (u.RequiredDlc is null || ModsConfig.IsActive(u.RequiredDlc)))
                    .Select(u => u.Id).ToList();
            foreach (var unit in selectable)
            {
                if (unit.Kind == AcquisitionKind.Derived)
                    continue;
                var enabled = Settings.experimentalUnitIds.Contains(unit.Id, StringComparer.Ordinal);
                var kind = unit.Kind == AcquisitionKind.Family ? (string)"RWS_Exp_Event".Translate()
                    : (string)"RWS_Exp_Snapshot".Translate();
                var label = unit.Id + " · " + kind;
                var provider = ModProviderAdapters.FirstDescriptorFor(unit.Id);
                if (provider is not null && !ModsConfig.IsActive(provider.OwnerPackageId))
                {
                    listing.Label(label + " · " + (string)"RWS_Mod_RequiresMod".Translate(provider.OwnerPackageId));
                    continue;
                }
                if (unit.RequiredDlc is { } dlc && !ModsConfig.IsActive(dlc))
                {
                    listing.Label(label + " · " + (string)"RWS_Exp_RequiresDlc".Translate(dlc));
                    continue;
                }
                if (enabled && UnitRecordingStates.Get(unit.Id) == UnitRecordingState.TargetIncompatible)
                    label += " · " + (string)"RWS_Status_TargetIncompatible".Translate();
                listing.CheckboxLabeled(label, ref enabled);
                if (enabled && !Settings.experimentalUnitIds.Contains(unit.Id, StringComparer.Ordinal))
                    Settings.experimentalUnitIds.Add(unit.Id);
                else if (!enabled)
                    Settings.experimentalUnitIds.RemoveAll(x => x == unit.Id);
            }
            if (SelectionNeedsRestart())
                listing.Label("RWS_Exp_Restart".Translate());
            listing.Label("RWS_Exp_Help".Translate());
            settingsContentHeight = listing.CurHeight;
            listing.End();
        }
        finally
        {
            Widgets.EndScrollView();
        }
    }

    private static string WindowSizeLabel(StatisticsWindowSizePreset preset) => preset switch
    {
        StatisticsWindowSizePreset.Large => "RWS_Setting_WindowSizeLarge".Translate(),
        StatisticsWindowSizePreset.ExtraLarge => "RWS_Setting_WindowSizeExtraLarge".Translate(),
        StatisticsWindowSizePreset.FitScreen => "RWS_Setting_WindowSizeFitScreen".Translate(),
        _ => "RWS_Setting_WindowSizeStandard".Translate(),
    };
}
