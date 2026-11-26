using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Separates measured coverage, evidence quality and active collection state.</summary>
internal static class CoverageTab
{
    private enum CoverageFilter { All, Measured, Partial, Missing, NotApplicable }
    private sealed record DisplayRow(
        string Group, string Label, string Capture, string Coverage, string Count,
        MetricStatus? Status, MetricVisualKind VisualKind);

    private const float RowHeight = 27f;
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
    private static StatsView? builtFor;
    private static object? language;
    private static long recordingRevision = -1;
    private static IReadOnlyList<MetricStatus> statuses = [];
    private static DisplayRow[] rows = [];
    private static int matchedMetricCount;
    private static bool rebuildRows = true;
    private static string search = "";
    private static MetricCategory? category;
    private static CoverageFilter filter;
    private static MetricVerification? verification;
    private static Vector2 scroll;
    private static string summary = "";
    private static string caution = "";
    private static float layoutWidth = -1;
    private static ColumnPlan? tablePlan;

    public static void Draw(Rect rect, StatsView view)
    {
        var activeLanguage = LanguageDatabase.activeLanguage;
        var viewChanged = !ReferenceEquals(builtFor, view) || !ReferenceEquals(language, activeLanguage);
        var currentRecordingRevision = MetricGates.RecordingRevision;
        if (viewChanged || recordingRevision != currentRecordingRevision)
        {
            statuses = MetricStatusClassifier.Classify(view, UnitRecordingStates.Get);
            builtFor = view;
            language = activeLanguage;
            recordingRevision = currentRecordingRevision;
            if (viewChanged)
                scroll = Vector2.zero;
            rebuildRows = true;
            var countMeasured = statuses.Count(s => s.Availability == MetricAvailability.AllMeasuredColonies
                && !MetricStatusClassifier.IsPartial(s));
            var countPartial = statuses.Count(s => s.Measured > 0 && MetricStatusClassifier.IsPartial(s));
            var countMissing = statuses.Count(s => s.Availability == MetricAvailability.NoMeasuredColonies);
            var countNA = statuses.Count(s => s.Availability == MetricAvailability.NoApplicableColonies);
            var countExperimental = statuses.Count(s => s.Verification == MetricVerification.Experimental);

            summary = "RWS_Status_Summary".Translate(
                countMeasured, countPartial, countMissing, countNA, countExperimental);
            caution = "RWS_Status_Caution".Translate();
        }

        var summaryRect = new Rect(rect.x, rect.y, rect.width, 30f);
        Widgets.Label(summaryRect, summary.Truncate(rect.width));
        TooltipHandler.TipRegion(summaryRect, summary);
        // A full SDK deduplication set cannot accept further events safely:
        // display this live-session warning even when the cached history view has not changed.
        var cappedMetrics = statuses
            .Where(status => ExternalSdkHost.Instance.IsEventDedupCapacityExceeded(status.Definition.Key))
            .Select(status => MetricText.Label(status.Definition)).ToArray();
        var cautionText = cappedMetrics.Length == 0 ? caution
            : (string)"RWS_Status_SdkEventCapExceeded".Translate(cappedMetrics.Length, FamilyOps.KeyCap);
        var cautionRect = new Rect(rect.x, rect.y + 29f, rect.width, 28f);
        Widgets.Label(cautionRect, cautionText.Truncate(rect.width));
        TooltipHandler.TipRegion(cautionRect, cappedMetrics.Length == 0 ? caution
            : cautionText + "\n" + string.Join("\n", cappedMetrics) + "\n" + caution);

        var controls = new ControlRow(new Rect(rect.x, rect.y + 63f, rect.width, 28f));
        var filterText = FilterLabel(filter);
        var filterBtn = controls.Next(filterText, 140f, 185f);
        var shownFilter = filterText.Truncate(Math.Max(0f, filterBtn.width - 12f));
        if (shownFilter != filterText)
            TooltipHandler.TipRegion(filterBtn, filterText);
        if (Widgets.ButtonText(filterBtn, shownFilter))
        {
            var menu = Enum.GetValues<CoverageFilter>().Select(s =>
                new FloatMenuOption(FilterLabel(s), () => { filter = s; rebuildRows = true; scroll = Vector2.zero; })).ToList();
            Find.WindowStack.Add(new FloatMenu(menu));
        }
        var categoryText = category is { } cat ? DetailsTab.CategoryLabel(cat) : (string)"RWS_Details_AllCategories".Translate();
        var catBtn = controls.Next(categoryText, 135f, 200f);
        var shownCat = categoryText.Truncate(Math.Max(0f, catBtn.width - 12f));
        if (shownCat != categoryText)
            TooltipHandler.TipRegion(catBtn, categoryText);
        if (Widgets.ButtonText(catBtn, shownCat))
        {
            var menu = new List<FloatMenuOption>
            {
                new("RWS_Details_AllCategories".Translate(), () =>
                { category = null; rebuildRows = true; scroll = Vector2.zero; }),
            };
            menu.AddRange(statuses.Select(s => s.Definition.Category).Distinct()
                .Select(cat2 => new FloatMenuOption(DetailsTab.CategoryLabel(cat2), () =>
                { category = cat2; rebuildRows = true; scroll = Vector2.zero; })));
            Find.WindowStack.Add(new FloatMenu(menu));
        }
        var verificationText = verification is null
            ? (string)"RWS_Status_VerificationAll".Translate()
            : VerificationLabel(verification.Value);
        var verBtn = controls.Next(verificationText, 135f, 200f);
        var shownVer = verificationText.Truncate(Math.Max(0f, verBtn.width - 12f));
        if (shownVer != verificationText)
            TooltipHandler.TipRegion(verBtn, verificationText);
        if (Widgets.ButtonText(verBtn, shownVer))
        {
            Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
            {
                new("RWS_Status_VerificationAll".Translate(), () =>
                { verification = null; rebuildRows = true; scroll = Vector2.zero; }),
                new(VerificationLabel(MetricVerification.Standard), () =>
                { verification = MetricVerification.Standard; rebuildRows = true; scroll = Vector2.zero; }),
                new(VerificationLabel(MetricVerification.Experimental), () =>
                { verification = MetricVerification.Experimental; rebuildRows = true; scroll = Vector2.zero; }),
                new(VerificationLabel(MetricVerification.Legacy), () =>
                { verification = MetricVerification.Legacy; rebuildRows = true; scroll = Vector2.zero; }),
            }));
        }
        // U2: change recording here, without leaving for the settings window. The menu uses the same mutation and
        // apply path as the settings rows (RimworldStatisticsMod.SetUnitSelected).
        var recordingText = (string)"RWS_Coverage_Recording".Translate();
        var recordingBtn = controls.Next(recordingText, 120f, 190f);
        var shownRecording = recordingText.Truncate(Math.Max(0f, recordingBtn.width - 12f));
        if (shownRecording != recordingText)
            TooltipHandler.TipRegion(recordingBtn, recordingText);
        if (Widgets.ButtonText(recordingBtn, shownRecording))
            OpenRecordingMenu();

        if (rebuildRows)
        {
            rows = BuildRows();
            rebuildRows = false;
        }

        var activeFilters = FilterSummary.CountActiveFilters(
            filter != CoverageFilter.All, category is not null, verification is not null, search);
        var shownText = (string)"RWS_Filter_Shown".Translate(matchedMetricCount, statuses.Count);
        var activeText = activeFilters > 0 ? (string)"RWS_Filter_ActiveCount".Translate(activeFilters) : "";
        var summaryLabelText = activeFilters > 0 ? shownText + " · " + activeText : shownText;

        var searchRowY = rect.y + 63f + controls.Height + 4f;
        var searchRow = new ControlRow(new Rect(rect.x, searchRowY, rect.width, 28f));
        var searchWidth = Math.Max(140f, Math.Min(rect.width * 0.48f, 360f));
        var searchFieldRect = searchRow.Next(searchWidth);
        var nextSearch = Widgets.TextField(searchFieldRect, search) ?? "";
        if (nextSearch != search)
        {
            search = nextSearch;
            rebuildRows = true;
            scroll = Vector2.zero;
        }
        var summaryRect2 = searchRow.Next(summaryLabelText, 95f, 240f);
        using (UiScopes.GuiState())
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.color = new Color(0.8f, 0.8f, 0.8f);
            Widgets.Label(summaryRect2, summaryLabelText.Truncate(summaryRect2.width));
        }
        TooltipHandler.TipRegion(summaryRect2, summaryLabelText);
        if (activeFilters > 0)
        {
            var resetLabel = (string)"RWS_Filter_Reset".Translate();
            var resetRect = searchRow.Next(resetLabel, 95f, 175f);
            if (Widgets.ButtonText(resetRect, resetLabel.Truncate(Math.Max(0f, resetRect.width - 10f))))
            {
                ResetFilters();
            }
        }
        if (rebuildRows)
        {
            rows = BuildRows();
            rebuildRows = false;
        }

        var start = searchRowY + searchRow.Height + 6f;
        if (tablePlan is null || layoutWidth != rect.width)
        {
            layoutWidth = rect.width;
            tablePlan = ColumnLayout.Resolve(
                [new(340f, 5f), new(150f, 2f), new(180f, 3f), new(85f, 1f)], Math.Max(0, rect.width - 16f));
        }
        var tableWidth = tablePlan.TotalWidth;
        var titleRect = new Rect(rect.x, start, rect.width, 27f);
        using (UiScopes.Group(titleRect))
        {
            DrawCells(new Rect(-scroll.x, 0f, tableWidth, 27f),
                ["RWS_Status_Statistic".Translate(), "RWS_Status_Recording".Translate(),
                    "RWS_Status_Coverage".Translate(), "RWS_Status_Count".Translate()], tablePlan.Widths, null);
        }

        var viewport = new Rect(rect.x, start + RowHeight, rect.width, Math.Max(0, rect.yMax - start - RowHeight));
        if (viewport.height <= 0)
            return;
        if (rows.Length == 0)
        {
            Widgets.Label(viewport, "RWS_Details_NoMatch".Translate());
            return;
        }
        using (UiScopes.ScrollView(viewport, ref scroll, new Rect(0, 0, tableWidth, rows.Length * RowHeight)))
        {
            var first = Math.Max(0, (int)(scroll.y / RowHeight));
            var last = Math.Min(rows.Length - 1, (int)((scroll.y + viewport.height) / RowHeight) + 1);
            for (var i = first; i <= last; i++)
            {
                var row = rows[i];
                var rowRect = new Rect(0f, i * RowHeight, tableWidth, RowHeight);
                if (i % 2 == 1)
                    Widgets.DrawHighlight(rowRect, 0.2f);
                DrawCells(rowRect, [row.Label, row.Capture, row.Coverage, row.Count], tablePlan.Widths,
                    row.VisualKind, isGroup: row.Status is null);
                if (row.Status is null)
                {
                    Widgets.DrawHighlightIfMouseover(rowRect);
                    if (Widgets.ButtonInvisible(rowRect) && string.IsNullOrWhiteSpace(search))
                    {
                        if (!Expanded.Remove(row.Group))
                            Expanded.Add(row.Group);
                        rebuildRows = true;
                    }
                }
                else
                {
                    var metric = row.Status;
                    var id = unchecked(0x52571000 + i * 8191);
                    // U36: the full diagnostic tooltip normally, the metric key while Shift is held.
                    UiTooltips.Tip(rowRect,
                        () => Tooltip(metric, view.Stats) + "\n" + (string)"RWS_Tip_TechnicalHint".Translate(),
                        metric.Definition.Key, id);
                }
            }
        }
    }

    private static void ResetFilters()
    {
        filter = CoverageFilter.All;
        category = null;
        verification = null;
        search = "";
        rebuildRows = true;
        scroll = Vector2.zero;
    }

    /// <summary>
    /// The Recording menu (U2): sections first, then every unit the settings window would show as a checkbox, marked
    /// selected or not. Units whose DLC or owner MOD is absent are left out, exactly as the settings rows leave them out
    /// of the checkbox list (the settings window still lists them with a requirement notice).
    /// </summary>
    private static void OpenRecordingMenu()
    {
        var units = RimworldStatisticsMod.SelectableUnits();
        var labels = RimworldStatisticsMod.SelectableUnitLabels;
        var ids = RimworldStatisticsMod.Settings.experimentalUnitIds;
        var autoAll = ids.Contains(ExperimentalAcquisitionPolicy.AllUnitsToken, StringComparer.Ordinal);
        var menu = new List<FloatMenuOption>();
        foreach (var section in RimworldStatisticsMod.SelectableUnitSections)
        {
            var candidates = section.Indices
                .Where(index => RimworldStatisticsMod.IsUnitToggleable(units[index]))
                .ToArray();
            if (candidates.Length == 0)
                continue;
            var sectionLabel = RimworldStatisticsMod.UnitSectionLabel(section);
            menu.Add(new FloatMenuOption(sectionLabel, () =>
            {
                var options = new List<FloatMenuOption>(candidates.Length);
                foreach (var index in candidates)
                {
                    var unit = units[index];
                    var selected = autoAll || ids.Contains(unit.Id, StringComparer.Ordinal);
                    var kind = unit.Kind == AcquisitionKind.Family
                        ? (string)"RWS_Exp_Event".Translate()
                        : (string)"RWS_Exp_Snapshot".Translate();
                    var optionLabel = (selected ? "✓ " : "✗ ") + labels[index] + " · " + kind;
                    var unitId = unit.Id;
                    var choose = !selected;
                    options.Add(new FloatMenuOption(optionLabel, () =>
                    {
                        RimworldStatisticsMod.SetUnitSelected(unitId, choose);
                        // Reclassify and rebuild even when the selection happened not to change the revision.
                        recordingRevision = -1;
                        rebuildRows = true;
                        scroll = Vector2.zero;
                    }));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }));
        }
        if (menu.Count > 0)
            Find.WindowStack.Add(new FloatMenu(menu));
    }

    private static DisplayRow[] BuildRows()
    {
        var term = search.Trim();
        var filtered = statuses.Where(s => category is null || s.Definition.Category == category)
            .Where(s => verification is null || s.Verification == verification)
            .Where(s => filter switch
            {
                CoverageFilter.Measured => s.Measured > 0,
                CoverageFilter.Partial => MetricStatusClassifier.IsPartial(s),
                CoverageFilter.Missing => s.Availability == MetricAvailability.NoMeasuredColonies,
                CoverageFilter.NotApplicable => s.Availability == MetricAvailability.NoApplicableColonies,
                _ => true,
            })
            .Where(s => term.Length == 0 ||
                s.Definition.Key.Contains(term, StringComparison.OrdinalIgnoreCase)
                || MetricText.Label(s.Definition).Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || (MetricText.ModSourceLabel(s.Definition)?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false))
            .ToArray();
        matchedMetricCount = filtered.Length;

        var groups = MetricAcquisitionLabels.BuildGroups(
            filtered,
            MetricText.ActiveModTitle,
            key => (string)key.Translate(),
            DetailsTab.CategoryLabel,
            cat => (string)"RWS_Status_ExternalModCategory".Translate(cat),
            StringComparer.CurrentCulture);

        // Two units with one name (two units of one MOD) are told apart as in the MOD settings (#320).
        var displayLabels = MetricAcquisitionLabels.Distinguish(
            groups.Select(g => g.Label).ToArray(),
            i => groups[i].Items.Select(s => MetricText.Label(s.Definition)),
            StringComparer.CurrentCulture);

        var results = new List<DisplayRow>(filtered.Length + groups.Count);
        for (var index = 0; index < groups.Count; index++)
        {
            var group = groups[index];
            var expanded = MetricAcquisitionLabels.IsGroupExpanded(group.GroupId, Expanded, term);
            var reqParts = new List<string>(2);
            if (!string.IsNullOrEmpty(group.RequiredDlc))
                reqParts.Add(MetricText.DlcDisplayName(group.RequiredDlc!));
            // The same "MOD: name" as each statistic's row and tooltip, with the generic label for an unknown provider (#303).
            if (!string.IsNullOrEmpty(group.OwnerPackageId))
                reqParts.Add((string)"RWS_Mod_Provider".Translate(MetricText.SafeModDisplayName(group.OwnerPackageId!, group.OwnerDisplayName)));
            var requirement = string.Join(" · ", reqParts);

            var displayLabel = displayLabels[index];

            results.Add(new DisplayRow(
                group.GroupId,
                (expanded ? "▾ " : "▸ ") + displayLabel,
                RecordingLabel(group.Recording),
                requirement,
                "RWS_Status_GroupCount".Translate(group.MeasuredCount, group.TotalCount),
                null,
                group.VisualKind));

            if (!expanded)
                continue;

            foreach (var status in group.Items.OrderBy(s => MetricText.Label(s.Definition), StringComparer.CurrentCulture))
                results.Add(new DisplayRow(
                    group.GroupId,
                    "    " + MetricText.Label(status.Definition),
                    (MetricText.ModSourceLabel(status.Definition) is { } modSource
                        ? modSource + " · " : "") + RecordingLabel(status.Recording),
                    CoverageLabel(status) + " · " + VerificationLabel(status.Verification),
                    status.Measured + "/" + status.Targets,
                    status,
                    MetricVisualState.Classify(status)));
        }
        return results.ToArray();
    }

    private static string CoverageLabel(MetricStatus s)
    {
        if (s.Availability == MetricAvailability.NoApplicableColonies)
            return "RWS_Status_NotApplicable".Translate();
        if (s.Availability == MetricAvailability.NoMeasuredColonies)
            return "RWS_Status_NotMeasured".Translate();
        if (MetricStatusClassifier.IsPartial(s))
            return "RWS_Status_Partial".Translate();
        return "RWS_Status_Measured".Translate();
    }

    private static string FilterLabel(CoverageFilter status) => status switch
    {
        CoverageFilter.Measured => "RWS_Status_FilterMeasured".Translate(),
        CoverageFilter.Partial => "RWS_Status_FilterPartial".Translate(),
        CoverageFilter.Missing => "RWS_Status_FilterMissing".Translate(),
        CoverageFilter.NotApplicable => "RWS_Status_FilterNA".Translate(),
        _ => "RWS_Status_FilterAll".Translate(),
    };

    private static string VerificationLabel(MetricVerification verification) => verification switch
    {
        MetricVerification.Experimental => "RWS_Status_Experimental".Translate(),
        MetricVerification.Legacy => "RWS_Status_Legacy".Translate(),
        _ => "RWS_Status_Standard".Translate(),
    };

    private static string RecordingLabel(UnitRecordingState recording) => recording switch
    {
        UnitRecordingState.Recording => "RWS_Status_RecordingOn".Translate(),
        UnitRecordingState.NotSelected => "RWS_Status_RecordingOff".Translate(),
        UnitRecordingState.DlcMissing => "RWS_Status_DlcMissing".Translate(),
        UnitRecordingState.TargetModMissing => "RWS_Status_ModMissing".Translate(),
        UnitRecordingState.TargetIncompatible => "RWS_Status_TargetIncompatible".Translate(),
        UnitRecordingState.Failed => "RWS_Status_RecordingFailed".Translate(),
        _ => "RWS_Status_RecordingUnnecessary".Translate(),
    };

    private static string Tooltip(MetricStatus status, StatsResult stats)
    {
        var lines = new List<string>
        {
            MetricText.Label(status.Definition),
            CoverageLabel(status) + " · " + VerificationLabel(status.Verification),
            RecordingLabel(status.Recording),
        };
        if (status.Targets > 0)
            lines.Add("RWS_Details_Tip_Colonies".Translate(status.Targets, status.Measured));
        lines.Add(MetricAcquisitionLabels.ResolveGroupLabel(
            status.Definition,
            MetricText.ActiveModTitle,
            key => (string)key.Translate(),
            DetailsTab.CategoryLabel,
            cat => (string)"RWS_Status_ExternalModCategory".Translate(cat)));
        if (status.Definition.RequiredDlc is { } dlc)
            lines.Add("RWS_Exp_RequiresDlc".Translate(MetricText.DlcDisplayName(dlc)));
        if (status.Definition.OwnerPackageId is { } owner)
            lines.Add("RWS_Mod_Provider".Translate(MetricText.SafeModDisplayName(owner, status.Definition.OwnerDisplayName)));
        if (ExternalSdkHost.Instance.IsEventDedupCapacityExceeded(status.Definition.Key))
            lines.Add("RWS_Status_SdkEventCapExceeded".Translate(1, FamilyOps.KeyCap));
        if (status.MissingColonyKeys.Count > 0)
        {
            lines.Add("RWS_Status_MissingColonies".Translate(status.MissingColonyKeys.Count));
            foreach (var key in status.MissingColonyKeys.Take(10))
            {
                var colony = stats.Colonies.FirstOrDefault(c => c.Key == key);
                lines.Add(colony is null ? key : Dialog_RimworldStatistics.ColonyName(colony));
            }
        }
        return string.Join("\n", lines);
    }

    private static void DrawCells(Rect rect, string[] cells, float[] widths, MetricVisualKind? visualKind, bool isGroup = false)
    {
        var x = rect.x;
        using (UiScopes.GuiState())
        {
            for (var i = 0; i < cells.Length; i++)
            {
                Text.Anchor = i == 3 ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                GUI.color = visualKind is { } kind && (isGroup ? i == 3 : i == 2)
                    ? MetricVisualStyle.StateColor(kind) : Color.white;
                var cell = new Rect(x + 4f, rect.y, Math.Max(0, widths[i] - 8f), rect.height);
                var shown = cells[i].Truncate(cell.width);
                Widgets.Label(cell, shown);
                // Metric rows use one full-row tooltip (which contains the untruncated label,
                // recording, coverage and counts). Group/header rows have no such tooltip.
                if ((isGroup || visualKind is null) && shown != cells[i])
                    TooltipHandler.TipRegion(cell, cells[i]);
                x += widths[i];
            }
        }
    }
}
