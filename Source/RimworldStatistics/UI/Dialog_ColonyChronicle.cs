using System.Globalization;
using System.Text;
using RimWorld;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Read-only evidence-based colony chronicle viewer (V2-A).
/// Shows confirmed endings, milestones, completed research, verified checkpoint-to-checkpoint intervals, and explicit
/// recording gaps strictly on one selected save branch; undated traces remain in a separate section.
/// </summary>
internal sealed class Dialog_ColonyChronicle : Window
{
    private const float HeaderRowHeight = 25f;
    private const float RowHeight = 30f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly Color EndingColor = new(0.98f, 0.84f, 0.38f);
    private static readonly Color MilestoneColor = new(0.46f, 0.82f, 0.62f);
    private static readonly Color ResearchColor = new(0.48f, 0.74f, 0.94f);
    private static readonly Color IntervalColor = new(0.78f, 0.82f, 0.88f);
    private static readonly Color GapColor = new(0.94f, 0.64f, 0.38f);

    private enum FilterMode
    {
        All,
        Highlights,
        IntervalsAndGaps,
        Research,
    }

    private sealed record ChartTarget(string MetricKey, string Label);

    private sealed record DisplayEntryRow(
        ChronicleEntry Entry,
        string TimeText,
        string KindText,
        Color KindColor,
        string EventText,
        string EvidenceText,
        string Tooltip,
        ChartTarget[] ChartTargets);

    private sealed record ChronicleDisplayCache(
        ColonyChronicle Chronicle,
        object? Language,
        FilterMode Filter,
        bool NewestFirst,
        string ActiveBranchLabel,
        string SummaryText,
        string[] NoticeLines,
        DisplayEntryRow[] Rows,
        string[] UndatedLines,
        int TotalCount,
        int HighlightsCount,
        int IntervalsAndGapsCount,
        int ResearchCount);

    private readonly StatsView view;
    private string colonyKey;
    private string colonyName = "";
    private int? requestedBranchLeafIndex;
    private FilterMode filter = FilterMode.All;
    private bool newestFirst;

    private Task<ColonyChronicle?>? chronicleTask;
    private object? cachedLanguage;
    private ChronicleDisplayCache? displayCache;
    private ColumnPlan? tablePlan;
    private float cachedTableWidth = -1f;
    private Vector2 scroll;

    internal Dialog_ColonyChronicle(StatsView view, string colonyKey, int? initialBranchLeafIndex = null)
    {
        this.view = view;
        this.colonyKey = colonyKey;
        requestedBranchLeafIndex = initialBranchLeafIndex;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;

        StartLoad(colonyKey, initialBranchLeafIndex);
    }

    public override Vector2 InitialSize => new(
        Math.Min(1160f, Verse.UI.screenWidth * 0.92f),
        Math.Min(780f, Verse.UI.screenHeight * 0.90f));

    public override void DoWindowContents(Rect rect)
    {
        var lang = LanguageDatabase.activeLanguage;
        if (!ReferenceEquals(cachedLanguage, lang))
        {
            cachedLanguage = lang;
            RefreshColonyName();
            displayCache = null;
            tablePlan = null;
        }

        var titleText = (string)"RWS_Chronicle_Title".Translate(colonyName);
        Text.Font = GameFont.Medium;
        var titleRect = new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 36f), 32f);
        var shownTitle = titleText.Truncate(titleRect.width);
        Widgets.Label(titleRect, shownTitle);
        if (shownTitle != titleText)
            TooltipHandler.TipRegion(titleRect, titleText);
        Text.Font = GameFont.Small;

        var currentChronicle = chronicleTask is { IsCompletedSuccessfully: true } done ? done.Result : null;
        var display = currentChronicle is not null ? EnsureDisplayCache(currentChronicle, lang) : null;

        var y = rect.y + 36f;
        var controls = new ControlRow(new Rect(rect.x, y, rect.width, 28f));

        if (view.Colonies.Count > 1)
        {
            var switchLabel = (string)"RWS_Chronicle_SwitchColony".Translate();
            var switchRect = controls.Next(switchLabel, 125f, 210f);
            if (Widgets.ButtonText(switchRect, switchLabel.Truncate(Math.Max(0f, switchRect.width - 12f))))
                ShowColonyMenu();
        }

        if (currentChronicle is { Branches.Count: > 1 } && display is not null)
        {
            var branchBtn = (string)"RWS_Chronicle_BranchButton".Translate(display.ActiveBranchLabel);
            var branchRect = controls.Next(branchBtn, 165f, 320f);
            var shownBranch = branchBtn.Truncate(Math.Max(0f, branchRect.width - 12f));
            if (Widgets.ButtonText(branchRect, shownBranch))
                ShowBranchMenu(currentChronicle);
            if (shownBranch != branchBtn)
                TooltipHandler.TipRegion(branchRect, branchBtn);
        }

        if (display is not null)
        {
            var filterLabel = (string)"RWS_Chronicle_FilterButton".Translate(
                FilterName(filter), FilterCount(display, filter).ToString(Inv));
            var filterRect = controls.Next(filterLabel, 145f, 260f);
            if (Widgets.ButtonText(filterRect, filterLabel.Truncate(Math.Max(0f, filterRect.width - 12f))))
                ShowFilterMenu(display);

            var orderLabel = (string)(newestFirst ? "RWS_Chronicle_OrderNewest" : "RWS_Chronicle_OrderOldest").Translate();
            var orderRect = controls.Next(orderLabel, 130f, 220f);
            if (Widgets.ButtonText(orderRect, orderLabel.Truncate(Math.Max(0f, orderRect.width - 12f))))
            {
                newestFirst = !newestFirst;
                displayCache = null;
            }
        }

        var reportLabel = (string)"RWS_Report_Open".Translate();
        var reportRect = controls.Next(reportLabel, 120f, 215f);
        if (Widgets.ButtonText(reportRect, reportLabel.Truncate(Math.Max(0f, reportRect.width - 12f))))
            Find.WindowStack.Add(new Dialog_ColonySummaryReport(view, colonyKey));
        TooltipHandler.TipRegion(reportRect, "RWS_Report_OpenTip".Translate());

        var lineageLabel = (string)"RWS_Lineage_Open".Translate();
        var lineageRect = controls.Next(lineageLabel, 120f, 215f);
        if (Widgets.ButtonText(lineageRect, lineageLabel.Truncate(Math.Max(0f, lineageRect.width - 12f))))
            Find.WindowStack.Add(new Dialog_ColonySaveLineage(view, colonyKey));
        TooltipHandler.TipRegion(lineageRect, "RWS_Lineage_OpenTip".Translate());

        y += controls.Height + 6f;
        var body = new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y));

        if (chronicleTask is null || !chronicleTask.IsCompleted)
        {
            Widgets.Label(body, "RWS_Loading".Translate());
            return;
        }
        if (!chronicleTask.IsCompletedSuccessfully || display is null)
        {
            Widgets.Label(body, "RWS_Chronicle_LoadFailed".Translate());
            return;
        }

        DrawBody(body, display);
    }

    private void StartLoad(string targetColonyKey, int? branchLeafIndex)
    {
        colonyKey = targetColonyKey;
        requestedBranchLeafIndex = branchLeafIndex;
        RefreshColonyName();
        displayCache = null;
        tablePlan = null;
        scroll = Vector2.zero;

        var v = view;
        var includeDev = ProfileService.Options.IncludeDevMode;
        chronicleTask = Task.Run(() => ProfileService.Session.ReadColonyChronicle(v, targetColonyKey, branchLeafIndex, includeDev));
    }

    private void RefreshColonyName()
    {
        var stats = view.Stats.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        colonyName = stats is not null ? Dialog_RimworldStatistics.ColonyName(stats) : colonyKey;
    }

    private void ShowColonyMenu()
    {
        var options = view.Stats.Colonies
            .OrderBy(Dialog_RimworldStatistics.ColonyName, StringComparer.CurrentCulture)
            .Select(c =>
            {
                var key = c.Key;
                var label = Dialog_RimworldStatistics.ColonyName(c) + (c.Excluded ? (string)"RWS_ExcludedMark".Translate() : "");
                return new FloatMenuOption(label, () => StartLoad(key, null));
            })
            .ToList();
        if (options.Count > 0)
            Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowBranchMenu(ColonyChronicle chronicle)
    {
        var options = new List<FloatMenuOption>(chronicle.Branches.Count);
        foreach (var b in chronicle.Branches)
        {
            var idx = b.LeafNodeIndex;
            var label = FormatBranchOptionLabel(b);
            options.Add(new FloatMenuOption(label, () => StartLoad(colonyKey, idx)));
        }
        if (options.Count > 0)
            Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowFilterMenu(ChronicleDisplayCache display)
    {
        FloatMenuOption MakeOption(FilterMode mode)
        {
            var text = $"{FilterName(mode)} ({FilterCount(display, mode).ToString(Inv)})";
            return new FloatMenuOption(text, () =>
            {
                if (filter != mode)
                {
                    filter = mode;
                    displayCache = null;
                    scroll = Vector2.zero;
                }
            });
        }

        Find.WindowStack.Add(new FloatMenu(
        [
            MakeOption(FilterMode.All),
            MakeOption(FilterMode.Highlights),
            MakeOption(FilterMode.IntervalsAndGaps),
            MakeOption(FilterMode.Research),
        ]));
    }

    private static string FilterName(FilterMode mode) => mode switch
    {
        FilterMode.Highlights => "RWS_Chronicle_FilterHighlights".Translate(),
        FilterMode.IntervalsAndGaps => "RWS_Chronicle_FilterIntervalsAndGaps".Translate(),
        FilterMode.Research => "RWS_Chronicle_FilterResearch".Translate(),
        _ => "RWS_Chronicle_FilterAll".Translate(),
    };

    private static int FilterCount(ChronicleDisplayCache d, FilterMode mode) => mode switch
    {
        FilterMode.Highlights => d.HighlightsCount,
        FilterMode.IntervalsAndGaps => d.IntervalsAndGapsCount,
        FilterMode.Research => d.ResearchCount,
        _ => d.TotalCount,
    };

    private static bool MatchesFilter(ChronicleEntry entry, FilterMode mode) => mode switch
    {
        FilterMode.Highlights => entry.Kind is ChronicleEntryKind.Ending or ChronicleEntryKind.Milestone,
        FilterMode.IntervalsAndGaps => entry.Kind is ChronicleEntryKind.VerifiedInterval or ChronicleEntryKind.RecordingGap,
        FilterMode.Research => entry.Kind == ChronicleEntryKind.ResearchCompleted,
        _ => true,
    };

    private void DrawBody(Rect outRect, ChronicleDisplayCache display)
    {
        var innerWidth = Math.Max(160f, outRect.width - 16f);
        if (tablePlan is null || Math.Abs(cachedTableWidth - innerWidth) > 0.5f)
        {
            cachedTableWidth = innerWidth;
            tablePlan = ColumnLayout.Resolve(
                [new(135, 2), new(120, 2), new(320, 6), new(155, 3)],
                innerWidth);
        }

        var summaryBlock = display.NoticeLines.Length > 0
            ? display.SummaryText + "\n" + string.Join("\n", display.NoticeLines)
            : display.SummaryText;
        var summaryHeight = Math.Max(26f, Text.CalcHeight(summaryBlock, innerWidth - 16f) + 12f);

        var tableBodyHeight = display.Rows.Length == 0
            ? 32f
            : display.Rows.Length * RowHeight;
        var tableTotalHeight = HeaderRowHeight + tableBodyHeight + 8f;

        var undatedHeight = 0f;
        if (display.UndatedLines.Length > 0)
        {
            var subtitle = (string)"RWS_Chronicle_UndatedSubtitle".Translate();
            var subtitleH = Math.Max(20f, Text.CalcHeight(subtitle, innerWidth - 20f) + 4f);
            var linesH = display.UndatedLines.Sum(l => Math.Max(20f, Text.CalcHeight("• " + l, innerWidth - 20f) + 2f));
            undatedHeight = 28f + 14f + subtitleH + linesH + 8f;
        }

        var viewHeight = Math.Max(outRect.height, summaryHeight + 8f + tableTotalHeight + undatedHeight);
        var viewWidth = Math.Max(innerWidth, tablePlan.TotalWidth);
        var viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        var cy = 0f;

        // Summary & notice card.
        var summaryRect = new Rect(0f, cy, innerWidth, summaryHeight);
        Widgets.DrawBoxSolidWithOutline(summaryRect,
            new Color(0.14f, 0.16f, 0.19f, 0.85f),
            new Color(0.38f, 0.52f, 0.66f, 0.55f));
        Widgets.Label(new Rect(8f, cy + 6f, innerWidth - 16f, summaryHeight - 12f), summaryBlock);
        cy += summaryHeight + 8f;

        // Table header.
        var widths = tablePlan.Widths;
        string[] headers =
        [
            "RWS_Chronicle_ColTime".Translate(),
            "RWS_Chronicle_ColKind".Translate(),
            "RWS_Chronicle_ColEvent".Translate(),
            "RWS_Chronicle_ColEvidence".Translate(),
        ];

        var prevAnchor = Text.Anchor;
        var prevColor = GUI.color;
        GUI.color = new Color(0.78f, 0.80f, 0.84f);
        Text.Anchor = TextAnchor.MiddleLeft;
        var hx = 0f;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = new Rect(hx + 4f, cy, Math.Max(0f, widths[c] - 8f), HeaderRowHeight);
            var shown = headers[c].Truncate(cell.width);
            Widgets.Label(cell, shown);
            if (shown != headers[c])
                TooltipHandler.TipRegion(cell, headers[c]);
            hx += widths[c];
        }
        Widgets.DrawBoxSolid(new Rect(0f, cy + HeaderRowHeight - 1f, tablePlan.TotalWidth, 1f), new Color(1f, 1f, 1f, 0.15f));
        GUI.color = prevColor;
        Text.Anchor = prevAnchor;
        cy += HeaderRowHeight;

        if (display.Rows.Length == 0)
        {
            GUI.color = new Color(0.72f, 0.75f, 0.80f);
            Widgets.Label(new Rect(4f, cy + 6f, innerWidth - 8f, 24f), "RWS_Chronicle_Empty".Translate());
            GUI.color = Color.white;
            cy += 32f;
        }
        else
        {
            var viewportTop = scroll.y - cy;
            var viewportBottom = scroll.y + outRect.height - cy;
            var firstRow = Math.Max(0, (int)Math.Floor(viewportTop / RowHeight) - 1);
            var lastRow = Math.Min(display.Rows.Length - 1, (int)Math.Ceiling(viewportBottom / RowHeight) + 1);

            for (var i = firstRow; i <= lastRow; i++)
            {
                var r = display.Rows[i];
                var ry = cy + i * RowHeight;
                var rowRect = new Rect(0f, ry, tablePlan.TotalWidth, RowHeight);

                if (i % 2 == 1)
                    Widgets.DrawHighlight(rowRect, 0.22f);
                var chartable = r.ChartTargets.Length > 0;
                if (chartable)
                    Widgets.DrawHighlightIfMouseover(rowRect);
                var hovered = chartable && Mouse.IsOver(rowRect);

                Text.Anchor = TextAnchor.MiddleLeft;
                var rx = 0f;

                // Time column.
                var timeCell = new Rect(rx + 4f, ry, Math.Max(0f, widths[0] - 8f), RowHeight);
                Widgets.Label(timeCell, r.TimeText.Truncate(timeCell.width));
                rx += widths[0];

                // Kind column.
                var kindCell = new Rect(rx + 4f, ry, Math.Max(0f, widths[1] - 8f), RowHeight);
                GUI.color = r.KindColor;
                Widgets.Label(kindCell, r.KindText.Truncate(kindCell.width));
                GUI.color = Color.white;
                rx += widths[1];

                // Event column (+ mini chart icon when chartable).
                var iconReserve = chartable ? 18f : 0f;
                var eventWidth = Math.Max(0f, widths[2] - 8f - iconReserve);
                var eventCell = new Rect(rx + 4f, ry, eventWidth, RowHeight);
                Widgets.Label(eventCell, r.EventText.Truncate(eventCell.width));
                if (chartable)
                {
                    var iconRect = new Rect(rx + widths[2] - 18f, ry + (RowHeight - 12f) * 0.5f, 12f, 12f);
                    MetricVisualStyle.DrawMiniChartIcon(iconRect, hovered);
                }
                rx += widths[2];

                // Evidence column.
                var evCell = new Rect(rx + 4f, ry, Math.Max(0f, widths[3] - 8f), RowHeight);
                GUI.color = r.Entry.Coverage == MetricCoverage.Partial
                    ? new Color(0.92f, 0.79f, 0.52f)
                    : new Color(0.82f, 0.85f, 0.90f);
                Widgets.Label(evCell, r.EvidenceText.Truncate(evCell.width));
                GUI.color = Color.white;

                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(rowRect, r.Tooltip);
                if (chartable && Widgets.ButtonInvisible(rowRect))
                    OpenEntryChart(r.ChartTargets);
            }

            cy += display.Rows.Length * RowHeight + 8f;
        }

        // Separate undated traces section so undated evidence never receives a fabricated day.
        if (display.UndatedLines.Length > 0)
        {
            var prevHeadingColor = GUI.color;
            GUI.color = new Color(0.58f, 0.82f, 0.94f);
            Widgets.Label(new Rect(0f, cy, innerWidth, 23f),
                "RWS_Chronicle_UndatedTitle".Translate(display.UndatedLines.Length.ToString(Inv)));
            Widgets.DrawBoxSolid(new Rect(0f, cy + 23f, innerWidth, 1f), new Color(1f, 1f, 1f, 0.14f));
            GUI.color = prevHeadingColor;
            cy += 28f;

            var subtitle = (string)"RWS_Chronicle_UndatedSubtitle".Translate();
            var subtitleH = Math.Max(20f, Text.CalcHeight(subtitle, innerWidth - 20f) + 4f);
            var linesH = display.UndatedLines.Sum(l => Math.Max(20f, Text.CalcHeight("• " + l, innerWidth - 20f) + 2f));
            var cardH = 14f + subtitleH + linesH;
            Widgets.DrawMenuSection(new Rect(0f, cy, innerWidth, cardH));

            var uy = cy + 7f;
            GUI.color = new Color(0.76f, 0.79f, 0.84f);
            Widgets.Label(new Rect(10f, uy, innerWidth - 20f, subtitleH), subtitle);
            GUI.color = Color.white;
            uy += subtitleH;

            foreach (var line in display.UndatedLines)
            {
                var bullet = "• " + line;
                var lh = Math.Max(20f, Text.CalcHeight(bullet, innerWidth - 20f) + 2f);
                Widgets.Label(new Rect(10f, uy, innerWidth - 20f, lh), bullet);
                uy += lh;
            }
        }

        Widgets.EndScrollView();
    }

    private void OpenEntryChart(ChartTarget[] targets)
    {
        if (targets.Length == 1)
        {
            Find.WindowStack.Add(new Dialog_MetricChart(view, targets[0].MetricKey, colonyKey));
            return;
        }

        var options = targets
            .Select(t => new FloatMenuOption(
                (string)"RWS_Chronicle_OpenChartMenu".Translate(t.Label),
                () => Find.WindowStack.Add(new Dialog_MetricChart(view, t.MetricKey, colonyKey))))
            .ToList();
        if (options.Count > 0)
            Find.WindowStack.Add(new FloatMenu(options));
    }

    private ChronicleDisplayCache EnsureDisplayCache(ColonyChronicle chronicle, object? lang)
    {
        if (displayCache is { } cached
            && ReferenceEquals(cached.Chronicle, chronicle)
            && ReferenceEquals(cached.Language, lang)
            && cached.Filter == filter
            && cached.NewestFirst == newestFirst)
        {
            return cached;
        }

        var researchDef = MetricLookup.Definition(view, ResearchFamily.CompletedTicks);
        var total = chronicle.Entries.Count;
        int highlights = 0, intervalsAndGaps = 0, research = 0;
        foreach (var e in chronicle.Entries)
        {
            if (MatchesFilter(e, FilterMode.Highlights)) highlights++;
            if (MatchesFilter(e, FilterMode.IntervalsAndGaps)) intervalsAndGaps++;
            if (MatchesFilter(e, FilterMode.Research)) research++;
        }

        var filtered = chronicle.Entries.Where(e => MatchesFilter(e, filter));
        if (newestFirst)
            filtered = filtered.Reverse();
        var rows = filtered.Select(e => BuildEntryRow(e, researchDef)).ToArray();

        var activeBranch = chronicle.Branches.FirstOrDefault(b => b.LeafNodeIndex == chronicle.SelectedLeafNodeIndex)
            ?? chronicle.Branches.FirstOrDefault();
        var activeBranchLabel = activeBranch is not null ? FormatBranchOptionLabel(activeBranch) : Dialog_RimworldStatistics.Dash;

        var summaryText = (string)"RWS_Chronicle_SummaryLine".Translate(
            rows.Length.ToString(Inv),
            total.ToString(Inv),
            chronicle.BranchNodeChain.Count.ToString(Inv));

        var notices = new List<string>(3);
        if (chronicle.OtherBranchEndingCount > 0)
            notices.Add((string)"RWS_Chronicle_OtherBranchEndingsNotice".Translate(chronicle.OtherBranchEndingCount.ToString(Inv)));
        if (chronicle.AmbiguousBranchEndingCount > 0)
            notices.Add((string)"RWS_Chronicle_AmbiguousEndingsNotice".Translate(chronicle.AmbiguousBranchEndingCount.ToString(Inv)));
        if (chronicle.OmittedEntryCount > 0)
            notices.Add((string)"RWS_Chronicle_OmittedCapNotice".Translate(chronicle.OmittedEntryCount.ToString(Inv)));

        var undatedLines = chronicle.UndatedTraces.Select(FormatUndatedTrace).ToArray();

        displayCache = new ChronicleDisplayCache(
            Chronicle: chronicle,
            Language: lang,
            Filter: filter,
            NewestFirst: newestFirst,
            ActiveBranchLabel: activeBranchLabel,
            SummaryText: summaryText,
            NoticeLines: notices.ToArray(),
            Rows: rows,
            UndatedLines: undatedLines,
            TotalCount: total,
            HighlightsCount: highlights,
            IntervalsAndGapsCount: intervalsAndGaps,
            ResearchCount: research);
        return displayCache;
    }

    private DisplayEntryRow BuildEntryRow(ChronicleEntry e, MetricDefinition? researchCompletedDef)
    {
        var timeText = e.TimingKind switch
        {
            ChronicleTimingKind.ExactTick => (string)"RWS_Chronicle_DayExact".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
            ChronicleTimingKind.HistorySampleDay => (string)"RWS_Chronicle_DaySample".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
            _ => (string)"RWS_Chronicle_DayInterval".Translate(
                Dialog_RimworldStatistics.Days(e.StartTick),
                Dialog_RimworldStatistics.Days(e.EndTick)),
        };

        var (kindText, kindColor) = e.Kind switch
        {
            ChronicleEntryKind.Ending => ((string)"RWS_Chronicle_Kind_Ending".Translate(), EndingColor),
            ChronicleEntryKind.Milestone => ((string)"RWS_Chronicle_Kind_Milestone".Translate(), MilestoneColor),
            ChronicleEntryKind.ResearchCompleted => ((string)"RWS_Chronicle_Kind_Research".Translate(), ResearchColor),
            ChronicleEntryKind.VerifiedInterval => ((string)"RWS_Chronicle_Kind_Interval".Translate(), IntervalColor),
            _ => ((string)"RWS_Chronicle_Kind_Gap".Translate(), GapColor),
        };

        var (eventText, extraNote) = FormatEventText(e, researchCompletedDef);
        var evidenceBase = FormatEvidenceSource(e.EvidenceSource);
        var evidenceText = e.Coverage == MetricCoverage.Partial
            ? evidenceBase + " · " + (string)"RWS_Coverage_Partial".Translate()
            : evidenceBase;

        var targets = ResolveChartTargets(e);

        var tip = new StringBuilder(220);
        tip.Append(kindText).Append(" — ").Append(timeText);
        tip.Append('\n').Append(eventText);
        tip.Append('\n').Append((string)"RWS_Chronicle_ColEvidence".Translate()).Append(": ").Append(evidenceText);

        if (e.TimingKind == ChronicleTimingKind.HistorySampleDay)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipHistorySample".Translate());
        else if (e.TimingKind == ChronicleTimingKind.ProvenInterval && e.Kind == ChronicleEntryKind.VerifiedInterval)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipInterval".Translate());

        if (extraNote is not null)
            tip.Append('\n').Append(extraNote);

        if (e.ParentSaveName is { Length: > 0 } pSave && e.ChildSaveName is { Length: > 0 } cSave)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipSaves".Translate(pSave, cSave));
        else if (e.ChildSaveName is { Length: > 0 } singleSave)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipSaveSingle".Translate(singleSave));

        if (e.RealTimeUtc is { } utc)
            tip.Append('\n').Append((string)"RWS_Chronicle_TipRealTime".Translate(utc.ToString("yyyy-MM-dd HH:mm", Inv)));

        if (targets.Length > 0)
            tip.Append("\n\n").Append((string)"RWS_Chart_OpenTip".Translate());

        return new DisplayEntryRow(e, timeText, kindText, kindColor, eventText, evidenceText, tip.ToString(), targets);
    }

    private (string Text, string? ExtraNote) FormatEventText(ChronicleEntry e, MetricDefinition? researchCompletedDef)
    {
        switch (e.Kind)
        {
            case ChronicleEntryKind.Ending:
            {
                var typeName = e.EndingType is { } t ? Dialog_RimworldStatistics.EndingLabel(t) : Dialog_RimworldStatistics.Dash;
                var head = e.EndingOrdinal is > 0 and { } ord
                    ? (string)"RWS_Chronicle_EndingDetail".Translate(typeName, (ord + 1).ToString(Inv))
                    : typeName;

                var parts = new List<string>(4) { head };
                if (e.DepartedCount is > 0 and { } dep)
                    parts.Add((string)"RWS_Chronicle_EndingDeparted".Translate(dep.ToString(Inv)));
                if (e.PartyAtEnding is > 0 and { } party)
                    parts.Add((string)"RWS_Chronicle_EndingParty".Translate(party.ToString(Inv)));
                if (e.ColonistsKilledAtEnding is { } deathsAt)
                    parts.Add((string)"RWS_Chronicle_EndingDeathsAt".Translate(deathsAt.ToString(Inv)));

                var text = string.Join(" · ", parts);
                if (e.DevMode == true)
                    text += " " + (string)"RWS_Chronicle_EndingDevTag".Translate();
                else if (e.DevMode is null)
                    text += " " + (string)"RWS_Chronicle_EndingDevUnknown".Translate();
                return (text, null);
            }

            case ChronicleEntryKind.Milestone:
            {
                var mText = e.MilestoneKind switch
                {
                    ChronicleMilestoneKind.FirstColonistJoined => (string)"RWS_Chronicle_Milestone_FirstJoin".Translate(),
                    ChronicleMilestoneKind.FirstEnemyRaid => (string)"RWS_Chronicle_Milestone_FirstRaid".Translate(),
                    ChronicleMilestoneKind.FirstColonistDeath => (string)"RWS_Chronicle_Milestone_FirstDeath".Translate(),
                    ChronicleMilestoneKind.FirstResearchCompleted => (string)"RWS_Chronicle_Milestone_FirstResearch".Translate(),
                    ChronicleMilestoneKind.WealthFirst100K => (string)"RWS_Chronicle_Milestone_Wealth100K".Translate(),
                    ChronicleMilestoneKind.WealthFirst1M => (string)"RWS_Chronicle_Milestone_Wealth1M".Translate(),
                    ChronicleMilestoneKind.ColonistsFirst10 => (string)"RWS_Chronicle_Milestone_Colonists10".Translate(),
                    ChronicleMilestoneKind.WealthPeakDay when e.MilestoneValue is { } peak =>
                        (string)"RWS_Chronicle_Milestone_WealthPeak".Translate(Math.Round(peak).ToString("N0", Inv)),
                    ChronicleMilestoneKind.WealthPeakDay => (string)"RWS_Chronicle_Milestone_WealthPeakNoVal".Translate(),
                    _ => Dialog_RimworldStatistics.Dash,
                };
                return (mText, null);
            }

            case ChronicleEntryKind.ResearchCompleted:
            {
                var defKey = e.ResearchDefKey ?? "";
                var (projLabel, unloaded) = ResolveResearchProjectLabel(defKey, researchCompletedDef);
                var note = unloaded ? (string)"RWS_Chronicle_ResearchUnloadedNote".Translate() : null;
                return ((string)"RWS_Chronicle_ResearchDone".Translate(projLabel), note);
            }

            case ChronicleEntryKind.VerifiedInterval:
            {
                var chunks = new List<string>(e.IntervalFacts.Count);
                foreach (var fact in e.IntervalFacts)
                {
                    var mLabel = ResolveMetricLabel(fact.MetricKey);
                    var sign = fact.Delta >= 0 ? "+" : "";
                    var numText = Math.Abs(fact.Delta - Math.Round(fact.Delta)) < 1e-6
                        ? ((long)Math.Round(fact.Delta)).ToString("N0", Inv)
                        : fact.Delta.ToString("N1", Inv);
                    chunks.Add($"{mLabel} {sign}{numText}");
                }
                return (string.Join(" · ", chunks), null);
            }

            case ChronicleEntryKind.RecordingGap:
            default:
            {
                var gapText = e.GapKind switch
                {
                    ChronicleGapKind.MissingParentCheckpoint =>
                        (string)"RWS_Chronicle_Gap_MissingParent".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.UnprovenSaveWithoutCheckpoint =>
                        (string)"RWS_Chronicle_Gap_UnprovenSave".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.CarriedCheckpointUnrecordedSpan =>
                        (string)"RWS_Chronicle_Gap_CarriedSpan".Translate(
                            Dialog_RimworldStatistics.Days(e.StartTick),
                            Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.LiveRecordingStartedLateOrReset =>
                        (string)"RWS_Chronicle_Gap_LateLiveStart".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.ArchivedMetricsUnavailable =>
                        (string)"RWS_Chronicle_Gap_ArchiveUnavailable".Translate(Dialog_RimworldStatistics.Days(e.EndTick)),
                    ChronicleGapKind.ResearchKeyCapReached =>
                        (string)"RWS_Chronicle_Gap_ResearchCap".Translate(),
                    ChronicleGapKind.EnvironmentChanged =>
                        (string)"RWS_Chronicle_Gap_EnvChanged".Translate(FormatEnvChangeSummary(e)),
                    _ => Dialog_RimworldStatistics.Dash,
                };
                return (gapText, null);
            }
        }
    }

    private static string FormatEnvChangeSummary(ChronicleEntry e)
    {
        var items = new List<string>(3);
        if (e.VersionChanged) items.Add((string)"RWS_Chronicle_Env_Version".Translate());
        if (e.ModSetChanged) items.Add((string)"RWS_Chronicle_Env_ModList".Translate());
        if (e.DlcChanged) items.Add((string)"RWS_Chronicle_Env_Dlc".Translate());
        return items.Count > 0 ? string.Join(", ", items) : Dialog_RimworldStatistics.Dash;
    }

    private static (string Label, bool Unloaded) ResolveResearchProjectLabel(string defKey, MetricDefinition? completedDef)
    {
        if (string.IsNullOrEmpty(defKey))
            return (Dialog_RimworldStatistics.Dash, false);

        var named = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(defKey);
        if (named is not null && !string.IsNullOrWhiteSpace(named.label))
            return (named.LabelCap.ToString(), false);

        if (completedDef is not null)
        {
            var viaBreakdown = MetricText.Breakdown(completedDef, defKey);
            if (!string.Equals(viaBreakdown, defKey, StringComparison.Ordinal))
                return (viaBreakdown, false);
        }

        if (OptionalTransitionRecording.TryParseQualifiedDefKey(defKey, out _, out var shortName) && !string.IsNullOrEmpty(shortName))
        {
            var byShort = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(shortName);
            if (byShort is not null && !string.IsNullOrWhiteSpace(byShort.label))
                return (byShort.LabelCap.ToString(), false);
            return (shortName, true);
        }

        return (defKey, true);
    }

    private string ResolveMetricLabel(string metricKey)
    {
        if (BasicMetrics.Contains(metricKey))
        {
            return metricKey switch
            {
                BasicMetrics.PeakWealth => "RWS_Col_PeakWealth".Translate(),
                BasicMetrics.LastWealth => "RWS_Col_LastWealth".Translate(),
                _ => MetricText.BasicLabel(metricKey),
            };
        }
        return MetricLookup.Definition(view, metricKey) is { } def ? MetricText.Label(def) : metricKey;
    }

    private ChartTarget[] ResolveChartTargets(ChronicleEntry e)
    {
        if (e.Kind == ChronicleEntryKind.VerifiedInterval)
        {
            var list = new List<ChartTarget>(e.IntervalFacts.Count);
            foreach (var f in e.IntervalFacts)
            {
                if (MetricLookup.IsKnownKey(view, f.MetricKey))
                    list.Add(new ChartTarget(f.MetricKey, ResolveMetricLabel(f.MetricKey)));
            }
            return list.ToArray();
        }

        if (e.RelatedMetricKey is { } key && MetricLookup.IsKnownKey(view, key))
            return [new ChartTarget(key, ResolveMetricLabel(key))];

        return [];
    }

    private static string FormatEvidenceSource(ChronicleEvidenceSource source) => source switch
    {
        ChronicleEvidenceSource.LiveEvent => "RWS_Chronicle_Evidence_Live".Translate(),
        ChronicleEvidenceSource.CheckpointAggregate => "RWS_Chronicle_Evidence_Checkpoint".Translate(),
        ChronicleEvidenceSource.VanillaCounterInterval => "RWS_Chronicle_Evidence_VanillaInterval".Translate(),
        ChronicleEvidenceSource.SaveHistoryGraph => "RWS_Chronicle_Evidence_HistoryGraph".Translate(),
        ChronicleEvidenceSource.RecoveredLineage => "RWS_Chronicle_Evidence_Recovered".Translate(),
        ChronicleEvidenceSource.SaveMetadata => "RWS_Chronicle_Evidence_Metadata".Translate(),
        ChronicleEvidenceSource.SeededTrace => "RWS_Chronicle_Evidence_SeededTrace".Translate(),
        _ => "RWS_Chronicle_Evidence_SaveTrace".Translate(),
    };

    private static string FormatUndatedTrace(ChronicleUndatedTrace t)
    {
        var typeName = Dialog_RimworldStatistics.EndingLabel(t.EndingType);
        var srcName = FormatEvidenceSource(t.EvidenceSource);
        var line = $"{typeName} — {srcName}";
        if (t.DevMode == true)
            line += " " + (string)"RWS_Chronicle_EndingDevTag".Translate();
        return line;
    }

    private static string FormatBranchOptionLabel(ChronicleBranchOption b)
    {
        var day = Dialog_RimworldStatistics.Days(b.SaveTick);
        var file = string.IsNullOrWhiteSpace(b.PrimaryFileName)
            ? (string)"RWS_Chronicle_BranchNoFile".Translate()
            : b.PrimaryFileName!;

        if (b.IsRepresentative && b.Kind == SaveLineageNodeKind.Checkpoint)
            return (string)"RWS_Chronicle_BranchRep".Translate(day, file);
        if (b.Kind == SaveLineageNodeKind.CarriedCheckpointSave)
            return (string)"RWS_Chronicle_BranchCarried".Translate(day, file);
        if (b.Kind != SaveLineageNodeKind.Checkpoint)
            return (string)"RWS_Chronicle_BranchUnproven".Translate(day, file);
        if (b.HasRewindEvidence)
            return (string)"RWS_Chronicle_BranchRewind".Translate(day, file);
        return (string)"RWS_Chronicle_BranchAlt".Translate(day, file);
    }
}
