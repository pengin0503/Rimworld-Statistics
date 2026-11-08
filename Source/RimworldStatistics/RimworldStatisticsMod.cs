using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics;

public sealed class RimworldStatisticsMod : Mod
{
    public const string HarmonyId = "pengin0503.rimworldstatistics";

    public static RimworldStatisticsSettings Settings { get; private set; } = new();

    /// <summary>Frozen at startup. Changes to desired settings never hot-patch Harmony.</summary>
    public static ExperimentalSelection ActiveAcquisitionSelection { get; private set; } =
        new(false, Array.Empty<string>());

    private static Vector2 experimentalScroll;

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
        Settings = GetSettings<RimworldStatisticsSettings>();
        ActiveAcquisitionSelection = Settings.DesiredSelection;
        MetricGates.ConfigureProductionSelection(
            ExperimentalAcquisitionPolicy.Resolve(ActiveAcquisitionSelection, AcquisitionUnits.All));
        PatchRunner.ApplyAll(new Harmony(HarmonyId));
    }

    public override string SettingsCategory() => "RWS_SettingsCategory".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        const float rowHeight = 27f;
        var selectable = AcquisitionUnits.All.Concat(ModProviderAdapters.KnownUnits).ToArray();
        var viewRect = new Rect(0f, 0f, inRect.width - 20f, 330f + selectable.Length * rowHeight);
        Widgets.BeginScrollView(inRect, ref experimentalScroll, viewRect);
        try
        {
            var listing = new Listing_Standard();
            listing.Begin(viewRect);
            listing.CheckboxLabeled("RWS_Setting_IncludeDevMode".Translate(), ref Settings.includeDevMode);
            listing.CheckboxLabeled("RWS_Setting_AutoImport".Translate(), ref Settings.autoImport);
            listing.Gap(8f);
            listing.CheckboxLabeled("RWS_Exp_Enable".Translate(), ref Settings.experimentalEnabled);
            listing.Label("RWS_Exp_Warning".Translate());
            listing.CheckboxLabeled("RWS_Exp_ViewSaved".Translate(), ref Settings.showRecordedExperimental);
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
                var provider = ModProviderAdapters.KnownDescriptors.FirstOrDefault(d => d.AcquisitionId == unit.Id);
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
                listing.CheckboxLabeled(label, ref enabled);
                if (enabled && !Settings.experimentalUnitIds.Contains(unit.Id, StringComparer.Ordinal))
                    Settings.experimentalUnitIds.Add(unit.Id);
                else if (!enabled)
                    Settings.experimentalUnitIds.RemoveAll(x => x == unit.Id);
            }
            if (SelectionNeedsRestart())
                listing.Label("RWS_Exp_Restart".Translate());
            listing.Label("RWS_Exp_Help".Translate());
            listing.End();
        }
        finally
        {
            Widgets.EndScrollView();
        }
    }
}
