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
        MetricStatus? Status);

    private const float RowHeight = 27f;
    private static readonly HashSet<string> Expanded = new(StringComparer.Ordinal);
    private static StatsView? builtFor;
    private static object? language;
    private static long recordingRevision = -1;
    private static IReadOnlyList<MetricStatus> statuses = [];
    private static DisplayRow[] rows = [];
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
            {
                Expanded.UnionWith(statuses.Select(GroupId));
                scroll = Vector2.zero;
            }
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
        if (Widgets.ButtonText(controls.Next(filterText, 140f, 185f), filterText))
        {
            var menu = Enum.GetValues<CoverageFilter>().Select(s =>
                new FloatMenuOption(FilterLabel(s), () => { filter = s; rebuildRows = true; scroll = Vector2.zero; })).ToList();
            Find.WindowStack.Add(new FloatMenu(menu));
        }
        var categoryText = category is { } cat ? DetailsTab.CategoryLabel(cat) : (string)"RWS_Details_AllCategories".Translate();
        if (Widgets.ButtonText(controls.Next(categoryText, 135f, 200f), categoryText.Truncate(192f)))
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
        if (Widgets.ButtonText(controls.Next(verificationText, 135f, 200f), verificationText.Truncate(192f)))
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

        var searchY = rect.y + 63f + controls.Height + 4f;
        var nextSearch = Widgets.TextField(new Rect(rect.x, searchY, rect.width, 28f), search) ?? "";
        if (nextSearch != search)
        {
            search = nextSearch;
            rebuildRows = true;
            scroll = Vector2.zero;
        }
        if (rebuildRows)
        {
            rows = BuildRows();
            rebuildRows = false;
        }

        var start = searchY + 34f;
        if (tablePlan is null || layoutWidth != rect.width)
        {
            layoutWidth = rect.width;
            tablePlan = ColumnLayout.Resolve(
                [new(340f, 5f), new(150f, 2f), new(180f, 3f), new(85f, 1f)], Math.Max(0, rect.width - 16f));
        }
        var tableWidth = tablePlan.TotalWidth;
        var titleRect = new Rect(rect.x, start, rect.width, 27f);
        GUI.BeginGroup(titleRect);
        DrawCells(new Rect(-scroll.x, 0f, tableWidth, 27f),
            ["RWS_Status_Statistic".Translate(), "RWS_Status_Recording".Translate(),
                "RWS_Status_Coverage".Translate(), "RWS_Status_Count".Translate()], tablePlan.Widths);
        GUI.EndGroup();

        var viewport = new Rect(rect.x, start + RowHeight, rect.width, Math.Max(0, rect.yMax - start - RowHeight));
        Widgets.BeginScrollView(viewport, ref scroll,
            new Rect(0, 0, tableWidth, rows.Length * RowHeight));
        var first = Math.Max(0, (int)(scroll.y / RowHeight));
        var last = Math.Min(rows.Length - 1, (int)((scroll.y + viewport.height) / RowHeight) + 1);
        for (var i = first; i <= last; i++)
        {
            var row = rows[i];
            var rowRect = new Rect(0f, i * RowHeight, tableWidth, RowHeight);
            if (i % 2 == 1)
                Widgets.DrawHighlight(rowRect, 0.2f);
            DrawCells(rowRect, [row.Label, row.Capture, row.Coverage, row.Count], tablePlan.Widths);
            if (row.Status is null)
            {
                Widgets.DrawHighlightIfMouseover(rowRect);
                if (Widgets.ButtonInvisible(rowRect))
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
                TooltipHandler.TipRegion(rowRect, () => Tooltip(metric, view.Stats), id);
            }
        }
        Widgets.EndScrollView();
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
                || MetricText.Label(s.Definition).Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .ToArray();
        var results = new List<DisplayRow>(filtered.Length + 30);
        foreach (var group in filtered.GroupBy(GroupId).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            var state = first.Recording;
            var requirement = string.Join(" · ", new[] { first.Definition.RequiredDlc, first.Definition.OwnerPackageId }
                .Where(value => !string.IsNullOrEmpty(value)));
            var num = group.Count();
            var measured = group.Count(s => s.Measured > 0);
            results.Add(new DisplayRow(group.Key, (Expanded.Contains(group.Key) ? "▾ " : "▸ ") + group.Key,
                RecordingLabel(state), requirement, measured + "/" + num, null));
            if (!Expanded.Contains(group.Key))
                continue;
            foreach (var status in group.OrderBy(s => MetricText.Label(s.Definition), StringComparer.CurrentCulture))
                results.Add(new DisplayRow(group.Key, "    " + MetricText.Label(status.Definition),
                    RecordingLabel(status.Recording), CoverageLabel(status) + " · " + VerificationLabel(status.Verification),
                    status.Measured + "/" + status.Targets, status));
        }
        return results.ToArray();
    }

    private static string GroupId(MetricStatus s) =>
        s.Definition.Acquisition ?? s.Definition.Provenance.ToString();

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
        var text = MetricText.Label(status.Definition) + "\n" + status.Definition.Key + "\n"
            + CoverageLabel(status) + " · " + VerificationLabel(status.Verification) + "\n"
            + RecordingLabel(status.Recording) + "\n"
            + status.Measured + " / " + status.Targets + "\n"
            + status.Definition.Provenance + " · " + status.Definition.Source + "\n";
        if (status.Definition.Acquisition is { } unit)
            text += unit + "\n";
        if (status.Definition.RequiredDlc is { } dlc)
            text += dlc + "\n";
        if (status.Definition.OwnerPackageId is { } owner)
            text += owner + "\n";
        if (status.MissingColonyKeys.Count > 0)
        {
            text += "RWS_Status_MissingColonies".Translate(status.MissingColonyKeys.Count) + "\n";
            foreach (var key in status.MissingColonyKeys.Take(10))
            {
                var colony = stats.Colonies.FirstOrDefault(c => c.Key == key);
                text += (colony is null ? key : Dialog_RimworldStatistics.ColonyName(colony)) + "\n";
            }
        }
        return text;
    }

    private static void DrawCells(Rect rect, string[] cells, float[] widths)
    {
        var x = rect.x;
        var priorAnchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = new Rect(x + 4f, rect.y, Math.Max(0, widths[i] - 8f), rect.height);
            var shown = cells[i].Truncate(cell.width);
            Widgets.Label(cell, shown);
            if (shown != cells[i])
                TooltipHandler.TipRegion(cell, cells[i]);
            x += widths[i];
        }
        Text.Anchor = priorAnchor;
    }
}
