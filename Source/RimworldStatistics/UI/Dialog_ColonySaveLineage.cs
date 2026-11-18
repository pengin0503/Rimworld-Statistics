using System.Globalization;
using System.Text;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Read-only save lineage &amp; branch viewer for one colony (V2-B).
/// Draws edges strictly where embedded checkpoint ids prove parentage; unproven saves, missing parents and conflicted
/// checkpoints remain unconnected markers so guessed timelines are never presented as facts.
/// </summary>
internal sealed class Dialog_ColonySaveLineage : Window
{
    private const float RowHeight = 30f;
    private const float HeaderRowHeight = 25f;
    private const float LaneSpacing = 16f;

    private static readonly Color MainLineColor = new(0.35f, 0.68f, 0.86f);
    private static readonly Color BranchLineColor = new(0.92f, 0.64f, 0.34f);
    private static readonly Color PausedLineColor = new(0.68f, 0.58f, 0.88f);
    private static readonly Color CarriedLineColor = new(0.62f, 0.66f, 0.72f, 0.65f);
    private static readonly Color UnprovenOutline = new(0.66f, 0.69f, 0.74f);
    private static readonly Color MissingParentAccent = new(0.92f, 0.75f, 0.34f);
    private static readonly Color ConflictedColor = new(0.88f, 0.38f, 0.36f);
    private static readonly Color RepresentativeRing = new(0.98f, 0.84f, 0.36f);
    private static readonly Color HollowFill = new(0.12f, 0.13f, 0.15f);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record MetricChoice(string Key, MetricDefinition Definition, string Label, string? Source);
    private sealed record RowDisplay(string Day, string SaveLabel, string Status, string MetricValue, string Timestamp, string Tooltip);

    private readonly StatsView view;
    private readonly string? scopeName;
    private readonly string savesDirectory;
    private string colonyKey;
    private string colonyName = "";
    private Task<ColonySaveLineage>? lineageTask;
    private Task<SaveFileIndex?>? filesTask;
    private SaveFileIndex? files;

    private MetricChoice[] metricChoices = [];
    private MetricChoice? activeMetric;
    private object? cachedLanguage;
    private string? cachedNameSignature;

    private Vector2 tableScroll;
    private Vector2 detailScroll;
    private RowDisplay?[] rowCache = [];
    private ColonySaveLineage? cachedForLineage;
    private string? cachedForMetricKey;
    private SaveFileIndex? cachedForFiles;

    private int? selectedA;
    private int? selectedB;
    private string? detailCache;
    private (int? A, int? B, string? MetricKey, SaveFileIndex? Files, object? Lang) detailCacheKey;

    internal Dialog_ColonySaveLineage(
        StatsView view,
        string colonyKey,
        SaveFileIndex? files = null,
        string? initialMetricKey = null,
        string? scopeName = null)
    {
        this.view = view;
        this.colonyKey = colonyKey;
        this.files = files;
        this.scopeName = scopeName;
        savesDirectory = GenFilePaths.SavedGamesFolderPath;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;

        BuildMetricChoices(initialMetricKey);
        StartLoad(colonyKey);
        if (files is null)
        {
            var dir = savesDirectory;
            filesTask = Task.Run(() =>
            {
                try { return SaveFileIndex.Read(dir); }
                catch (Exception) { return null; }
            });
        }
    }

    public override Vector2 InitialSize => new(
        Math.Min(1180f, Verse.UI.screenWidth * 0.92f),
        Math.Min(760f, Verse.UI.screenHeight * 0.90f));

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            DrawContents(rect);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Colony save lineage window failed to draw.\n{e}", "RWS.lineage".GetHashCode());
            Widgets.Label(rect.ContractedBy(20f), "RWS_WindowError".Translate());
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }

    private void DrawContents(Rect rect)
    {
        if (filesTask is { IsCompleted: true } doneFiles)
        {
            filesTask = null;
            if (doneFiles.IsCompletedSuccessfully)
                files = doneFiles.Result;
            else
                _ = doneFiles.Exception;
        }

        var lang = LanguageDatabase.activeLanguage;
        var languageChanged = !ReferenceEquals(cachedLanguage, lang);
        if (languageChanged)
        {
            cachedLanguage = lang;
            BuildMetricChoices(activeMetric?.Key);
            InvalidateDisplay();
        }
        var nameSig = ColonyPresentation.Signature(Dialog_RimworldStatistics.ActivePreferences);
        if (languageChanged || cachedNameSignature != nameSig)
        {
            cachedNameSignature = nameSig;
            RefreshColonyName();
        }

        var titleText = (string)"RWS_Lineage_Title".Translate(colonyName);
        Text.Font = GameFont.Medium;
        var titleRect = new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 36f), 32f);
        var shownTitle = titleText.Truncate(titleRect.width);
        Widgets.Label(titleRect, shownTitle);
        if (shownTitle != titleText)
            TooltipHandler.TipRegion(titleRect, titleText);
        Text.Font = GameFont.Small;

        var y = rect.y + 36f;
        var controls = new ControlRow(new Rect(rect.x, y, rect.width, 28f));

        if (view.Colonies.Count > 1)
        {
            var switchLabel = (string)"RWS_Lineage_SwitchColony".Translate();
            var switchRect = controls.Next(switchLabel, 130f, 220f);
            if (Widgets.ButtonText(switchRect, switchLabel.Truncate(Math.Max(0f, switchRect.width - 12f))))
                ShowColonyMenu();
        }

        var reportLabel = (string)"RWS_Report_Open".Translate();
        var reportRect = controls.Next(reportLabel, 130f, 230f);
        if (Widgets.ButtonText(reportRect, reportLabel.Truncate(Math.Max(0f, reportRect.width - 12f))))
            UiScopes.OpenOrReplaceWindow(new Dialog_ColonySummaryReport(view, colonyKey, scopeName));
        TooltipHandler.TipRegion(reportRect, "RWS_Report_OpenTip".Translate());

        var chronicleLabel = (string)"RWS_Chronicle_Open".Translate();
        var chronicleRect = controls.Next(chronicleLabel, 120f, 215f);
        if (Widgets.ButtonText(chronicleRect, chronicleLabel.Truncate(Math.Max(0f, chronicleRect.width - 12f))))
            UiScopes.OpenOrReplaceWindow(new Dialog_ColonyChronicle(view, colonyKey, PickedChronicleBranch(), scopeName));
        TooltipHandler.TipRegion(chronicleRect, "RWS_Chronicle_OpenTip".Translate());

        if (activeMetric is { } metric)
        {
            var metricBtnText = (string)"RWS_Lineage_MetricButton".Translate(metric.Label);
            var metricRect = controls.Next(metricBtnText, 170f, 340f);
            var shownMetric = metricBtnText.Truncate(Math.Max(0f, metricRect.width - 12f));
            if (Widgets.ButtonText(metricRect, shownMetric))
                UiScopes.OpenOrReplaceWindow(new MetricPickerDialog(
                    metricChoices.Select(c => new MetricPickerChoice(c.Key, c.Label, c.Source)).ToArray(),
                    key => { if (metricChoices.FirstOrDefault(c => c.Key == key) is { } picked) SelectMetric(picked); },
                    selectedKey: metric.Key));
            if (shownMetric != metricBtnText)
                TooltipHandler.TipRegion(metricRect, metricBtnText);

            var colonyValue = ColonyComparisonValue(metric);
            var openChartLabel = (string)"RWS_Lineage_OpenChart".Translate();
            var openChartRect = controls.Next(openChartLabel, 125f, 220f);
            if (Widgets.ButtonText(openChartRect, openChartLabel.Truncate(Math.Max(0f, openChartRect.width - 12f)),
                active: colonyValue is not null) && colonyValue is { } val)
            {
                UiScopes.OpenOrReplaceWindow(new Dialog_ColonyMetricHistory(view, colonyKey, colonyName, metric.Definition, metric.Label, val, scopeName));
            }
        }

        var clearLabel = (string)"RWS_Lineage_ClearSelection".Translate();
        var clearRect = controls.Next(clearLabel, 110f, 190f);
        if (Widgets.ButtonText(clearRect, clearLabel.Truncate(Math.Max(0f, clearRect.width - 12f)),
            active: selectedA is not null || selectedB is not null))
        {
            selectedA = null;
            selectedB = null;
            detailCache = null;
        }

        y += controls.Height + 6f;
        var body = new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y));

        if (lineageTask is null || !lineageTask.IsCompleted)
        {
            Widgets.Label(body, "RWS_Loading".Translate());
            return;
        }
        if (!lineageTask.IsCompletedSuccessfully)
        {
            _ = lineageTask.Exception;
            Widgets.Label(body, "RWS_WindowError".Translate());
            return;
        }

        var lineage = lineageTask.Result;
        if (lineage.Nodes.Count == 0)
        {
            Widgets.Label(body, "RWS_Lineage_Empty".Translate());
            return;
        }

        EnsureRowCache(lineage);

        var summary = BuildSummaryLine(lineage);
        var summaryHeight = Math.Max(22f, Text.CalcHeight(summary, body.width));
        using (UiScopes.GuiState())
        {
            GUI.color = new Color(0.86f, 0.88f, 0.92f);
            Widgets.Label(new Rect(body.x, body.y, body.width, summaryHeight), summary);
        }

        var legendY = body.y + summaryHeight + 3f;
        DrawLegendRow(new Rect(body.x, legendY, body.width, 22f));

        var contentY = legendY + 25f;
        var remainingHeight = Math.Max(0f, body.yMax - contentY);
        var detailHeight = Math.Clamp(remainingHeight * 0.34f, 145f, 225f);
        var listHeight = Math.Max(80f, remainingHeight - detailHeight - 8f);

        DrawGraphAndRows(new Rect(body.x, contentY, body.width, listHeight), lineage);
        DrawDetailPanel(new Rect(body.x, contentY + listHeight + 8f, body.width, Math.Max(0f, body.yMax - (contentY + listHeight + 8f))), lineage);
    }

    private void StartLoad(string targetColonyKey)
    {
        colonyKey = targetColonyKey;
        RefreshColonyName();
        selectedA = null;
        selectedB = null;
        tableScroll = Vector2.zero;
        detailScroll = Vector2.zero;
        InvalidateDisplay();

        var session = ProfileService.Session;
        var currentView = view;
        lineageTask = Task.Run(() => session.ReadColonySaveLineage(currentView, targetColonyKey));
    }

    private void RefreshColonyName()
    {
        var stats = view.Stats.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        colonyName = stats is not null ? Dialog_RimworldStatistics.ColonyName(stats) : colonyKey;
    }

    /// <summary>
    /// The record picked in this view as a branch of the chronicle, by what it is rather than by its index (the chronicle
    /// reads the lineage again). Null for the representative line, which is the chronicle's own default.
    /// </summary>
    private ChronicleBranchKey? PickedChronicleBranch()
    {
        if ((selectedB ?? selectedA) is not { } picked || lineageTask is not { IsCompletedSuccessfully: true } done)
            return null;
        var lineage = done.Result;
        if (picked < 0 || picked >= lineage.Nodes.Count || picked == lineage.RepresentativeIndex)
            return null;
        return ChronicleBranchKey.Of(lineage.Nodes[picked]);
    }

    private void BuildMetricChoices(string? preferredKey)
    {
        var keys = LeaderboardBuilder.RankableKeys(view);
        metricChoices = keys.Select(key =>
        {
            if (BasicMetrics.Contains(key))
                return BasicMetrics.HasSaveHistory(key)
                    ? new MetricChoice(key, BasicMetrics.Definition(key), MetricText.PerColonyBasicLabel(key), null)
                    : null;
            var def = MetricLookup.Definition(view, key);
            return def is null || CrossColonyCatalog.IsDerived(def)
                ? null
                : new MetricChoice(key, def, MetricText.Label(def), MetricText.ModSourceLabel(def));
        }).Where(c => c is not null)
          .Select(c => c!)
          .OrderBy(c => c.Label, StringComparer.CurrentCulture)
          .ThenBy(c => c.Key, StringComparer.Ordinal)
          .ToArray();

        var wanted = preferredKey ?? BasicMetrics.LastWealth;
        activeMetric = metricChoices.FirstOrDefault(c => c.Key == wanted)
            ?? metricChoices.FirstOrDefault(c => c.Key == BasicMetrics.PeakWealth)
            ?? metricChoices.FirstOrDefault();
    }

    private void SelectMetric(MetricChoice choice)
    {
        activeMetric = choice;
        InvalidateDisplay();
    }

    private void InvalidateDisplay()
    {
        cachedForLineage = null;
        detailCache = null;
    }

    private void ShowColonyMenu()
    {
        if (view.Stats.Colonies.Count == 0)
            return;
        var currentColonyKey = Current.ProgramState == ProgramState.Playing
            ? InGameColonyAccess.Resolve(view, GameComponent_RimworldStatistics.CurrentOrNull?.campaignId).ColonyKey
            : null;
        UiScopes.OpenOrReplaceWindow(new ColonyPickerDialog(
            view.Stats.Colonies,
            Dialog_RimworldStatistics.ActivePreferences,
            picked => StartLoad(picked.Key),
            heading: (string)"RWS_Lineage_SwitchColony".Translate(),
            selectedColonyKey: colonyKey,
            currentInGameColonyKey: currentColonyKey));
    }

    private NumericValue? ColonyComparisonValue(MetricChoice choice)
    {
        if (BasicMetrics.Contains(choice.Key))
        {
            if (!BasicMetrics.HasSaveHistory(choice.Key))
                return null;
            var stats = view.Stats.Colonies.FirstOrDefault(c => c.Key == colonyKey);
            if (stats is null || BasicMetrics.Value(stats, choice.Key) is not { } n || !double.IsFinite(n))
                return null;
            return new NumericValue { Number = n, Coverage = MetricCoverage.SnapshotOnly };
        }

        var row = MetricLookup.ForColony(view, colonyKey, choice.Key);
        return row?.Value is { Number: { } d } nv && double.IsFinite(d) ? nv : null;
    }

    private void EnsureRowCache(ColonySaveLineage lineage)
    {
        var metricKey = activeMetric?.Key;
        if (ReferenceEquals(cachedForLineage, lineage) && cachedForMetricKey == metricKey && ReferenceEquals(cachedForFiles, files))
            return;
        cachedForLineage = lineage;
        cachedForMetricKey = metricKey;
        cachedForFiles = files;
        rowCache = new RowDisplay?[lineage.Nodes.Count];
        if (selectedA is null && lineage.RepresentativeIndex >= 0)
            selectedA = lineage.RepresentativeIndex;
    }

    private static string BuildSummaryLine(ColonySaveLineage lineage)
    {
        var stateLabel = lineage.SummaryLineage switch
        {
            CheckpointLineage.Linear => (string)"RWS_Lineage_State_SingleChain".Translate(),
            CheckpointLineage.Branched => (string)"RWS_Lineage_State_Branched".Translate(),
            CheckpointLineage.Unknown => (string)"RWS_Lineage_State_Gaps".Translate(),
            _ => (string)"RWS_Lineage_State_None".Translate(),
        };

        var parts = new List<string>
        {
            stateLabel,
            (string)"RWS_Lineage_SummaryNodes".Translate(lineage.Nodes.Count.ToString(Inv)),
        };
        if (lineage.BranchPointCount > 0)
            parts.Add((string)"RWS_Lineage_SummaryBranches".Translate(lineage.BranchPointCount.ToString(Inv)));
        if (lineage.CarriedSaveCount > 0)
            parts.Add((string)"RWS_Lineage_SummaryCarried".Translate(lineage.CarriedSaveCount.ToString(Inv)));
        if (lineage.MissingParentCount > 0)
            parts.Add((string)"RWS_Lineage_SummaryMissingParents".Translate(lineage.MissingParentCount.ToString(Inv)));
        if (lineage.UnprovenNodeCount > 0)
            parts.Add((string)"RWS_Lineage_SummaryUnproven".Translate(lineage.UnprovenNodeCount.ToString(Inv)));
        if (lineage.ConflictedNodeCount > 0)
            parts.Add((string)"RWS_Lineage_SummaryConflicted".Translate(lineage.ConflictedNodeCount.ToString(Inv)));
        if (lineage.OmittedSnapshotSaves > 0)
            parts.Add((string)"RWS_Lineage_SummaryCompacted".Translate(lineage.OmittedSnapshotSaves.ToString(Inv)));

        return string.Join(" · ", parts) + " — " + (string)"RWS_Lineage_ReadOnlyNote".Translate();
    }

    private static void DrawLegendRow(Rect rect)
    {
        var x = rect.x;
        void Item(Action<Rect> icon, string text)
        {
            var labelWidth = Text.CalcSize(text).x;
            var total = 22f + labelWidth + 14f;
            if (x + total > rect.xMax && x > rect.x)
                return;
            icon(new Rect(x, rect.y + 4f, 18f, 14f));
            Widgets.Label(new Rect(x + 21f, rect.y + 1f, labelWidth + 4f, 20f), text);
            x += total;
        }

        Item(r =>
        {
            Widgets.DrawLine(new Vector2(r.x, r.center.y), new Vector2(r.xMax, r.center.y), MainLineColor, 2f);
            Widgets.DrawBoxSolid(new Rect(r.center.x - 3.5f, r.center.y - 3.5f, 7f, 7f), MainLineColor);
        }, (string)"RWS_Lineage_LegendProven".Translate());

        Item(r =>
        {
            Widgets.DrawLine(new Vector2(r.x, r.center.y), new Vector2(r.xMax, r.center.y), BranchLineColor, 2f);
            Widgets.DrawBoxSolid(new Rect(r.center.x - 3.5f, r.center.y - 3.5f, 7f, 7f), BranchLineColor);
        }, (string)"RWS_Lineage_LegendBranch".Translate());

        Item(r =>
        {
            Widgets.DrawBoxSolid(new Rect(r.x + 1f, r.center.y - 0.5f, 4f, 1.5f), CarriedLineColor);
            Widgets.DrawBoxSolid(new Rect(r.x + 13f, r.center.y - 0.5f, 4f, 1.5f), CarriedLineColor);
            Widgets.DrawBoxSolidWithOutline(new Rect(r.center.x - 3f, r.center.y - 3f, 6f, 6f), CarriedLineColor, CarriedLineColor, 1);
        }, (string)"RWS_Lineage_LegendCarried".Translate());

        Item(r =>
        {
            Widgets.DrawBoxSolidWithOutline(new Rect(r.center.x - 3.5f, r.center.y - 3.5f, 7f, 7f), HollowFill, UnprovenOutline, 1);
        }, (string)"RWS_Lineage_LegendUnproven".Translate());
    }

    private void DrawGraphAndRows(Rect rect, ColonySaveLineage lineage)
    {
        Widgets.DrawMenuSection(rect);
        var inner = rect.ContractedBy(4f);
        var nodes = lineage.Nodes;
        var graphWidth = Math.Clamp((lineage.MaxLane + 1) * LaneSpacing + 18f, 48f, 280f);
        var minTotalWidth = graphWidth + 760f;
        var contentWidth = Math.Max(inner.width - 16f, minTotalWidth);

        var remaining = contentWidth - graphWidth;
        var colDay = Math.Max(92f, remaining * 0.13f);
        var colSave = Math.Max(185f, remaining * 0.27f);
        var colMetric = Math.Max(115f, remaining * 0.16f);
        var colTime = Math.Max(125f, remaining * 0.17f);
        var colStatus = Math.Max(150f, remaining - colDay - colSave - colMetric - colTime);

        // Fixed column header tracking horizontal scroll.
        using (UiScopes.Group(new Rect(inner.x, inner.y, Math.Max(0f, inner.width - 16f), HeaderRowHeight)))
        {
            DrawTableHeader(new Rect(-tableScroll.x, 0f, contentWidth, HeaderRowHeight),
                graphWidth, colDay, colSave, colStatus, colMetric, colTime);
        }

        var outRect = new Rect(inner.x, inner.y + HeaderRowHeight, inner.width, Math.Max(0f, inner.height - HeaderRowHeight));
        var viewRect = new Rect(0f, 0f, contentWidth, nodes.Count * RowHeight);
        using (UiScopes.ScrollView(outRect, ref tableScroll, viewRect))
        using (UiScopes.GuiState())
        {
            var firstRow = Math.Max(0, (int)(tableScroll.y / RowHeight) - 1);
            var lastRow = Math.Min(nodes.Count - 1, (int)((tableScroll.y + outRect.height) / RowHeight) + 1);
            var clipMinY = tableScroll.y - RowHeight * 2f;
            var clipMaxY = tableScroll.y + outRect.height + RowHeight * 2f;

            // 1. Row backgrounds & click targets for visible rows only.
            for (var i = firstRow; i <= lastRow; i++)
            {
                var rowRect = new Rect(0f, i * RowHeight, contentWidth, RowHeight);
                var isPicked = selectedA == i || selectedB == i;
                if (isPicked)
                    Widgets.DrawHighlightSelected(rowRect);
                else if (i % 2 == 1)
                    Widgets.DrawHighlight(rowRect, 0.2f);
                Widgets.DrawHighlightIfMouseover(rowRect);

                var display = rowCache[i] ??= BuildRowDisplay(lineage, i);
                TooltipHandler.TipRegion(rowRect, display.Tooltip);
                if (Widgets.ButtonInvisible(rowRect))
                    OnRowClicked(i);
            }

            // 2. Proven edges intersecting [firstRow, lastRow] (viewport-clipped so 3,000+ rows stay light).
            float LaneX(int lane) => 12f + lane * LaneSpacing;
            float RowY(int index) => index * RowHeight + RowHeight * 0.5f;

            foreach (var edge in lineage.Edges)
            {
                if (edge.ToIndex < firstRow || edge.FromIndex > lastRow)
                    continue;

                var fromNode = nodes[edge.FromIndex];
                var toNode = nodes[edge.ToIndex];
                var color = EdgeColor(edge, fromNode, toNode);
                var width = edge.Kind == SaveLineageEdgeKind.CarriedCheckpoint ? 1.2f
                    : fromNode.OnRepresentativeLine && toNode.OnRepresentativeLine ? 2.2f : 1.7f;

                var x0 = LaneX(edge.FromLane);
                var y0 = RowY(edge.FromIndex);
                var x1 = LaneX(edge.ToLane);
                var y1 = RowY(edge.ToIndex);

                if (edge.FromLane == edge.ToLane)
                {
                    DrawClippedSegment(x0, y0, x1, y1, clipMinY, clipMaxY, color, width, edge.Kind == SaveLineageEdgeKind.CarriedCheckpoint);
                }
                else
                {
                    var bendY = Math.Min(y1, y0 + 11f);
                    DrawClippedSegment(x0, y0, x1, bendY, clipMinY, clipMaxY, color, width, edge.Kind == SaveLineageEdgeKind.CarriedCheckpoint);
                    DrawClippedSegment(x1, bendY, x1, y1, clipMinY, clipMaxY, color, width, edge.Kind == SaveLineageEdgeKind.CarriedCheckpoint);
                }
            }

            // 3. Node markers & row text for visible rows only.
            for (var i = firstRow; i <= lastRow; i++)
            {
                var node = nodes[i];
                var center = new Vector2(LaneX(node.Lane), RowY(i));
                DrawNodeMarker(center, node, selectedA == i || selectedB == i);

                var display = rowCache[i] ??= BuildRowDisplay(lineage, i);
                var rowY = i * RowHeight;
                var x = graphWidth;

                Text.Anchor = TextAnchor.MiddleLeft;
                DrawCell(ref x, rowY, colDay, display.Day);
                DrawCell(ref x, rowY, colSave, display.SaveLabel);
                DrawCell(ref x, rowY, colStatus, display.Status);

                Text.Anchor = TextAnchor.MiddleRight;
                DrawCell(ref x, rowY, colMetric, display.MetricValue);

                Text.Anchor = TextAnchor.MiddleLeft;
                DrawCell(ref x, rowY, colTime, display.Timestamp);
            }
        }
    }

    private void DrawTableHeader(Rect rect, float graphWidth, float colDay, float colSave, float colStatus, float colMetric, float colTime)
    {
        using (UiScopes.GuiState())
        {
            GUI.color = new Color(0.80f, 0.82f, 0.86f);
            Text.Anchor = TextAnchor.MiddleLeft;

            var x = rect.x;
            Widgets.Label(new Rect(x + 4f, rect.y, graphWidth - 8f, rect.height),
                ((string)"RWS_Lineage_Col_Graph".Translate()).Truncate(Math.Max(0f, graphWidth - 8f)));
            x += graphWidth;

            void Header(float width, string label, bool right = false)
            {
                Text.Anchor = right ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                var shown = label.Truncate(Math.Max(0f, width - 8f));
                var cell = new Rect(x + 4f, rect.y, Math.Max(0f, width - 8f), rect.height);
                Widgets.Label(cell, shown);
                if (shown != label)
                    TooltipHandler.TipRegion(cell, label);
                x += width;
            }

            Header(colDay, "RWS_Lineage_Col_Day".Translate());
            Header(colSave, "RWS_Lineage_Col_Save".Translate());
            Header(colStatus, "RWS_Lineage_Col_Status".Translate());
            Header(colMetric, activeMetric?.Label ?? (string)"RWS_Details_Col_Value".Translate(), right: true);
            Header(colTime, "RWS_Lineage_Col_Recorded".Translate());

            Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(1f, 1f, 1f, 0.14f));
        }
    }

    private static void DrawCell(ref float x, float y, float width, string text)
    {
        var cell = new Rect(x + 4f, y, Math.Max(0f, width - 8f), RowHeight);
        Widgets.Label(cell, text.Truncate(cell.width));
        x += width;
    }

    private static Color EdgeColor(SaveLineageEdge edge, SaveLineageNode from, SaveLineageNode to) => edge.Kind switch
    {
        SaveLineageEdgeKind.CarriedCheckpoint => CarriedLineColor,
        SaveLineageEdgeKind.PausedResave => PausedLineColor,
        _ => from.OnRepresentativeLine && to.OnRepresentativeLine ? MainLineColor : BranchLineColor,
    };

    private static void DrawClippedSegment(float x0, float y0, float x1, float y1, float minY, float maxY, Color color, float width, bool dashed)
    {
        if (Math.Max(y0, y1) < minY || Math.Min(y0, y1) > maxY)
            return;

        // Clip vertical/near-vertical spans to the visible viewport band so off-screen geometry is minimal.
        if (Math.Abs(y1 - y0) > 0.01f)
        {
            if (y0 < minY)
            {
                var t = (minY - y0) / (y1 - y0);
                x0 += (x1 - x0) * t;
                y0 = minY;
            }
            else if (y0 > maxY)
            {
                var t = (maxY - y0) / (y1 - y0);
                x0 += (x1 - x0) * t;
                y0 = maxY;
            }
            if (y1 < minY)
            {
                var t = (minY - y0) / (y1 - y0);
                x1 = x0 + (x1 - x0) * t;
                y1 = minY;
            }
            else if (y1 > maxY)
            {
                var t = (maxY - y0) / (y1 - y0);
                x1 = x0 + (x1 - x0) * t;
                y1 = maxY;
            }
        }

        if (!dashed)
        {
            Widgets.DrawLine(new Vector2(x0, y0), new Vector2(x1, y1), color, width);
            return;
        }

        var delta = new Vector2(x1 - x0, y1 - y0);
        var length = delta.magnitude;
        if (length <= 0.5f)
            return;
        var dir = delta / length;
        const float dash = 4f;
        const float gap = 4f;
        for (var pos = 0f; pos < length; pos += dash + gap)
        {
            var end = Math.Min(length, pos + dash);
            Widgets.DrawLine(new Vector2(x0, y0) + dir * pos, new Vector2(x0, y0) + dir * end, color, width);
        }
    }

    private static void DrawNodeMarker(Vector2 center, SaveLineageNode node, bool selected)
    {
        if (selected)
            Widgets.DrawBoxSolidWithOutline(new Rect(center.x - 6.5f, center.y - 6.5f, 13f, 13f), HollowFill, Color.white, 1);

        if (node.IsRepresentative)
            Widgets.DrawBoxSolidWithOutline(new Rect(center.x - 5.5f, center.y - 5.5f, 11f, 11f), HollowFill, RepresentativeRing, 1);

        switch (node.Kind)
        {
            case SaveLineageNodeKind.Checkpoint:
            {
                var fill = node.OnRepresentativeLine ? MainLineColor : BranchLineColor;
                if (node.ParentState is SaveLineageParentState.MissingParent or SaveLineageParentState.InvalidOrConflictedParent)
                {
                    Widgets.DrawBoxSolidWithOutline(new Rect(center.x - 4.5f, center.y - 4.5f, 9f, 9f), HollowFill, MissingParentAccent, 1);
                    Widgets.DrawBoxSolid(new Rect(center.x - 2f, center.y - 2f, 4f, 4f), fill);
                }
                else
                {
                    Widgets.DrawBoxSolid(new Rect(center.x - 4f, center.y - 4f, 8f, 8f), fill);
                }
                break;
            }
            case SaveLineageNodeKind.CarriedCheckpointSave:
                Widgets.DrawBoxSolidWithOutline(new Rect(center.x - 3.5f, center.y - 3.5f, 7f, 7f), HollowFill, CarriedLineColor, 1);
                Widgets.DrawBoxSolid(new Rect(center.x - 1.5f, center.y - 1.5f, 3f, 3f), CarriedLineColor);
                break;
            case SaveLineageNodeKind.ConflictedCheckpoint:
                Widgets.DrawBoxSolidWithOutline(new Rect(center.x - 4.5f, center.y - 4.5f, 9f, 9f), HollowFill, ConflictedColor, 1);
                break;
            default:
                Widgets.DrawBoxSolidWithOutline(new Rect(center.x - 4f, center.y - 4f, 8f, 8f), HollowFill, UnprovenOutline, 1);
                break;
        }
    }

    private RowDisplay BuildRowDisplay(ColonySaveLineage lineage, int index)
    {
        var node = lineage.Nodes[index];
        var day = node.Kind == SaveLineageNodeKind.CarriedCheckpointSave && node.CapturedTick is { } cap && cap != node.SaveTick
            ? (string)"RWS_Lineage_DayCarried".Translate(Dialog_RimworldStatistics.Days(node.SaveTick), Dialog_RimworldStatistics.Days(cap))
            : Dialog_RimworldStatistics.Days(node.SaveTick);

        string saveLabel;
        if (node.Files.Count > 0)
        {
            saveLabel = node.Files[0].FileName;
            if (node.Files.Count > 1)
                saveLabel += $" (+{(node.Files.Count - 1).ToString(Inv)})";
        }
        else if (node.HasLiveSnapshotOnly)
        {
            saveLabel = (string)"RWS_LiveRecord".Translate();
        }
        else if (node.CheckpointId is { } cpId)
        {
            saveLabel = ShortId(cpId);
        }
        else
        {
            saveLabel = Dialog_RimworldStatistics.Dash;
        }

        var tags = BuildBadges(node);
        var status = string.Join(" · ", tags);

        var metricText = Dialog_RimworldStatistics.Dash;
        if (activeMetric is { } m && lineage.MetricAt(index, m.Definition) is { } val && val.Number is { } num && double.IsFinite(num))
            metricText = MetricText.Format(m.Definition, val);

        var timeText = Dialog_RimworldStatistics.Local(node.ObservedUtc);
        var tip = $"{day} — {saveLabel}\n{status}";
        return new RowDisplay(day, saveLabel, status, metricText, timeText, tip);
    }

    private static List<string> BuildBadges(SaveLineageNode node)
    {
        var badges = new List<string>(6);
        if (node.IsRepresentative)
            badges.Add((string)"RWS_Lineage_Badge_Representative".Translate());

        switch (node.Kind)
        {
            case SaveLineageNodeKind.Checkpoint:
                badges.Add(node.ParentState switch
                {
                    SaveLineageParentState.Root => (string)"RWS_Lineage_Badge_Root".Translate(),
                    SaveLineageParentState.ProvenParent => (string)"RWS_Lineage_Badge_Checkpoint".Translate(),
                    SaveLineageParentState.MissingParent => (string)"RWS_Lineage_Badge_MissingParent".Translate(),
                    _ => (string)"RWS_Lineage_Badge_InvalidParent".Translate(),
                });
                break;
            case SaveLineageNodeKind.CarriedCheckpointSave:
                badges.Add((string)"RWS_Lineage_Badge_Carried".Translate());
                break;
            case SaveLineageNodeKind.ConflictedCheckpoint:
                badges.Add((string)"RWS_Lineage_Badge_Conflicted".Translate());
                break;
            default:
                badges.Add((string)"RWS_Lineage_Badge_Unproven".Translate());
                break;
        }

        if (node.IsBranchPoint)
            badges.Add((string)"RWS_Lineage_Badge_BranchPoint".Translate());
        else if (node.IsAlternateBranch)
            badges.Add((string)"RWS_Lineage_Badge_Branch".Translate());

        if (node.HasRewindEvidence)
            badges.Add((string)"RWS_Lineage_Badge_Rewind".Translate());
        if (node.SameTickParent)
            badges.Add((string)"RWS_Lineage_Badge_SameTick".Translate());
        if (node.ArchiveState == SaveLineageArchiveState.RestoredFromArchive)
            badges.Add((string)"RWS_Lineage_Badge_RestoredArchive".Translate());
        else if (node.ArchiveState == SaveLineageArchiveState.ArchivedUnavailable)
            badges.Add((string)"RWS_Lineage_Badge_ArchivedMissing".Translate());

        if (node.GameVersionChangedFromParent)
            badges.Add((string)"RWS_Lineage_Badge_VersionChanged".Translate());
        if (node.ModSetChangedFromParent)
            badges.Add((string)"RWS_Lineage_Badge_ModChanged".Translate());
        if (node.DlcChangedFromParent)
            badges.Add((string)"RWS_Lineage_Badge_DlcChanged".Translate());

        if (node.VerifiedParentInterval is { } iv && (iv.RaidsIncrease > 0 || iv.ColonistDeathsIncrease > 0))
            badges.Add((string)"RWS_Lineage_Badge_VerifiedInterval".Translate(
                iv.RaidsIncrease.ToString(Inv), iv.ColonistDeathsIncrease.ToString(Inv)));

        return badges;
    }

    private void OnRowClicked(int index)
    {
        if (selectedA == index)
        {
            selectedA = selectedB;
            selectedB = null;
        }
        else if (selectedB == index)
        {
            selectedB = null;
        }
        else if (selectedA is null)
        {
            selectedA = index;
        }
        else if (selectedB is null)
        {
            selectedB = index;
        }
        else
        {
            selectedA = index;
            selectedB = null;
        }
        detailCache = null;
        detailScroll = Vector2.zero;
    }

    private void DrawDetailPanel(Rect rect, ColonySaveLineage lineage)
    {
        Widgets.DrawMenuSection(rect);
        var inner = rect.ContractedBy(8f);
        if (inner.width <= 10f || inner.height <= 10f)
            return;

        var key = (selectedA, selectedB, activeMetric?.Key, files, LanguageDatabase.activeLanguage);
        if (detailCache is null || detailCacheKey != key)
        {
            detailCacheKey = key;
            detailCache = BuildDetailText(lineage);
        }

        var contentWidth = Math.Max(1f, inner.width - 16f);
        var textHeight = Math.Max(inner.height, Text.CalcHeight(detailCache, contentWidth) + 4f);
        using (UiScopes.ScrollView(inner, ref detailScroll, new Rect(0f, 0f, contentWidth, textHeight)))
        {
            Widgets.Label(new Rect(0f, 0f, contentWidth, textHeight), detailCache);
        }
    }

    private string BuildDetailText(ColonySaveLineage lineage)
    {
        if (selectedA is not { } a || a < 0 || a >= lineage.Nodes.Count)
            return (string)"RWS_Lineage_SelectHint".Translate();

        if (selectedB is { } b && b >= 0 && b < lineage.Nodes.Count && b != a)
            return BuildComparisonText(lineage, a, b);

        return BuildSingleNodeText(lineage, a) + "\n\n" + (string)"RWS_Lineage_CompareHint".Translate();
    }

    private string BuildSingleNodeText(ColonySaveLineage lineage, int index)
    {
        var node = lineage.Nodes[index];
        var sb = new StringBuilder();
        sb.AppendLine((string)"RWS_Lineage_Detail_Header".Translate(
            Dialog_RimworldStatistics.DaysWithYears(node.SaveTick),
            node.SaveTick.ToString("N0", Inv),
            Dialog_RimworldStatistics.Local(node.ObservedUtc)));

        sb.AppendLine((string)"RWS_Lineage_Detail_Status".Translate(string.Join(" · ", BuildBadges(node))));

        if (node.CapturedTick is { } cap && cap != node.SaveTick)
            sb.AppendLine((string)"RWS_Lineage_Detail_CapturedAt".Translate(
                Dialog_RimworldStatistics.Days(cap), cap.ToString("N0", Inv)));
        if (node.CarriedThroughSaveTick is { } through && through > node.SaveTick)
            sb.AppendLine((string)"RWS_Lineage_Detail_CarriedThrough".Translate(
                Dialog_RimworldStatistics.Days(through), through.ToString("N0", Inv)));

        if (node.CheckpointId is { } cpId)
            sb.AppendLine((string)"RWS_Lineage_Detail_CheckpointId".Translate(ShortId(cpId)));
        if (node.ParentState is SaveLineageParentState.MissingParent or SaveLineageParentState.InvalidOrConflictedParent
            && node.RawParentCheckpointId is { } rawParent)
            sb.AppendLine((string)"RWS_Lineage_Detail_MissingParentId".Translate(ShortId(rawParent)));

        var version = node.GameVersion ?? Dialog_RimworldStatistics.Dash;
        var dlcText = node.ActiveDlcIds.Count == 0
            ? Dialog_RimworldStatistics.Dash
            : string.Join(", ", node.ActiveDlcIds.Select(MetricText.DlcDisplayName));
        sb.AppendLine((string)"RWS_Lineage_Detail_Environment".Translate(version, dlcText));

        if (node.VerifiedParentInterval is { } iv)
            sb.AppendLine((string)"RWS_Lineage_Detail_VerifiedInterval".Translate(
                Dialog_RimworldStatistics.Days(iv.StartTick),
                Dialog_RimworldStatistics.Days(iv.EndTick),
                iv.RaidsIncrease.ToString("N0", Inv),
                iv.ColonistDeathsIncrease.ToString("N0", Inv)));

        if (node.Files.Count > 0)
        {
            var formattedFiles = node.Files.Select(f =>
            {
                var presence = files is null
                    ? Dialog_RimworldStatistics.Dash
                    : f.Matches(files) ? (string)"RWS_Yes".Translate() : (string)"RWS_FileMissing".Translate();
                var superseded = f.Superseded ? " [" + (string)"RWS_Col_Superseded".Translate() + "]" : "";
                return $"{f.FileName}{superseded} ({presence})";
            });
            sb.Append((string)"RWS_Lineage_Detail_Files".Translate(string.Join(", ", formattedFiles)));
        }
        else if (node.HasLiveSnapshotOnly)
        {
            sb.Append((string)"RWS_Lineage_Detail_Files".Translate((string)"RWS_LiveRecord".Translate()));
        }

        return sb.ToString().TrimEnd();
    }

    private string BuildComparisonText(ColonySaveLineage lineage, int indexA, int indexB)
    {
        var cmp = lineage.CompareNodes(indexA, indexB);
        var sb = new StringBuilder();

        var relationLine = cmp.Relation switch
        {
            SaveLineageRelation.DirectParentChild => (string)"RWS_Lineage_Rel_DirectParent".Translate(
                Dialog_RimworldStatistics.Days(cmp.Earlier.SaveTick), Dialog_RimworldStatistics.Days(cmp.Later.SaveTick)),
            SaveLineageRelation.ProvenLineage => (string)"RWS_Lineage_Rel_ProvenChain".Translate(
                Dialog_RimworldStatistics.Days(cmp.Earlier.SaveTick), Dialog_RimworldStatistics.Days(cmp.Later.SaveTick),
                cmp.Steps.ToString(Inv)),
            SaveLineageRelation.SameCheckpointCarried => (string)"RWS_Lineage_Rel_CarriedSame".Translate(
                Dialog_RimworldStatistics.Days(cmp.Earlier.SaveTick), Dialog_RimworldStatistics.Days(cmp.Later.SaveTick)),
            SaveLineageRelation.SiblingBranches => (string)"RWS_Lineage_Rel_SiblingBranches".Translate(
                Dialog_RimworldStatistics.Days(lineage.Nodes[cmp.CommonAncestorIndex].SaveTick)),
            SaveLineageRelation.SharedCarriedOrigin => (string)"RWS_Lineage_Rel_SharedCarriedOrigin".Translate(
                Dialog_RimworldStatistics.Days(lineage.Nodes[cmp.CommonAncestorIndex].SaveTick)),
            _ => (string)"RWS_Lineage_Rel_Unproven".Translate(),
        };
        sb.AppendLine(relationLine);

        if (cmp.ModSetChangedAlongPath || cmp.GameVersionChangedAlongPath || cmp.DlcChangedAlongPath)
        {
            var envChanges = new List<string>(3);
            if (cmp.GameVersionChangedAlongPath)
                envChanges.Add((string)"RWS_Lineage_Badge_VersionChanged".Translate());
            if (cmp.ModSetChangedAlongPath)
                envChanges.Add((string)"RWS_Lineage_Badge_ModChanged".Translate());
            if (cmp.DlcChangedAlongPath)
                envChanges.Add((string)"RWS_Lineage_Badge_DlcChanged".Translate());
            sb.AppendLine((string)"RWS_Lineage_Rel_EnvChanges".Translate(string.Join(", ", envChanges)));
        }

        if (cmp.DirectCounterInterval is { } iv)
        {
            sb.AppendLine((string)"RWS_Lineage_Detail_VerifiedInterval".Translate(
                Dialog_RimworldStatistics.Days(iv.StartTick),
                Dialog_RimworldStatistics.Days(iv.EndTick),
                iv.RaidsIncrease.ToString("N0", Inv),
                iv.ColonistDeathsIncrease.ToString("N0", Inv)));
        }

        if (activeMetric is { } metric)
        {
            sb.AppendLine();
            sb.AppendLine(metric.Label + ":");
            var change = lineage.CompareMetric(metric.Definition, indexA, indexB);
            sb.Append(Dialog_ColonyMetricHistory.FormatHistoryChange(metric.Definition, change));
        }

        return sb.ToString().TrimEnd();
    }

    private static string ShortId(string id) => id.Length <= 12 ? id : id[..12] + "…";
}
