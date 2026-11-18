using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal sealed class Dialog_ColonyMetricHistory : Window
{
    private static readonly Color SeriesColor = new(0.35f, 0.64f, 0.78f);
    private static readonly Color PartialColor = new(0.63f, 0.66f, 0.71f);
    private static readonly Color GridColor = new(0.7f, 0.7f, 0.7f, 0.28f);
    private static readonly Color HollowFill = new(0.12f, 0.13f, 0.15f);
    private static readonly Color GapColor = new(0.7f, 0.7f, 0.7f, 0.6f);
    private readonly MetricDefinition definition;
    private readonly string heading;
    private readonly string recordLabel;
    private readonly string colonyKey;
    private readonly string colonyName;
    private readonly string? modSource;
    private readonly StatsView view;
    private readonly string? scopeName;
    private readonly string directory;
    private readonly HistoryOverlayTracker overlayTracker;
    private readonly Dictionary<long, Task<MetricHistory>> overlayTasks = [];
    private static readonly Color[] OverlayColors = [new(0.95f, 0.64f, 0.34f), new(0.65f, 0.8f, 0.42f)];
    private readonly Task<MetricHistory> pending;
    private string? historyNote;
    private MetricHistoryPoint[]? measured;
    private MetricHistoryPoint[]? visiblePrimaryMeasured;
    private IReadOnlyList<MetricHistoryPoint> slicedPrimaryPoints = [];
    private int visibleTotalMeasuredCount;
    private HistoryPlotBounds fullBounds;
    private HistoryPlotBounds bounds;
    private bool boundsDirty = true;
    private IReadOnlyList<MetricHistoryPoint> plotPoints = [];
    private int[]? gapTicks;   // ticks of drawn readings without a value, ascending
    private int plotWidth;
    private Vector2 chartScroll;
    // Screen-local day-range zoom (kept separate from the two-reading comparison below and never persisted).
    private string zoomStartInput = "";
    private string zoomEndInput = "";
    private HistoryTickRange? zoomRange;
    private bool zoomInputInvalid;
    // Two readings of this colony compared (#307): the first one clicked, then the change once a second one is.
    private int? pickFrom;
    private HistoryChange? change;
    private string? changeText;
    private (int From, int To)? firstAndLast;
    private bool firstAndLastKnown;
    private static readonly Color PickColor = new(1f, 1f, 1f, 0.85f);
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    internal Dialog_ColonyMetricHistory(StatsView view, string colonyKey, string colonyName,
        MetricDefinition definition, string metricLabel, NumericValue comparison, string? scopeName = null)
    {
        this.definition = definition;
        this.view = view;
        this.scopeName = scopeName;
        this.colonyKey = colonyKey;
        overlayTracker = new HistoryOverlayTracker(colonyKey, OverlayColors.Length);
        this.colonyName = colonyName;
        modSource = MetricText.ModSourceLabel(definition);
        heading = "RWS_Chart_Title".Translate(metricLabel);
        recordLabel = "RWS_Chart_HistoryRecord".Translate(MetricText.Format(definition, comparison));
        directory = GenFilePaths.SavedGamesFolderPath;
        pending = Task.Run(() => ProfileService.Session.ReadChartHistory(view, colonyKey, definition, directory));
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
    }

    public override Vector2 InitialSize => new(Math.Min(930f, Verse.UI.screenWidth * 0.88f), Math.Min(605f, Verse.UI.screenHeight * 0.85f));

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            DrawContents(rect);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Colony metric history window failed to draw.\n{e}", "RWS.history".GetHashCode());
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

    private string OverlayColonyName(HistoryOverlayTracker.Entry overlay)
        => view.Stats.Colonies.FirstOrDefault(c => c.Key == overlay.ColonyKey) is { } stats
            ? Dialog_RimworldStatistics.ColonyName(stats)
            : overlay.ColonyName;

    private void DrawContents(Rect rect)
    {
        Text.Font = GameFont.Medium;
        var titleRect = new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 32f), 32f);
        var shownTitle = heading.Truncate(titleRect.width);
        Widgets.Label(titleRect, shownTitle);
        if (shownTitle != heading)
            TooltipHandler.TipRegion(titleRect, heading);
        Text.Font = GameFont.Small;
        var colonyStats = view.Stats.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        var activePreferences = Dialog_RimworldStatistics.ActivePreferences;
        var currentColonyName = colonyStats is not null
            ? Dialog_RimworldStatistics.ColonyName(colonyStats)
            : colonyName;
        var colonyTip = colonyStats is not null && ColonyPresentation.HasCustomName(activePreferences, colonyStats)
            ? currentColonyName + "\n" + (string)"RWS_ColonyName_Original".Translate(Dialog_RimworldStatistics.OriginalColonyName(colonyStats))
            : currentColonyName;
        var colony = new Rect(rect.x, rect.y + 35f, rect.width, 26f);
        Widgets.Label(colony, currentColonyName.Truncate(colony.width));
        TooltipHandler.TipRegion(colony, colonyTip);
        var sourceOffset = modSource is null ? 0f : 20f;
        if (modSource is { } source)
        {
            var sourceRect = new Rect(rect.x, rect.y + 62f, rect.width, 20f);
            var shownSource = source.Truncate(sourceRect.width);
            Widgets.Label(sourceRect, shownSource);
            if (shownSource != source)
                TooltipHandler.TipRegion(sourceRect, source);
        }
        Widgets.Label(new Rect(rect.x, rect.y + 63f + sourceOffset, rect.width, 26f), recordLabel);
        var bodyY = rect.y + 94f + sourceOffset;
        var body = new Rect(rect.x, bodyY, rect.width, Math.Max(0, rect.yMax - bodyY));
        if (!pending.IsCompleted)
        {
            Widgets.Label(body, "RWS_Loading".Translate());
            return;
        }
        if (!pending.IsCompletedSuccessfully)
        {
            _ = pending.Exception;
            Widgets.Label(body, "RWS_WindowError".Translate());
            return;
        }

        var history = pending.Result;
        if (measured is null)
        {
            measured = history.Points.Where(p => p.Value is not null).ToArray();
            boundsDirty = true;
        }
        if (measured.Length == 0)
        {
            Widgets.Label(body, "RWS_Chart_HistoryEmpty".Translate());
            return;
        }

        PollOverlayTasks(history.Kind);

        if (body.width <= 20f || body.height <= 0f)
            return;

        var contentWidth = Math.Max(1f, body.width - 16f);
        var note = historyNote ??= Describe(history, measured);
        var noteHeight = Text.CalcHeight(note, contentWidth);
        EnsureBoundsAndLegend(history, contentWidth);
        var legendHeight = Math.Max(LegendRowHeight, legendPlan?.TotalHeight ?? LegendRowHeight);
        const float overlayRowHeight = 23f;
        var isFavorite = ColonyHistoryFavorites.IsFavorite(
            activePreferences, colonyKey, colonyStats?.AliasKeys, definition.Key);
        string addColonyText = "RWS_Chart_AddColony".Translate();
        var favText = (isFavorite ? "★ " : "☆ ") + (string)(isFavorite
            ? "RWS_ColonyFav_MenuColonyRemove".Translate(currentColonyName)
            : "RWS_ColonyFav_MenuColonyAdd".Translate(currentColonyName));
        var topMeasure = new ControlRow(new Rect(0f, 0f, contentWidth, 28f));
        _ = topMeasure.Next(addColonyText, 120f, 250f);
        _ = topMeasure.Next(favText, 140f, 360f);
        var topControlsHeight = topMeasure.Height + 3f;
        var zoomStatus = ZoomStatusText();
        var zoomHeight = MeasureZoomControls(contentWidth, zoomStatus);
        var changeLines = ChangeText(history);
        // Room for three lines (an interval and a reason) is kept, so picking a reading does not move the chart under the mouse.
        var changeTextHeight = Math.Max(Text.CalcHeight(changeLines, contentWidth), Text.LineHeight * 3f);
        var changeHeight = 31f + changeTextHeight + 6f;
        var controlsHeight = topControlsHeight + overlayRowHeight * overlayTracker.Entries.Count + zoomHeight + changeHeight;
        var vertical = HistoryLegendLayout.ResolveVertical(
            body.height - controlsHeight, noteHeight, legendHeight);
        var scrollHeight = controlsHeight + vertical.ContentHeight;

        // The whole dynamic content scrolls: adding overlays or using a longer
        // translation cannot silently remove the graph or the legend on small screens.
        using (UiScopes.ScrollView(body, ref chartScroll, new Rect(0f, 0f, contentWidth, scrollHeight)))
        {
            var topControls = new ControlRow(new Rect(0f, 0f, contentWidth, 28f));
            var add = topControls.Next(addColonyText, 120f, 250f);
            if (Widgets.ButtonText(add, addColonyText.Truncate(Math.Max(0f, add.width - 12f)), active: overlayTracker.CanAdd))
            {
                var candidates = view.Stats.Colonies
                    .Where(c => !c.Excluded && !c.OutOfScope && !overlayTracker.IsSelected(c.Key))
                    .ToList();
                if (candidates.Count > 0)
                {
                    UiScopes.OpenOrReplaceWindow(new ColonyPickerDialog(candidates, activePreferences, c =>
                    {
                        var name = Dialog_RimworldStatistics.ColonyName(c);
                        if (!overlayTracker.TryAdd(c.Key, name, out var entry) || entry is null)
                            return;
                        var task = Task.Run(() => ProfileService.Session.ReadChartHistory(view, c.Key, definition, directory));
                        task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
                        overlayTasks[entry.RequestId] = task;
                        InvalidateOverlays();
                    }, addColonyText));
                }
            }
            var favButton = topControls.Next(favText, 140f, 360f);
            var shownFavText = favText.Truncate(Math.Max(0f, favButton.width - 12f));
            if (Widgets.ButtonText(favButton, shownFavText))
            {
                Dialog_RimworldStatistics.UpdateActivePreferences(prefs =>
                    ColonyHistoryFavorites.Toggle(prefs, colonyKey, colonyStats?.AliasKeys, definition.Key));
            }
            TooltipHandler.TipRegion(favButton, favText);

            var y = topControls.Height + 3f;
            string? removeKey = null;
            foreach (var overlay in overlayTracker.Entries)
            {
                var row = new Rect(0f, y, contentWidth, overlayRowHeight);
                var color = OverlayColors[overlay.ColorIndex];
                Widgets.DrawBoxSolid(new Rect(row.x, row.y + 5f, 12f, 12f), color);

                var status = overlay.Status switch
                {
                    HistoryOverlayStatus.Loading => (string)"RWS_Loading".Translate(),
                    HistoryOverlayStatus.Empty => (string)"RWS_Chart_HistoryEmpty".Translate(),
                    HistoryOverlayStatus.Incompatible => (string)"RWS_Chart_OverlayOtherSource".Translate(),
                    _ => "",
                };
                var overlayDisplayName = OverlayColonyName(overlay);
                var label = status.Length == 0 ? overlayDisplayName : overlayDisplayName + " · " + status;

                const float removeWidth = 24f;
                var removeRect = new Rect(row.xMax - removeWidth, row.y + 1f, removeWidth, 21f);
                if (Widgets.ButtonText(removeRect, "×"))
                    removeKey = overlay.ColonyKey;
                TooltipHandler.TipRegion(removeRect, "RWS_Chart_RemoveOverlay".Translate());

                var labelWidth = Math.Max(0f, removeRect.x - (row.x + 18f) - 6f);
                var labelRect = new Rect(row.x + 18f, row.y, labelWidth, row.height);
                Widgets.Label(labelRect, label.Truncate(labelRect.width));
                if (Text.CalcSize(label).x > labelRect.width)
                    TooltipHandler.TipRegion(labelRect, label);

                y = row.yMax;
            }

            if (removeKey is not null)
            {
                var removed = overlayTracker.Entries.FirstOrDefault(e => string.Equals(e.ColonyKey, removeKey, StringComparison.Ordinal));
                if (removed is not null)
                    overlayTasks.Remove(removed.RequestId);
                if (overlayTracker.Remove(removeKey))
                    InvalidateOverlays();
            }

            y = DrawZoomControls(history, y, contentWidth, zoomStatus);
            if (boundsDirty)
            {
                EnsureBoundsAndLegend(history, contentWidth);
                changeLines = ChangeText(history);
            }
            y = DrawChangeControls(history, y, contentWidth, changeLines, changeTextHeight);
            Widgets.Label(new Rect(0f, y, contentWidth, noteHeight), note);
            var axisWidth = Math.Min(145f, contentWidth * 0.28f);
            var plot = new Rect(axisWidth, y + noteHeight + 13f,
                contentWidth - axisWidth - 16f, vertical.PlotHeight);
            if (plot.width >= 10f)
            {
                DrawPlot(plot, axisWidth, history, currentColonyName);
                DrawLegend(new Rect(0f, plot.yMax + 46f, contentWidth, legendHeight));
            }
        }
    }

    private readonly record struct ZoomControlsLayout(
        Rect LabelRect,
        Rect StartRect,
        Rect SeparatorRect,
        Rect EndRect,
        Rect ApplyRect,
        Rect ResetRect,
        Rect StatusRect,
        float TotalHeight);

    private static ZoomControlsLayout ComputeZoomControlsLayout(
        float y, float width, string labelText, string applyText, string resetText, string statusText)
    {
        var row = new ControlRow(new Rect(0f, y, width, 28f));
        var labelRect = row.Next(Math.Min(width, Text.CalcSize(labelText).x + 4f));
        var groupWidth = Math.Min(width, 170f);
        var groupRect = row.Next(groupWidth);
        var sepWidth = Math.Min(18f, groupRect.width * 0.14f);
        var fieldWidth = Math.Max(20f, (groupRect.width - sepWidth) * 0.5f);
        var startRect = new Rect(groupRect.x, groupRect.y + 1f, fieldWidth, 26f);
        var separatorRect = new Rect(startRect.xMax, groupRect.y, sepWidth, 28f);
        var endRect = new Rect(separatorRect.xMax, groupRect.y + 1f, fieldWidth, 26f);
        var applyRect = row.Next(applyText, 96f, 220f);
        var resetRect = row.Next(resetText, 96f, 220f);
        var statusY = y + row.Height + 3f;
        var statusHeight = Math.Max(Text.LineHeight, Text.CalcHeight(statusText, width));
        var statusRect = new Rect(0f, statusY, width, statusHeight);
        return new ZoomControlsLayout(
            labelRect, startRect, separatorRect, endRect, applyRect, resetRect, statusRect,
            row.Height + 3f + statusHeight + 6f);
    }

    private static float MeasureZoomControls(float width, string statusText)
        => ComputeZoomControlsLayout(
            0f, width,
            "RWS_Chart_ZoomLabel".Translate(),
            "RWS_Chart_ZoomApply".Translate(),
            "RWS_Chart_ZoomReset".Translate(),
            statusText).TotalHeight;

    private float DrawZoomControls(MetricHistory history, float y, float width, string statusText)
    {
        string labelText = "RWS_Chart_ZoomLabel".Translate();
        string applyText = "RWS_Chart_ZoomApply".Translate();
        string resetText = "RWS_Chart_ZoomReset".Translate();
        var layout = ComputeZoomControlsLayout(y, width, labelText, applyText, resetText, statusText);

        string nextStart;
        string nextEnd;
        using (UiScopes.GuiState())
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            var shownLabel = labelText.Truncate(layout.LabelRect.width);
            Widgets.Label(layout.LabelRect, shownLabel);
            if (shownLabel != labelText)
                TooltipHandler.TipRegion(layout.LabelRect, labelText);

            Text.Anchor = TextAnchor.UpperLeft;
            nextStart = Widgets.TextField(layout.StartRect, zoomStartInput) ?? "";
            TooltipHandler.TipRegion(layout.StartRect, "RWS_Chart_ZoomStartTip".Translate());

            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(layout.SeparatorRect, "–");

            Text.Anchor = TextAnchor.UpperLeft;
            nextEnd = Widgets.TextField(layout.EndRect, zoomEndInput) ?? "";
            TooltipHandler.TipRegion(layout.EndRect, "RWS_Chart_ZoomEndTip".Translate());
        }

        if (nextStart != zoomStartInput || nextEnd != zoomEndInput)
        {
            zoomStartInput = nextStart;
            zoomEndInput = nextEnd;
            zoomInputInvalid = false;
        }

        var hasInput = !string.IsNullOrWhiteSpace(zoomStartInput) || !string.IsNullOrWhiteSpace(zoomEndInput);
        var shownApply = applyText.Truncate(Math.Max(0f, layout.ApplyRect.width - 12f));
        if (Widgets.ButtonText(layout.ApplyRect, shownApply, active: hasInput) && hasInput)
            ApplyZoomInput(history);
        TooltipHandler.TipRegion(layout.ApplyRect, "RWS_Chart_ZoomApplyTip".Translate());

        var canReset = zoomRange is not null || hasInput || zoomInputInvalid;
        var shownReset = resetText.Truncate(Math.Max(0f, layout.ResetRect.width - 12f));
        if (Widgets.ButtonText(layout.ResetRect, shownReset, active: canReset) && canReset)
            ResetZoom(history);
        TooltipHandler.TipRegion(layout.ResetRect, "RWS_Chart_ZoomResetTip".Translate());

        using (UiScopes.GuiState())
        {
            if (zoomInputInvalid)
                GUI.color = ColorLibrary.RedReadable;
            Widgets.Label(layout.StatusRect, statusText);
        }

        return y + layout.TotalHeight;
    }

    private string ZoomStatusText()
    {
        if (zoomInputInvalid)
            return "RWS_Chart_ZoomInvalid".Translate();
        var fullFrom = Dialog_RimworldStatistics.Days(fullBounds.FirstTick);
        var fullTo = Dialog_RimworldStatistics.Days(fullBounds.LastTick);
        if (zoomRange is not { } range)
            return "RWS_Chart_ZoomStatusFull".Translate(fullFrom, fullTo);
        var zoomFrom = Dialog_RimworldStatistics.Days(range.StartTick);
        var zoomTo = Dialog_RimworldStatistics.Days(range.EndTick);
        return visibleTotalMeasuredCount == 0
            ? (string)"RWS_Chart_ZoomStatusEmpty".Translate(zoomFrom, zoomTo, fullFrom, fullTo)
            : (string)"RWS_Chart_ZoomStatusActive".Translate(zoomFrom, zoomTo, fullFrom, fullTo);
    }

    private void ApplyZoomInput(MetricHistory history)
    {
        var resolution = HistoryPlotRange.Resolve(zoomStartInput, zoomEndInput, fullBounds.FirstTick, fullBounds.LastTick);
        if (resolution.Status == HistoryRangeStatus.InvalidInput)
        {
            zoomInputInvalid = true;
            return;
        }
        zoomInputInvalid = false;
        if (resolution.WasReversed)
            (zoomStartInput, zoomEndInput) = (zoomEndInput.Trim(), zoomStartInput.Trim());
        SetZoomRange(history, resolution.Range);
    }

    private void ResetZoom(MetricHistory history)
    {
        zoomStartInput = "";
        zoomEndInput = "";
        zoomInputInvalid = false;
        SetZoomRange(history, null);
    }

    private void SetZoomRange(MetricHistory history, HistoryTickRange? nextRange)
    {
        if (zoomRange == nextRange)
            return;
        zoomRange = nextRange;
        (pickFrom, change) = HistoryChanges.FilterSelection(history, pickFrom, change, zoomRange);
        firstAndLastKnown = false;
        changeText = null;
        plotWidth = 0;
        gapTicks = null;
        InvalidateOverlays();
    }

    /// <summary>The buttons and lines of the two-reading comparison (#307); returns the y below them.</summary>
    private float DrawChangeControls(MetricHistory history, float y, float width, string lines, float linesHeight)
    {
        var x = 0f;
        bool Button(string label, string? tip, bool active)
        {
            var rect = new Rect(x, y, Math.Min(Math.Max(110f, Text.CalcSize(label).x + 20f), Math.Max(0f, width - x)), 28f);
            x = rect.xMax + 6f;
            var clicked = Widgets.ButtonText(rect, label.Truncate(Math.Max(0f, rect.width - 12f)), active: active);
            if (tip is not null)
                TooltipHandler.TipRegion(rect, tip);
            return clicked && active;
        }

        if (!firstAndLastKnown)
        {
            firstAndLast = HistoryChanges.FirstAndLast(history, zoomRange);
            firstAndLastKnown = true;
        }
        if (Button("RWS_Change_FirstLast".Translate(), "RWS_Change_FirstLastTip".Translate(), firstAndLast is not null)
            && firstAndLast is { } pair)
        {
            pickFrom = null;
            SetChange(HistoryChanges.Compare(definition, history, pair.From, pair.To));
        }
        if (Button("RWS_Change_Clear".Translate(), null, pickFrom is not null || change is not null))
        {
            pickFrom = null;
            SetChange(null);
        }
        if (Button("RWS_Lineage_Open".Translate(), "RWS_Lineage_OpenTip".Translate(), true))
            UiScopes.OpenOrReplaceWindow(new Dialog_ColonySaveLineage(view, colonyKey, null, definition.Key, scopeName));
        Widgets.Label(new Rect(0f, y + 31f, width, linesHeight), lines);
        return y + 31f + linesHeight + 6f;
    }

    /// <summary>A click on a reading of this colony: the first of two, or the second, which shows the change.</summary>
    private void Pick(MetricHistory history, int index)
    {
        if (!HistoryChanges.CanPick(history, index, zoomRange))
            return;
        if (pickFrom is { } first && change is null && HistoryChanges.CanPick(history, first, zoomRange))
        {
            if (first == index)
                return;
            pickFrom = null;
            SetChange(HistoryChanges.Compare(definition, history, first, index));
            return;
        }
        pickFrom = index;
        SetChange(null);
    }

    private void SetChange(HistoryChange? value)
    {
        change = value;
        changeText = null;
    }

    private string ChangeText(MetricHistory history)
    {
        if (changeText is not null)
            return changeText;
        var points = history.Points;
        if (change is not { } c)
        {
            if (pickFrom is { } first && HistoryChanges.CanPick(history, first, zoomRange) && points[first].Value is { } picked)
                return changeText = (string)"RWS_Change_PickSecond".Translate(Dialog_RimworldStatistics.Days(points[first].Tick),
                    MetricText.Format(definition, picked));
            if (measured!.Length < 2)
                return changeText = (string)"RWS_Change_OnlyOne".Translate();
            if ((visiblePrimaryMeasured ?? measured).Length < 2)
                return changeText = (string)"RWS_Chart_ZoomCompareTooFew".Translate();
            return changeText = (string)"RWS_Change_Hint".Translate();
        }

        return changeText = FormatHistoryChange(definition, c);
    }

    internal static string FormatHistoryChange(MetricDefinition definition, HistoryChange c)
    {
        string Shown(MetricHistoryPoint point) => point.Value is { } value
            ? MetricText.Format(definition, value) : Dialog_RimworldStatistics.Dash;
        var text = (string)"RWS_Change_Interval".Translate(Dialog_RimworldStatistics.Days(c.From.Tick), Shown(c.From),
            Dialog_RimworldStatistics.Days(c.To.Tick), Shown(c.To), Dialog_RimworldStatistics.Days(c.Ticks));
        text += "\n" + c.Issue switch
        {
            HistoryChangeIssue.None => (string)(c.Kind == HistoryChangeKind.Increase ? "RWS_Change_Increase" : "RWS_Change_Difference")
                .Translate(Delta(definition, c.Delta!.Value), Ratio(c)),
            HistoryChangeIssue.SamePoint => (string)"RWS_Change_SamePoint".Translate(),
            HistoryChangeIssue.NoValue => (string)"RWS_Change_NoValue".Translate(),
            HistoryChangeIssue.NotLinked => (string)"RWS_Change_NotLinked".Translate(),
            HistoryChangeIssue.CounterGap => (string)"RWS_Change_CounterGap".Translate(),
            HistoryChangeIssue.CounterDecrease => (string)"RWS_Change_CounterDecrease".Translate(),
            _ => (string)"RWS_Change_TooLarge".Translate(),
        };
        if (c.Issue == HistoryChangeIssue.None && c.Partial)
            text += "\n" + (string)"RWS_Change_Partial".Translate();
        if (c.Issue == HistoryChangeIssue.None && c.RecordedSince is { } since)
            text += "\n" + (string)"RWS_Change_RecordedSince".Translate(Dialog_RimworldStatistics.Days(since));
        // A checkpoint later saves carried unchanged is one reading, at its own tick: the interval ends there.
        if (c.Issue == HistoryChangeIssue.None && c.To.SavedTick is { } saved)
            text += "\n" + (string)"RWS_Change_Carried".Translate(Dialog_RimworldStatistics.Days(saved));
        return text;
    }

    /// <summary>The change with its sign; percentages and mood change by percentage points.</summary>
    private static string Delta(MetricDefinition definition, double delta)
    {
        var magnitude = Math.Abs(delta);
        var shown = definition.Unit switch
        {
            MetricUnit.Percent => (string)"RWS_Change_PercentPoints".Translate((magnitude * 100).ToString("N1", Inv)),
            MetricUnit.Mood => (string)"RWS_Change_PercentPoints".Translate(magnitude.ToString("N1", Inv)),
            _ => MetricText.Format(definition with { Signed = false },
                new NumericValue { Number = magnitude, Coverage = MetricCoverage.SnapshotOnly }),
        };
        return (delta > 0 ? "+" : delta < 0 ? "-" : "") + shown;
    }

    private static string Ratio(HistoryChange change) => change.RatioIssue switch
    {
        HistoryRatioIssue.None => (string)"RWS_Change_Ratio".Translate(
            (change.Ratio > 0 ? "+" : "") + (change.Ratio!.Value * 100).ToString("N1", Inv) + "%"),
        HistoryRatioIssue.ZeroBase => (string)"RWS_Change_RatioZero".Translate(),
        HistoryRatioIssue.Unit => (string)"RWS_Change_RatioUnit".Translate(),
        _ => (string)"RWS_Change_RatioTooLarge".Translate(),
    };

    private void PollOverlayTasks(MetricHistoryKind primaryKind)
    {
        var changed = false;
        foreach (var entry in overlayTracker.Entries)
        {
            if (entry.Status != HistoryOverlayStatus.Loading)
                continue;
            if (!overlayTasks.TryGetValue(entry.RequestId, out var task) || !task.IsCompleted)
                continue;

            overlayTasks.Remove(entry.RequestId);
            if (task.IsCompletedSuccessfully)
                changed |= overlayTracker.TryComplete(entry.RequestId, task.Result, primaryKind);
            else
            {
                _ = task.Exception;
                changed |= overlayTracker.TryFail(entry.RequestId);
            }
        }
        if (changed)
            InvalidateOverlays();
    }

    private void InvalidateOverlays()
    {
        boundsDirty = true;
        legend = null;
        legendPlan = null;
        legendWidth = -1f;
    }

    private const float LegendRowHeight = 22f;
    private const float LegendItemGap = 16f;
    private const float LegendRowGap = 2f;

    private static string Describe(MetricHistory history, IReadOnlyList<MetricHistoryPoint> measured)
    {
        var note = history.Kind switch
        {
            MetricHistoryKind.GameRecorder => (string)"RWS_Chart_GameRecorder".Translate(),
            MetricHistoryKind.WealthRecorder => (string)"RWS_Chart_HistoryWealth".Translate(),
            MetricHistoryKind.CheckpointLineage => (string)"RWS_Chart_HistoryLineage".Translate(),
            _ => (string)"RWS_Chart_HistorySaves".Translate(),
        };
        if (measured.Count == 1)
            note += "\n" + (string)"RWS_Chart_HistorySingle".Translate();
        var gaps = history.Points.Count(p => p.Value is null);
        var partial = measured.Count(p => p.Value!.Coverage == MetricCoverage.Partial);
        if (gaps > 0 || partial > 0)
            note += "\n" + (string)"RWS_Chart_PointCounts".Translate(measured.Count, partial, gaps);
        // Saves whose snapshot reading was thinned out of the bounded series, or removed before it was kept (#318).
        if (history.OmittedSaves > 0)
            note += "\n" + (string)"RWS_Chart_HistoryOmitted".Translate(history.OmittedSaves);
        // The statistics stop at the last checkpoint; a later save carried no newer ones (recording off, or the
        // checkpoint written unchanged). That save may be on another branch, so it is named, not joined.
        if (history.Kind == MetricHistoryKind.CheckpointLineage && history.LatestSaveTick is { } latest
            && history.Points.Count > 0 && latest > history.Points[^1].Tick)
            note += "\n" + (string)"RWS_Chart_LastCapture".Translate(
                Dialog_RimworldStatistics.Days(history.Points[^1].Tick), Dialog_RimworldStatistics.Days(latest));
        return note;
    }

    private List<(string Label, Action<Rect> Swatch, string? Tip)>? legend;
    private HistoryLegendPlan? legendPlan;
    private float legendWidth = -1f;

    private void EnsureBoundsAndLegend(MetricHistory history, float availableWidth)
    {
        if (boundsDirty)
        {
            fullBounds = overlayTracker.ComputeBounds(history, measured!, visibleRange: null);
            bounds = zoomRange is null ? fullBounds : overlayTracker.ComputeBounds(history, measured!, zoomRange);
            visiblePrimaryMeasured = HistoryPlotRange.FilterMeasured(measured!, zoomRange);
            slicedPrimaryPoints = zoomRange is { } r ? MetricHistoryPlot.Slice(history.Points, r) : history.Points;
            visibleTotalMeasuredCount = overlayTracker.CountVisibleMeasured(measured!, zoomRange);
            boundsDirty = false;
            plotWidth = 0;
            gapTicks = null;
            legend = null;
            legendPlan = null;
        }

        if (legend is null)
        {
            legend = [("RWS_Chart_LegendMeasured".Translate(), r => Widgets.DrawBoxSolid(r, SeriesColor), null)];
            if (bounds.HasPartial)
                legend.Add(("RWS_Chart_LegendPartial".Translate(), r => Widgets.DrawBoxSolid(r, PartialColor), null));
            if (bounds.HasUnlinked)
                legend.Add(("RWS_Chart_LegendUnlinkedShort".Translate(),
                    r => Widgets.DrawBoxSolidWithOutline(r, HollowFill, SeriesColor, 1),
                    "RWS_Chart_LegendUnlinked".Translate()));
            if (slicedPrimaryPoints.Any(p => p.Value is null))
                legend.Add(("RWS_Chart_LegendGap".Translate(),
                    r => Widgets.DrawBoxSolid(new Rect(r.center.x - 1f, r.y, 2f, r.height), GapColor), null));
            legendPlan = null;
        }

        if (legendPlan is null || Math.Abs(legendWidth - availableWidth) > 0.5f)
        {
            var widths = new float[legend.Count];
            for (var i = 0; i < legend.Count; i++)
                widths[i] = 16f + Text.CalcSize(legend[i].Label).x;
            legendPlan = HistoryLegendLayout.Resolve(widths, availableWidth, LegendRowHeight, LegendItemGap, LegendRowGap);
            legendWidth = availableWidth;
        }
    }

    private void DrawLegend(Rect rect)
    {
        if (legend is null || legendPlan is null)
            return;

        foreach (var placement in legendPlan.Items)
        {
            var (label, swatch, tip) = legend[placement.Index];
            var x = rect.x + placement.X;
            var y = rect.y + placement.Y;
            var labelWidth = Math.Max(0f, placement.Width - 14f);
            swatch(new Rect(x, y + 6f, 9f, 9f));
            var itemRect = new Rect(x, y, placement.Width, LegendRowHeight);
            Widgets.Label(new Rect(x + 14f, y, labelWidth, LegendRowHeight), label.Truncate(labelWidth));
            if (tip is not null)
                TooltipHandler.TipRegion(itemRect, tip);
            else if (Text.CalcSize(label).x > labelWidth)
                TooltipHandler.TipRegion(itemRect, label);
        }
    }

    private void DrawPlot(Rect plot, float axisWidth, MetricHistory history, string primaryColonyName)
    {
        var pixelWidth = Math.Max(1, (int)plot.width);
        var ticks = (First: bounds.FirstTick, Last: bounds.LastTick);
        if (plotWidth != pixelWidth)
        {
            plotPoints = MetricHistoryPlot.Thin(
                slicedPrimaryPoints,
                pixelWidth,
                ticks.First,
                ticks.Last,
                isZoomed: zoomRange is not null);
            gapTicks = null;
            plotWidth = pixelWidth;
        }

        var range = (Min: bounds.MinValue, Max: bounds.MaxValue);
        float X(int tick) => plot.x + plot.width * (ticks.First == ticks.Last ? 0.5f
            : (float)((tick - ticks.First) / (double)(ticks.Last - ticks.First)));
        // Scale before subtracting to keep even opposite-sign finite extremes representable.
        var scale = Math.Max(1d, Math.Max(Math.Abs(range.Min), Math.Abs(range.Max)));
        var min = range.Min / scale;
        var span = range.Max / scale - min;
        float Y(double value) => plot.yMax - plot.height * (span == 0 ? 0.5f : (float)((value / scale - min) / span));

        using (UiScopes.GuiState())
        {
            Text.Anchor = TextAnchor.MiddleRight;
            var gridSteps = range.Min == range.Max ? 0 : 4;
            for (var i = 0; i <= gridSteps; i++)
            {
                var fraction = gridSteps == 0 ? 0.5d : i / (double)gridSteps;
                var value = range.Min * (1d - fraction) + range.Max * fraction;
                var y = plot.yMax - plot.height * (float)fraction;
                Widgets.DrawBoxSolid(new Rect(plot.x, y, plot.width, 1f), GridColor);
                var label = MetricText.Format(definition, new NumericValue { Number = value, Coverage = MetricCoverage.SnapshotOnly });
                Widgets.Label(new Rect(plot.x - axisWidth, y - 12f, axisWidth - 6f, 24f), label.Truncate(axisWidth - 6f));
            }
            Text.Anchor = TextAnchor.MiddleCenter;
            var tickSteps = ticks.First == ticks.Last ? 0 : 4;
            for (var i = 0; i <= tickSteps; i++)
            {
                var tick = tickSteps == 0 ? ticks.First : ticks.First + (int)((ticks.Last - ticks.First) * (i / (double)tickSteps));
                Widgets.Label(new Rect(X(tick) - 36f, plot.yMax + 3f, 72f, 22f), Dialog_RimworldStatistics.Days(tick));
            }
            Widgets.Label(new Rect(plot.x, plot.yMax + 23f, plot.width, 21f), "RWS_Col_Days".Translate());
        }

        DrawSeries(plotPoints, SeriesColor, PartialColor, isRawSeries: ReferenceEquals(plotPoints, slicedPrimaryPoints) && zoomRange is not null);
        foreach (var overlay in overlayTracker.Entries)
        {
            var overlayPoints = overlay.GetOrThinPoints(pixelWidth, zoomRange, ticks);
            if (overlayPoints.Count == 0)
                continue;
            var color = OverlayColors[overlay.ColorIndex];
            DrawSeries(overlayPoints, color, Color.Lerp(color, PartialColor, 0.5f), isRawSeries: zoomRange is not null && overlayPoints.Count <= pixelWidth * 2);
        }
        // A save or checkpoint exists here, but it holds no value for this statistic: marked on the axis, never as zero.
        gapTicks ??= plotPoints.Where(p => p.Value is null).Select(p => p.Tick).ToArray();
        foreach (var tick in gapTicks)
            Widgets.DrawBoxSolid(new Rect(X(tick) - 1f, plot.yMax - 7f, 2f, 7f), GapColor);

        if (zoomRange is not null && visibleTotalMeasuredCount == 0)
        {
            using (UiScopes.GuiState())
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = PartialColor;
                Widgets.Label(plot, "RWS_Chart_ZoomEmptyPlot".Translate());
            }
        }

        // The readings picked for a comparison (#307), from the full series: thinning may not have kept them.
        // A local function, not an array of the indices: this runs every frame.
        void DrawPicked(int? picked)
        {
            if (picked is not { } index || !HistoryChanges.CanPick(history, index, zoomRange)
                || history.Points[index].Value is not { Number: { } pickedNumber })
                return;
            var at = new Vector2(X(history.Points[index].Tick), Y(pickedNumber));
            Widgets.DrawBoxSolid(new Rect(at.x, plot.y, 1f, plot.height), PickColor);
            Widgets.DrawBox(new Rect(at.x - 5f, at.y - 5f, 10f, 10f), 1);
        }
        DrawPicked(pickFrom);
        if (change is not null)
        {
            DrawPicked(change.FromIndex);
            DrawPicked(change.ToIndex);
        }

        void DrawSeries(IReadOnlyList<MetricHistoryPoint> points, Color color, Color partialColor, bool isRawSeries)
        {
            // A line joins two readings only where the later one continues the earlier (MetricHistoryPoint);
            // a gap (no value) joins nothing, and a reading of unproven ancestry is drawn hollow.
            var positions = new Vector2?[points.Count];
            var partials = new bool[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                var point = points[i];
                if (point.Value is not { Number: { } number } value)
                    continue;
                var position = new Vector2(X(point.Tick), Y(number));
                var partial = value.Coverage == MetricCoverage.Partial;
                positions[i] = position;
                partials[i] = partial;
                var from = point.Predecessor(i);
                var joined = from >= 0 && from < i && positions[from] is not null;
                if (joined)
                    Widgets.DrawLine(positions[from]!.Value, position, partial || partials[from] ? partialColor : color, 2f);
                var marker = partial ? partialColor : color;
                if (point.Unlinked)
                {
                    Widgets.DrawBoxSolidWithOutline(new Rect(position.x - 3.5f, position.y - 3.5f, 7f, 7f), HollowFill, marker, 1);
                    continue;
                }
                var size = points.Count <= 90 || (isRawSeries && points.Count <= 220) || !joined ? 5f : 2f;
                Widgets.DrawBoxSolid(new Rect(position.x - size / 2f, position.y - size / 2f, size, size), marker);
            }
        }

        if (!Mouse.IsOver(plot))
            return;
        var mouse = Event.current.mousePosition;
        var hoverTick = ticks.First + (ticks.Last - ticks.First) * (double)Math.Clamp((mouse.x - plot.x) / plot.width, 0f, 1f);
        if (mouse.y >= plot.yMax - 12f && NearestGap(hoverTick) is { } gap && Math.Abs(X(gap) - mouse.x) <= 5f)
        {
            // One tooltip region at a time: RimWorld stacks overlapping ones.
            Widgets.DrawBoxSolid(new Rect(X(gap) - 1.5f, plot.yMax - 9f, 3f, 9f), Color.white);
            TooltipHandler.TipRegion(plot, (string)"RWS_Chart_GapTip".Translate(Dialog_RimworldStatistics.Days(gap)));
            return;
        }
        MetricHistoryPoint? nearest = null;
        var hoverName = primaryColonyName;
        IReadOnlyList<MetricHistoryPoint> hoverSeries = history.Points;
        var bestDistance = float.PositiveInfinity;
        var primaryCandidates = visiblePrimaryMeasured ?? measured!;
        var totalCandidateCount = primaryCandidates.Length;
        if (primaryCandidates.Length > 0)
        {
            var candidate = Nearest(primaryCandidates, hoverTick);
            if (candidate.Value?.Number is { } n)
            {
                nearest = candidate;
                bestDistance = (new Vector2(X(candidate.Tick), Y(n)) - mouse).sqrMagnitude;
            }
        }
        foreach (var overlay in overlayTracker.Entries)
        {
            var overlayCandidates = overlay.GetVisibleMeasured(zoomRange);
            if (overlayCandidates.Length == 0)
                continue;
            totalCandidateCount += overlayCandidates.Length;
            var candidate = Nearest(overlayCandidates, hoverTick);
            if (candidate.Value?.Number is not { } n)
                continue;
            var distance = (new Vector2(X(candidate.Tick), Y(n)) - mouse).sqrMagnitude;
            if (nearest is null || distance < bestDistance)
            {
                nearest = candidate;
                bestDistance = distance;
                hoverName = OverlayColonyName(overlay);
                hoverSeries = overlay.RawPoints;
            }
        }
        if (nearest is null)
            return;
        var x = X(nearest.Tick);
        if ((totalCandidateCount > 1 || ticks.First != ticks.Last) && Math.Abs(x - mouse.x) > 18f)
            return;
        var ownReading = ReferenceEquals(hoverSeries, history.Points);
        // A click picks the reading the tooltip names, and only this colony's own: other colonies are not its timeline.
        if (ownReading && Event.current.type == EventType.MouseDown && Event.current.button == 0)
        {
            if (FindIndex(history.Points, nearest) is var index && index >= 0 && HistoryChanges.CanPick(history, index, zoomRange))
                Pick(history, index);
            Event.current.Use();
        }
        var reading = nearest.Value!;
        var yPoint = Y(reading.Number!.Value);
        Widgets.DrawBoxSolid(new Rect(x, plot.y, 1f, plot.height), GridColor);
        Widgets.DrawBoxSolid(new Rect(x - 3f, yPoint - 3f, 6f, 6f), Color.white);
        TooltipHandler.TipRegion(plot, hoverName + "\n" + (string)"RWS_Unit_Days".Translate(Dialog_RimworldStatistics.Days(nearest.Tick))
            + "\n" + MetricText.Format(definition, reading) + "\n" + MetricText.State(definition, reading.Coverage)
            + MetricText.PerDay(definition, reading) + Lineage(hoverSeries, nearest)
            + (ownReading ? "\n" + (string)"RWS_Change_ClickTip".Translate() : ""));
    }

    private int? NearestGap(double tick)
    {
        if (gapTicks is not { Length: > 0 } gaps)
            return null;
        var index = Array.BinarySearch(gaps, (int)Math.Round(tick));
        if (index >= 0)
            return gaps[index];
        index = ~index;
        if (index == 0)
            return gaps[0];
        if (index == gaps.Length)
            return gaps[^1];
        return tick - gaps[index - 1] <= gaps[index] - tick ? gaps[index - 1] : gaps[index];
    }

    /// <summary>What the chart knows about where a reading comes from, beyond its value.</summary>
    private static string Lineage(IReadOnlyList<MetricHistoryPoint> series, MetricHistoryPoint point)
    {
        var text = "";
        if (point.Unlinked)
            text += "\n" + (string)"RWS_Chart_PointUnlinked".Translate();
        // "Another branch" only when every skipped reading is proven not to be this save's ancestor; a skipped save that
        // carried an older checkpoint, or has none, may be one, so its relation is called unproven instead.
        if (point.ContinuesFrom >= 0 && FindIndex(series, point) is var index && index >= 0
            && point.Predecessor(index) is var from && from >= 0 && from < series.Count)
            text += "\n" + (string)(point.SkipsOnlyOtherBranches ? "RWS_Chart_PointBranch" : "RWS_Chart_PointContinues")
                .Translate(Dialog_RimworldStatistics.Days(series[from].Tick));
        if (point.SavedTick is { } saved)
            text += "\n" + (string)"RWS_Chart_PointSavedLater".Translate(Dialog_RimworldStatistics.Days(saved));
        return text;
    }

    private static int FindIndex(IReadOnlyList<MetricHistoryPoint> series, MetricHistoryPoint point)
    {
        for (var i = 0; i < series.Count; i++)
            if (ReferenceEquals(series[i], point))
                return i;
        return -1;
    }

    private static MetricHistoryPoint Nearest(IReadOnlyList<MetricHistoryPoint> points, double tick)
    {
        var left = 0;
        var right = points.Count - 1;
        while (left < right)
        {
            var middle = (left + right) / 2;
            if (points[middle].Tick < tick)
                left = middle + 1;
            else
                right = middle;
        }
        return left > 0 && Math.Abs(points[left - 1].Tick - tick) < Math.Abs(points[left].Tick - tick)
            ? points[left - 1] : points[left];
    }
}
