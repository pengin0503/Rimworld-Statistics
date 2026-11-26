using System.Globalization;
using RimWorld;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Read-only evidence-based colony chronicle viewer &amp; standalone Markdown/HTML exporter (V2-A / §4.2.6).
/// Shows confirmed endings, milestones, completed research, verified checkpoint-to-checkpoint intervals, and explicit
/// recording gaps strictly on one selected save branch; undated traces remain in a separate section.
/// </summary>
internal sealed class Dialog_ColonyChronicle : Window
{
    private const float HeaderRowHeight = 25f;
    private const float RowHeight = 30f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly ChronicleKindFilter[] SelectableKinds =
    [
        ChronicleKindFilter.Highlights,
        ChronicleKindFilter.IntervalsAndGaps,
        ChronicleKindFilter.Research,
        ChronicleKindFilter.Tales,
    ];

    private static readonly Color WarningTextColor = new(0.95f, 0.72f, 0.45f);
    private static readonly Color FieldErrorLabelColor = new(0.96f, 0.58f, 0.44f);
    private static readonly Color FieldErrorBorderColor = new(0.95f, 0.45f, 0.35f, 0.85f);

    /// <summary>
    /// Branch-level localized rows and search terms built once per loaded <see cref="ColonyChronicle"/> and active language,
    /// so text and day-range edits never re-read saves, rebuild the chronicle, or re-query Def/translation tables.
    /// </summary>
    private sealed record ChronicleBranchCache(
        ColonyChronicle Chronicle,
        object? Language,
        string ActiveBranchLabel,
        string[] BaseNoticeLines,
        ChronicleFilterableEntry<ChronicleDisplayEntryRow>[] Entries,
        ChronicleFilterableUndatedTrace<string>[] UndatedTraces);

    private sealed record ChronicleDisplayCache(
        ChronicleBranchCache Branch,
        ChronicleFilterQuery Query,
        int KindVersion,
        ChronicleFilterResult<ChronicleDisplayEntryRow, string> Result,
        string SummaryText,
        string[] NoticeLines,
        string EmptyMessage,
        bool EmptyMessageIsWarning);

    private readonly StatsView view;
    private readonly string? scopeName;
    private string colonyKey;
    private string colonyName = "";
    private string? originalFactionName;
    // Screen-local kind filter: null means All, an empty set means None, otherwise the selected kinds are shown.
    private HashSet<ChronicleKindFilter>? selectedKinds;
    private int kindFilterVersion;
    private bool newestFirst;
    private string searchText = "";
    private string startDayText = "";
    private string endDayText = "";

    private sealed record ChronicleLayoutCache(
        ChronicleDisplayCache Display,
        object? Language,
        float InnerWidth,
        string SummaryBlock,
        float SummaryHeight,
        float EmptyLabelHeight,
        string UndatedSubtitle,
        float UndatedSubtitleHeight,
        float[] UndatedLineHeights,
        float UndatedHeight,
        float ContentHeight);

    private Task<ColonyChronicle?>? chronicleTask;
    private bool loadFailureLogged;
    private object? cachedLanguage;
    private string? cachedNameSignature;
    private ChronicleBranchCache? branchCache;
    private ChronicleDisplayCache? displayCache;
    private ChronicleLayoutCache? layoutCache;
    private object? cachedHeaderLanguage;
    private string[]? cachedHeaders;
    private ColumnPlan? tablePlan;
    private float cachedTableWidth = -1f;
    private Vector2 scroll;

    private int exportGeneration;
    private int activeExportGeneration;
    private Task<ColonyChronicleExportResult>? exportTask;
    private ColonyChronicleExportResult? exportCompleted;
    private string? exportError;

    internal Dialog_ColonyChronicle(
        StatsView view,
        string colonyKey,
        ChronicleBranchKey? initialBranch = null,
        string? scopeName = null)
    {
        this.view = view;
        this.colonyKey = colonyKey;
        this.scopeName = scopeName;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;

        StartLoad(colonyKey, initialBranch);
    }

    public override Vector2 InitialSize => new(
        Math.Min(1160f, Verse.UI.screenWidth * 0.92f),
        Math.Min(780f, Verse.UI.screenHeight * 0.90f));

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            DrawContents(rect);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] The colony chronicle failed to draw.\n{e}", "RWS.chronicle".GetHashCode());
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
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
        if (exportTask is { IsCompleted: true } doneExport)
        {
            var matchedGeneration = activeExportGeneration == exportGeneration;
            exportTask = null;
            if (matchedGeneration)
            {
                if (doneExport.IsCompletedSuccessfully)
                    exportCompleted = doneExport.Result;
                else
                    exportError = doneExport.Exception?.GetBaseException().Message ?? "RWS_Error_Unknown".Translate();
            }
            else
            {
                _ = doneExport.Exception;
            }
        }

        var lang = LanguageDatabase.activeLanguage;
        var languageChanged = !ReferenceEquals(cachedLanguage, lang);
        if (languageChanged)
        {
            cachedLanguage = lang;
            branchCache = null;
            displayCache = null;
            tablePlan = null;
        }

        var nameSig = ColonyPresentation.Signature(Dialog_RimworldStatistics.ActivePreferences);
        if (languageChanged || cachedNameSignature != nameSig)
        {
            cachedNameSignature = nameSig;
            RefreshColonyName();
        }

        var titleText = (string)"RWS_Chronicle_Title".Translate(colonyName);
        if (originalFactionName is { Length: > 0 } rawFaction && !string.Equals(colonyName, rawFaction, StringComparison.Ordinal))
            titleText += " (" + (string)"RWS_Report_OriginalFactionSubtitle".Translate(rawFaction) + ")";

        using (UiScopes.GuiState())
        {
            Text.Font = GameFont.Medium;
            var titleRect = new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 36f), 32f);
            var shownTitle = titleText.Truncate(titleRect.width);
            Widgets.Label(titleRect, shownTitle);
            if (shownTitle != titleText)
                TooltipHandler.TipRegion(titleRect, titleText);
        }

        var rawChronicle = chronicleTask is { IsCompletedSuccessfully: true } done ? done.Result : null;
        var currentChronicle = rawChronicle is not null && rawChronicle.ColonyKey == colonyKey ? rawChronicle : null;
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
            var branchBtn = (string)"RWS_Chronicle_BranchButton".Translate(display.Branch.ActiveBranchLabel);
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
                FilterSelectionName(), display.Result.Rows.Length.ToString(Inv));
            var filterRect = controls.Next(filterLabel, 145f, 260f);
            var shownFilter = filterLabel.Truncate(Math.Max(0f, filterRect.width - 12f));
            if (Widgets.ButtonText(filterRect, shownFilter))
                ShowFilterMenu(display);
            if (shownFilter != filterLabel)
                TooltipHandler.TipRegion(filterRect, filterLabel);

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
            UiScopes.OpenOrReplaceWindow(new Dialog_ColonySummaryReport(view, colonyKey, scopeName));
        TooltipHandler.TipRegion(reportRect, "RWS_Report_OpenTip".Translate());

        var lineageLabel = (string)"RWS_Lineage_Open".Translate();
        var lineageRect = controls.Next(lineageLabel, 120f, 215f);
        if (Widgets.ButtonText(lineageRect, lineageLabel.Truncate(Math.Max(0f, lineageRect.width - 12f))))
            UiScopes.OpenOrReplaceWindow(new Dialog_ColonySaveLineage(view, colonyKey, null, null, scopeName));
        TooltipHandler.TipRegion(lineageRect, "RWS_Lineage_OpenTip".Translate());

        if (display is not null)
        {
            var mdLabel = (string)"RWS_Chronicle_ExportMarkdown".Translate();
            var mdRect = controls.Next(mdLabel, 115f, 210f);
            if (Widgets.ButtonText(mdRect, mdLabel.Truncate(Math.Max(0f, mdRect.width - 12f)), active: exportTask is null))
                StartChronicleExport(display, ChronicleExportFormat.Markdown);
            TooltipHandler.TipRegion(mdRect, "RWS_Chronicle_ExportMarkdownTip".Translate());

            var htmlLabel = (string)"RWS_Chronicle_ExportHtml".Translate();
            var htmlRect = controls.Next(htmlLabel, 115f, 210f);
            if (Widgets.ButtonText(htmlRect, htmlLabel.Truncate(Math.Max(0f, htmlRect.width - 12f)), active: exportTask is null))
                StartChronicleExport(display, ChronicleExportFormat.Html);
            TooltipHandler.TipRegion(htmlRect, "RWS_Chronicle_ExportHtmlTip".Translate());

            if (exportCompleted is { } exported)
            {
                var openFolder = (string)"RWS_Backup_OpenFolder".Translate();
                var openRect = controls.Next(openFolder, 90f, 165f);
                if (Widgets.ButtonText(openRect, openFolder.Truncate(Math.Max(0f, openRect.width - 12f))))
                    Application.OpenURL(new Uri(Path.GetFullPath(exported.DirectoryPath) + Path.DirectorySeparatorChar).AbsoluteUri);

                var copyPath = (string)"RWS_Backup_CopyPath".Translate();
                var copyRect = controls.Next(copyPath, 90f, 165f);
                if (Widgets.ButtonText(copyRect, copyPath.Truncate(Math.Max(0f, copyRect.width - 12f))))
                    GUIUtility.systemCopyBuffer = exported.FilePath;
            }
        }

        y += controls.Height + 6f;

        var exportStatus = exportTask is not null ? (string)"RWS_Chronicle_Exporting".Translate()
            : exportError is not null ? (string)"RWS_Chronicle_ExportError".Translate(exportError)
            : exportCompleted is { } doneResult ? (string)"RWS_Chronicle_ExportDone".Translate(doneResult.FilePath)
            : null;
        if (exportStatus is not null)
        {
            var statusRect = new Rect(rect.x, y, rect.width, 22f);
            using (UiScopes.GuiState())
            {
                GUI.color = exportError is not null ? ColorLibrary.RedReadable : new Color(0.82f, 0.88f, 0.92f);
                Widgets.Label(statusRect, exportStatus.Truncate(statusRect.width));
            }
            TooltipHandler.TipRegion(statusRect, exportStatus);
            y += 24f;
        }

        if (currentChronicle is not null && display is not null)
        {
            var filterControls = new ControlRow(new Rect(rect.x, y, rect.width, 28f));

            var searchChanged = DrawLabeledField(
                ref filterControls,
                (string)"RWS_Chronicle_SearchLabel".Translate(),
                ref searchText,
                fieldWidth: 210f,
                tooltip: (string)"RWS_Chronicle_SearchTip".Translate(),
                highlightError: false);

            var dayTip = (string)"RWS_Chronicle_DayRangeTip".Translate();
            var startChanged = DrawLabeledField(
                ref filterControls,
                (string)"RWS_Chronicle_StartDayLabel".Translate(),
                ref startDayText,
                fieldWidth: 76f,
                tooltip: dayTip,
                highlightError: display.Query.DayRange.HighlightStartField);

            var endChanged = DrawLabeledField(
                ref filterControls,
                (string)"RWS_Chronicle_EndDayLabel".Translate(),
                ref endDayText,
                fieldWidth: 76f,
                tooltip: dayTip,
                highlightError: display.Query.DayRange.HighlightEndField);

            var clearLabel = (string)"RWS_Chronicle_ClearFilters".Translate();
            var clearRect = filterControls.Next(clearLabel, 96f, 185f);
            var shownClear = clearLabel.Truncate(Math.Max(0f, clearRect.width - 12f));
            if (shownClear != clearLabel)
                TooltipHandler.TipRegion(clearRect, clearLabel);

            var canClear = HasAnyFilterInput(display.Query);
            bool clearClicked;
            using (UiScopes.GuiState())
            {
                if (!canClear)
                    GUI.color = new Color(0.65f, 0.65f, 0.65f);
                clearClicked = Widgets.ButtonText(clearRect, shownClear, active: canClear);
            }
            if (clearClicked && canClear)
                ClearFilters();

            if (searchChanged || startChanged || endChanged)
            {
                displayCache = null;
                scroll = Vector2.zero;
            }

            if (displayCache is null)
                display = EnsureDisplayCache(currentChronicle, lang);

            y += filterControls.Height + 6f;
        }

        var body = new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y));

        if (chronicleTask is null || !chronicleTask.IsCompleted)
        {
            Widgets.Label(body, "RWS_Loading".Translate());
            return;
        }
        if (!chronicleTask.IsCompletedSuccessfully || display is null)
        {
            // Observed: a failed read is shown and logged once, never rethrown on the finalizer thread.
            if (chronicleTask.Exception is { } failure && !loadFailureLogged)
            {
                loadFailureLogged = true;
                Log.Warning($"[RimworldStatistics] The colony chronicle could not be read.\n{failure.GetBaseException()}");
            }
            Widgets.Label(body, "RWS_Chronicle_LoadFailed".Translate());
            return;
        }

        DrawBody(body, display);
    }

    private void StartChronicleExport(ChronicleDisplayCache display, ChronicleExportFormat format)
    {
        exportCompleted = null;
        exportError = null;
        try
        {
            var text = BuildExportText(display);
            var directory = Path.Combine(ProfileService.DirectoryPath, "exports");
            var gen = ++exportGeneration;
            activeExportGeneration = gen;
            exportTask = Task.Run(() => ColonyChronicleExport.Write(text, directory, format));
        }
        catch (Exception ex)
        {
            exportError = ex.Message;
        }
    }

    private ColonyChronicleExportText BuildExportText(ChronicleDisplayCache display)
    {
        var exportColonyKey = display.Branch.Chronicle.ColonyKey;
        var exportStats = view.Stats.Colonies.FirstOrDefault(c => c.Key == exportColonyKey);
        var exportColonyName = exportStats is not null ? Dialog_RimworldStatistics.ColonyName(exportStats) : exportColonyKey;
        var exportRawFaction = exportStats?.FactionName;
        var origLine = exportRawFaction is { Length: > 0 } rawFaction
            && !string.Equals(exportColonyName, rawFaction, StringComparison.Ordinal)
                ? (string)"RWS_Report_OriginalFactionSubtitle".Translate(rawFaction)
                : null;

        var subtitleParts = new List<string>(3)
        {
            (string)"RWS_Chronicle_BranchButton".Translate(display.Branch.ActiveBranchLabel),
            (string)(newestFirst ? "RWS_Chronicle_OrderNewest" : "RWS_Chronicle_OrderOldest").Translate(),
        };
        if (selectedKinds is not null)
            subtitleParts.Add(FilterSelectionName());

        var entries = display.Result.Rows.Select(ColonyChronicleFormatter.ToExportEntry).ToArray();
        var undated = display.Result.UndatedTraces;

        return new ColonyChronicleExportText
        {
            ColonyKey = exportColonyKey,
            ColonyName = exportColonyName,
            OriginalFactionLine = origLine,
            ModVersion = RimworldStatisticsMod.InstalledVersion,
            Language = LanguageDatabase.activeLanguage?.folderName ?? "en",
            PageTitle = (string)"RWS_Chronicle_Title".Translate(exportColonyName),
            Subtitle = string.Join(" · ", subtitleParts),
            SummaryLine = display.SummaryText,
            NoticeLines = display.NoticeLines,
            ColTime = (string)"RWS_Chronicle_ColTime".Translate(),
            ColKind = (string)"RWS_Chronicle_ColKind".Translate(),
            ColEvent = (string)"RWS_Chronicle_ColEvent".Translate(),
            ColEvidence = (string)"RWS_Chronicle_ColEvidence".Translate(),
            Entries = entries,
            EmptyMessage = display.EmptyMessage,
            UndatedTitle = undated.Length > 0 ? (string)"RWS_Chronicle_UndatedTitle".Translate(undated.Length.ToString(Inv)) : null,
            UndatedSubtitle = undated.Length > 0 ? (string)"RWS_Chronicle_UndatedSubtitle".Translate() : null,
            UndatedLines = undated,
            FooterNote = (string)"RWS_Chronicle_ExportFooterNote".Translate(),
            FooterColonyKeyLabel = (string)"RWS_Export_FooterColonyKey".Translate(),
            FooterGeneratorLabel = (string)"RWS_Export_FooterGenerator".Translate(),
        };
    }

    private static bool DrawLabeledField(
        ref ControlRow controls,
        string label,
        ref string value,
        float fieldWidth,
        string tooltip,
        bool highlightError)
    {
        var labelWidth = Math.Min(150f, Text.CalcSize(label).x + 6f);
        var groupRect = controls.Next(labelWidth + fieldWidth);
        var actualFieldWidth = Math.Max(40f, groupRect.width - labelWidth);
        var actualLabelWidth = Math.Max(0f, groupRect.width - actualFieldWidth);

        using (UiScopes.GuiState())
        {
            if (highlightError)
                GUI.color = FieldErrorLabelColor;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(
                new Rect(groupRect.x, groupRect.y, actualLabelWidth, groupRect.height),
                label.Truncate(actualLabelWidth));
        }

        var fieldRect = new Rect(groupRect.x + actualLabelWidth, groupRect.y + 2f, actualFieldWidth, groupRect.height - 4f);
        var nextValue = Widgets.TextField(fieldRect, value) ?? "";
        if (highlightError)
            Widgets.DrawBoxSolidWithOutline(fieldRect, Color.clear, FieldErrorBorderColor);
        TooltipHandler.TipRegion(groupRect, tooltip);

        if (string.Equals(nextValue, value, StringComparison.Ordinal))
            return false;

        value = nextValue;
        return true;
    }

    private bool HasAnyFilterInput(ChronicleFilterQuery query)
        => selectedKinds is not null
            || query.HasAnyFilter
            || searchText.Length > 0
            || startDayText.Length > 0
            || endDayText.Length > 0;

    private void ClearFilters()
    {
        selectedKinds = null;
        kindFilterVersion++;
        searchText = "";
        startDayText = "";
        endDayText = "";
        displayCache = null;
        scroll = Vector2.zero;
        Verse.UI.UnfocusCurrentControl();
    }

    private void StartLoad(string targetColonyKey, ChronicleBranchKey? branch)
    {
        colonyKey = targetColonyKey;
        RefreshColonyName();
        branchCache = null;
        displayCache = null;
        tablePlan = null;
        scroll = Vector2.zero;
        loadFailureLogged = false;
        exportGeneration++;
        exportTask = null;
        exportCompleted = null;
        exportError = null;

        var v = view;
        var includeDev = ProfileService.Options.IncludeDevMode;
        var session = ProfileService.Session;
        chronicleTask = Task.Run(() => session.ReadColonyChronicle(v, targetColonyKey, branch, includeDev));
    }

    private void RefreshColonyName()
    {
        var stats = view.Stats.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        colonyName = stats is not null ? Dialog_RimworldStatistics.ColonyName(stats) : colonyKey;
        originalFactionName = stats?.FactionName;
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
            picked => StartLoad(picked.Key, null),
            heading: (string)"RWS_Chronicle_SwitchColony".Translate(),
            selectedColonyKey: colonyKey,
            currentInGameColonyKey: currentColonyKey));
    }

    private void ShowBranchMenu(ColonyChronicle chronicle)
    {
        var options = new List<FloatMenuOption>(chronicle.Branches.Count);
        foreach (var b in chronicle.Branches)
        {
            var key = b.Key;
            var label = ColonyChronicleFormatter.FormatBranchOptionLabel(b);
            if (b.LeafNodeIndex == chronicle.SelectedLeafNodeIndex)
                label = "✓ " + label;
            options.Add(new FloatMenuOption(label, () => StartLoad(colonyKey, key)));
        }
        if (options.Count > 0)
            Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowFilterMenu(ChronicleDisplayCache display)
    {
        // Mirrors the checkable FloatMenu convention used by the recording menu: "✓ " / "✗ " before each label.
        string CheckPrefix(bool selected) => selected ? "✓ " : "✗ ";

        var options = new List<FloatMenuOption>(SelectableKinds.Length + 2);
        options.Add(new FloatMenuOption(
            CheckPrefix(selectedKinds is null) + (string)"RWS_Chronicle_FilterAll".Translate(),
            () => SetSelectedKinds(null)));
        options.Add(new FloatMenuOption(
            CheckPrefix(selectedKinds is { Count: 0 }) + (string)"RWS_Chronicle_Filter_None".Translate(),
            () => SetSelectedKinds(new HashSet<ChronicleKindFilter>())));
        foreach (var mode in SelectableKinds)
        {
            var kind = mode;
            var selected = selectedKinds is null || selectedKinds.Contains(kind);
            var text = CheckPrefix(selected) + FilterName(kind)
                + " (" + display.Result.FilterCount(kind).ToString(Inv) + ")";
            options.Add(new FloatMenuOption(text, () => ToggleKind(kind)));
        }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void SetSelectedKinds(HashSet<ChronicleKindFilter>? kinds)
    {
        selectedKinds = kinds;
        kindFilterVersion++;
        displayCache = null;
        scroll = Vector2.zero;
    }

    private void ToggleKind(ChronicleKindFilter kind)
    {
        // Starting from All when no selection is active makes the first click remove that kind from All,
        // matching the previous single-select default while allowing arbitrary combinations afterwards.
        var kinds = selectedKinds is { } current
            ? new HashSet<ChronicleKindFilter>(current)
            : new HashSet<ChronicleKindFilter>(SelectableKinds);
        if (!kinds.Add(kind))
            kinds.Remove(kind);
        SetSelectedKinds(kinds);
    }

    private string FilterSelectionName()
    {
        if (selectedKinds is null)
            return (string)"RWS_Chronicle_FilterAll".Translate();
        if (selectedKinds.Count == 0)
            return (string)"RWS_Chronicle_Filter_None".Translate();

        var names = new string[selectedKinds.Count];
        var index = 0;
        foreach (var mode in SelectableKinds)
        {
            if (selectedKinds.Contains(mode))
                names[index++] = FilterName(mode);
        }
        return string.Join(" + ", names);
    }

    private static string FilterName(ChronicleKindFilter mode) => mode switch
    {
        ChronicleKindFilter.Highlights => "RWS_Chronicle_FilterHighlights".Translate(),
        ChronicleKindFilter.IntervalsAndGaps => "RWS_Chronicle_FilterIntervalsAndGaps".Translate(),
        ChronicleKindFilter.Research => "RWS_Chronicle_FilterResearch".Translate(),
        ChronicleKindFilter.Tales => "RWS_Chronicle_FilterTales".Translate(),
        _ => "RWS_Chronicle_FilterAll".Translate(),
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

        var rows = display.Result.Rows;
        var undatedLines = display.Result.UndatedTraces;
        var lang = LanguageDatabase.activeLanguage;
        var layout = layoutCache;
        if (layout is null
            || !ReferenceEquals(layout.Display, display)
            || !ReferenceEquals(layout.Language, lang)
            || Math.Abs(layout.InnerWidth - innerWidth) > 0.5f)
        {
            var summaryBlock = display.NoticeLines.Length > 0
                ? display.SummaryText + "\n" + string.Join("\n", display.NoticeLines)
                : display.SummaryText;
            var summaryHeight = Math.Max(26f, Text.CalcHeight(summaryBlock, innerWidth - 16f) + 12f);
            var emptyLabelWidth = Math.Max(80f, innerWidth - 8f);
            var emptyLabelHeight = rows.Length == 0
                ? Math.Max(24f, Text.CalcHeight(display.EmptyMessage, emptyLabelWidth) + 4f)
                : 0f;
            var tableBodyHeight = rows.Length == 0
                ? emptyLabelHeight + 8f
                : rows.Length * RowHeight;
            var tableTotalHeight = HeaderRowHeight + tableBodyHeight + 8f;

            var subtitle = (string)"RWS_Chronicle_UndatedSubtitle".Translate();
            var subtitleH = 0f;
            var undatedHeights = undatedLines.Length > 0 ? new float[undatedLines.Length] : [];
            var undatedHeight = 0f;
            if (undatedLines.Length > 0)
            {
                subtitleH = Math.Max(20f, Text.CalcHeight(subtitle, innerWidth - 20f) + 4f);
                var linesH = 0f;
                for (var i = 0; i < undatedLines.Length; i++)
                {
                    var h = Math.Max(20f, Text.CalcHeight("• " + undatedLines[i], innerWidth - 20f) + 2f);
                    undatedHeights[i] = h;
                    linesH += h;
                }
                undatedHeight = 28f + 14f + subtitleH + linesH + 8f;
            }
            var contentHeight = summaryHeight + 8f + tableTotalHeight + undatedHeight;
            layout = new ChronicleLayoutCache(
                display, lang, innerWidth, summaryBlock, summaryHeight, emptyLabelHeight,
                subtitle, subtitleH, undatedHeights, undatedHeight, contentHeight);
            layoutCache = layout;
        }

        var viewHeight = Math.Max(outRect.height, layout.ContentHeight);
        var viewWidth = Math.Max(innerWidth, tablePlan.TotalWidth);
        var viewRect = new Rect(0f, 0f, viewWidth, viewHeight);

        using (UiScopes.ScrollView(outRect, ref scroll, viewRect))
        using (UiScopes.GuiState())
        {
            var cy = 0f;

            // Summary & notice card.
            var summaryRect = new Rect(0f, cy, innerWidth, layout.SummaryHeight);
            Widgets.DrawBoxSolidWithOutline(summaryRect,
                new Color(0.14f, 0.16f, 0.19f, 0.85f),
                new Color(0.38f, 0.52f, 0.66f, 0.55f));
            Widgets.Label(new Rect(8f, cy + 6f, innerWidth - 16f, layout.SummaryHeight - 12f), layout.SummaryBlock);
            cy += layout.SummaryHeight + 8f;

            cy = DrawTable(display, cy, innerWidth, outRect.height, layout.EmptyLabelHeight);

            // Separate undated traces section so undated evidence never receives a fabricated day.
            if (undatedLines.Length > 0)
                DrawUndated(undatedLines, cy, innerWidth, layout);
        }
    }

    private float DrawTable(ChronicleDisplayCache display, float cy, float innerWidth, float viewportHeight, float emptyLabelHeight)
    {
        var plan = tablePlan!;
        var widths = plan.Widths;
        var rows = display.Result.Rows;
        var lang = LanguageDatabase.activeLanguage;
        if (cachedHeaders is null || !ReferenceEquals(cachedHeaderLanguage, lang))
        {
            cachedHeaderLanguage = lang;
            cachedHeaders =
            [
                "RWS_Chronicle_ColTime".Translate(),
                "RWS_Chronicle_ColKind".Translate(),
                "RWS_Chronicle_ColEvent".Translate(),
                "RWS_Chronicle_ColEvidence".Translate(),
            ];
        }
        var headers = cachedHeaders;

        using (UiScopes.GuiState())
        {
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
            Widgets.DrawBoxSolid(new Rect(0f, cy + HeaderRowHeight - 1f, plan.TotalWidth, 1f), new Color(1f, 1f, 1f, 0.15f));
        }
        cy += HeaderRowHeight;

        if (rows.Length == 0)
        {
            using (UiScopes.GuiState())
            {
                GUI.color = display.EmptyMessageIsWarning
                    ? WarningTextColor
                    : new Color(0.72f, 0.75f, 0.80f);
                Widgets.Label(
                    new Rect(4f, cy + 6f, Math.Max(80f, innerWidth - 8f), emptyLabelHeight),
                    display.EmptyMessage);
            }
            return cy + emptyLabelHeight + 8f + 8f;
        }

        // Only the rows inside the scroll viewport are drawn.
        var viewportTop = scroll.y - cy;
        var viewportBottom = scroll.y + viewportHeight - cy;
        var firstRow = Math.Max(0, (int)Math.Floor(viewportTop / RowHeight) - 1);
        var lastRow = Math.Min(rows.Length - 1, (int)Math.Ceiling(viewportBottom / RowHeight) + 1);

        for (var i = firstRow; i <= lastRow; i++)
        {
            var r = rows[i];
            var ry = cy + i * RowHeight;
            var rowRect = new Rect(0f, ry, plan.TotalWidth, RowHeight);

            if (i % 2 == 1)
                Widgets.DrawHighlight(rowRect, 0.22f);
            var chartable = r.ChartTargets.Length > 0;
            if (chartable)
                Widgets.DrawHighlightIfMouseover(rowRect);
            var hovered = chartable && Mouse.IsOver(rowRect);

            using (UiScopes.GuiState())
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                var rx = 0f;

                var timeCell = new Rect(rx + 4f, ry, Math.Max(0f, widths[0] - 8f), RowHeight);
                Widgets.Label(timeCell, r.TimeText.Truncate(timeCell.width));
                rx += widths[0];

                var kindCell = new Rect(rx + 4f, ry, Math.Max(0f, widths[1] - 8f), RowHeight);
                GUI.color = r.KindColor;
                Widgets.Label(kindCell, r.KindText.Truncate(kindCell.width));
                GUI.color = Color.white;
                rx += widths[1];

                var iconReserve = chartable ? 18f : 0f;
                var eventCell = new Rect(rx + 4f, ry, Math.Max(0f, widths[2] - 8f - iconReserve), RowHeight);
                Widgets.Label(eventCell, r.EventText.Truncate(eventCell.width));
                if (chartable)
                {
                    var iconRect = new Rect(rx + widths[2] - 18f, ry + (RowHeight - 12f) * 0.5f, 12f, 12f);
                    MetricVisualStyle.DrawMiniChartIcon(iconRect, hovered);
                }
                rx += widths[2];

                var evCell = new Rect(rx + 4f, ry, Math.Max(0f, widths[3] - 8f), RowHeight);
                GUI.color = r.Entry.Coverage == MetricCoverage.Partial
                    ? new Color(0.92f, 0.79f, 0.52f)
                    : new Color(0.82f, 0.85f, 0.90f);
                Widgets.Label(evCell, r.EvidenceText.Truncate(evCell.width));
            }

            TooltipHandler.TipRegion(rowRect, r.Tooltip);
            if (chartable && Widgets.ButtonInvisible(rowRect))
                OpenEntryChart(r.ChartTargets);
        }

        return cy + rows.Length * RowHeight + 8f;
    }

    private static void DrawUndated(string[] undatedLines, float cy, float innerWidth, ChronicleLayoutCache layout)
    {
        using (UiScopes.GuiState())
        {
            GUI.color = new Color(0.58f, 0.82f, 0.94f);
            Widgets.Label(new Rect(0f, cy, innerWidth, 23f),
                "RWS_Chronicle_UndatedTitle".Translate(undatedLines.Length.ToString(Inv)));
            Widgets.DrawBoxSolid(new Rect(0f, cy + 23f, innerWidth, 1f), new Color(1f, 1f, 1f, 0.14f));
        }
        cy += 28f;

        var subtitleH = layout.UndatedSubtitleHeight;
        var linesH = layout.UndatedHeight - 28f - 14f - subtitleH - 8f;
        Widgets.DrawMenuSection(new Rect(0f, cy, innerWidth, 14f + subtitleH + linesH));

        var uy = cy + 7f;
        using (UiScopes.GuiState())
        {
            GUI.color = new Color(0.76f, 0.79f, 0.84f);
            Widgets.Label(new Rect(10f, uy, innerWidth - 20f, subtitleH), layout.UndatedSubtitle);
        }
        uy += subtitleH;

        for (var i = 0; i < undatedLines.Length; i++)
        {
            var bullet = "• " + undatedLines[i];
            var lh = layout.UndatedLineHeights[i];
            Widgets.Label(new Rect(10f, uy, innerWidth - 20f, lh), bullet);
            uy += lh;
        }
    }

    private void OpenEntryChart(ChronicleChartTarget[] targets)
    {
        if (targets.Length == 1)
        {
            UiScopes.OpenOrReplaceWindow(new Dialog_MetricChart(view, targets[0].MetricKey, colonyKey, scopeName));
            return;
        }

        var options = targets
            .Select(t => new FloatMenuOption(
                (string)"RWS_Chronicle_OpenChartMenu".Translate(t.Label),
                () => UiScopes.OpenOrReplaceWindow(new Dialog_MetricChart(view, t.MetricKey, colonyKey, scopeName))))
            .ToList();
        if (options.Count > 0)
            Find.WindowStack.Add(new FloatMenu(options));
    }

    private ChronicleBranchCache EnsureBranchCache(ColonyChronicle chronicle, object? lang)
    {
        if (branchCache is { } cached
            && ReferenceEquals(cached.Chronicle, chronicle)
            && ReferenceEquals(cached.Language, lang))
        {
            return cached;
        }

        var researchDef = MetricLookup.Definition(view, ResearchFamily.CompletedTicks);
        var entries = new ChronicleFilterableEntry<ChronicleDisplayEntryRow>[chronicle.Entries.Count];
        for (var i = 0; i < chronicle.Entries.Count; i++)
        {
            var entry = chronicle.Entries[i];
            var row = ColonyChronicleFormatter.BuildEntryRow(view, entry, researchDef);
            var additionalTerms = ColonyChronicleFormatter.BuildAdditionalSearchTerms(view, entry, researchDef);
            entries[i] = new ChronicleFilterableEntry<ChronicleDisplayEntryRow>(entry, row.EventText, additionalTerms, row);
        }

        var undated = new ChronicleFilterableUndatedTrace<string>[chronicle.UndatedTraces.Count];
        for (var i = 0; i < chronicle.UndatedTraces.Count; i++)
        {
            var trace = chronicle.UndatedTraces[i];
            var traceName = Dialog_RimworldStatistics.EndingLabel(trace.EndingType);
            var endingKey = Dialog_RimworldStatistics.EndingLabelKey(trace.EndingType);
            // U46: the keyed ending is also found by its English name and key.
            var traceSearch = string.Join(" ", traceName, endingKey, SearchCandidates.EnglishText(endingKey));
            undated[i] = new ChronicleFilterableUndatedTrace<string>(
                trace, traceSearch, ColonyChronicleFormatter.FormatUndatedTrace(trace));
        }

        var activeBranchLabel = chronicle.SelectedBranch is { } activeBranch
            ? ColonyChronicleFormatter.FormatBranchOptionLabel(activeBranch)
            : Dialog_RimworldStatistics.Dash;

        var notices = ColonyChronicleFormatter.BuildBaseNotices(chronicle);

        branchCache = new ChronicleBranchCache(
            Chronicle: chronicle,
            Language: lang,
            ActiveBranchLabel: activeBranchLabel,
            BaseNoticeLines: notices,
            Entries: entries,
            UndatedTraces: undated);
        return branchCache;
    }

    private ChronicleDisplayCache EnsureDisplayCache(ColonyChronicle chronicle, object? lang)
    {
        var branch = EnsureBranchCache(chronicle, lang);
        var query = ChronicleFilterQuery.Create(ChronicleKindFilter.All, searchText, startDayText, endDayText, newestFirst);

        if (displayCache is { } cached
            && ReferenceEquals(cached.Branch, branch)
            && cached.Query == query
            && cached.KindVersion == kindFilterVersion)
        {
            return cached;
        }

        // Core still applies a single-kind query (All): keep its per-kind counts and search/day semantics intact,
        // then narrow the dated rows locally to the session's selected kinds.
        var result = ColonyChronicleFilter.Apply(branch.Entries, branch.UndatedTraces, query);
        if (selectedKinds is not null)
        {
            var rows = new List<ChronicleDisplayEntryRow>(result.Rows.Length);
            foreach (var row in result.Rows)
            {
                if (MatchesSelectedKind(row.Entry))
                    rows.Add(row);
            }
            result = result with { Rows = rows.ToArray() };
        }

        var summaryText = (string)"RWS_Chronicle_SummaryLine".Translate(
            result.Rows.Length.ToString(Inv),
            chronicle.Entries.Count.ToString(Inv),
            chronicle.BranchNodeChain.Count.ToString(Inv));

        string[] noticeLines;
        if (result.HiddenUndatedTraceCountByDayRange > 0)
        {
            var list = new List<string>(branch.BaseNoticeLines.Length + 1);
            list.AddRange(branch.BaseNoticeLines);
            list.Add((string)"RWS_Chronicle_UndatedHiddenByDayNotice".Translate(
                result.HiddenUndatedTraceCountByDayRange.ToString(Inv)));
            noticeLines = list.ToArray();
        }
        else
        {
            noticeLines = branch.BaseNoticeLines;
        }

        var (emptyMessage, emptyWarning) = ResolveEmptyMessage(query);

        displayCache = new ChronicleDisplayCache(
            Branch: branch,
            Query: query,
            KindVersion: kindFilterVersion,
            Result: result,
            SummaryText: summaryText,
            NoticeLines: noticeLines,
            EmptyMessage: emptyMessage,
            EmptyMessageIsWarning: emptyWarning);
        return displayCache;
    }

    private bool MatchesSelectedKind(ChronicleEntry entry)
    {
        if (selectedKinds is null)
            return true;
        foreach (var mode in selectedKinds)
        {
            if (ColonyChronicleFilter.MatchesKind(entry, mode))
                return true;
        }
        return false;
    }

    private static (string Message, bool IsWarning) ResolveEmptyMessage(ChronicleFilterQuery query)
    {
        if (query.DayRange.HasInvalidNumber)
            return ((string)"RWS_Chronicle_InvalidDayRange".Translate(), true);
        if (query.DayRange.IsReversed)
            return ((string)"RWS_Chronicle_ReversedDayRange".Translate(), true);
        if (query.HasSearch || query.HasDayInput)
            return ((string)"RWS_Chronicle_NoFilterMatches".Translate(), false);
        return ((string)"RWS_Chronicle_Empty".Translate(), false);
    }
}
