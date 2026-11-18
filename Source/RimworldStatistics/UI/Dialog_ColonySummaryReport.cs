using System.Globalization;
using System.Text;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Read-only single-colony lifetime summary report &amp; static HTML exporter (V2-C).
/// Aggregates overview metadata, endings, coverage counts, key lifetime/peak/snapshot records, cross-colony ranks
/// and MOD statistics, with one-click navigation to <see cref="Dialog_MetricChart"/> and <see cref="Dialog_ColonySaveLineage"/>.
/// </summary>
internal sealed class Dialog_ColonySummaryReport : Window
{
    private const float HeaderRowHeight = 25f;
    private const float BaseRowHeight = 28f;
    private const float ModRowHeight = 42f;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed record DisplayRow(
        ColonyReportMetricItem Item,
        string Label,
        string? ModSource,
        string ValueText,
        string KindText,
        string RankText,
        string StateText,
        string RangeText,
        string Tooltip,
        bool Chartable,
        bool IsMissingOrNa,
        bool IsPartial);

    private sealed record ReportDisplayCache(
        ColonySummaryReport Report,
        ColonyChronicle? Chronicle,
        object? Language,
        string ColonyName,
        string? OriginalFactionLine,
        string ScopeLabel,
        string? Banner,
        (string Key, string Value)[] OverviewFields,
        string[] EndingLines,
        string? EndingFootnote,
        string CoverageLine,
        DisplayRow[] KeyRecords,
        DisplayRow[] TopRanks,
        DisplayRow[] ModMetrics,
        ColonyChronicleSectionHtmlText? ChronicleSection,
        bool AnyPartialRank);

    private sealed record InGameLoadedState(
        StatsView View,
        InGameColonyResolution Resolution,
        string? ActiveColonyKey,
        ColonySummaryReport? Report,
        ColonyChronicle? Chronicle,
        RecordedPointInfo ActiveRecordedPoint);

    private StatsView? view;
    private readonly string? explicitScopeName;
    private readonly bool isInGameEntry;
    private string? inGameCampaignId;
    private readonly string? fallbackFactionName;
    private readonly string? fallbackWorldName;
    private readonly RetainedBuild<(long Revision, string? CampaignId, string? ColonyOverride), InGameLoadedState>? inGameBuild;
    private ProfileSession? inGameSession;
    private bool inGameIncludesDevMode;
    private string? selectedColonyOverride;
    private static Exception? loggedInGameSessionFailure;

    private string colonyKey;
    private string colonyName = "";

    private Task<(ColonySummaryReport? Report, ColonyChronicle? Chronicle)>? reportTask;
    private object? cachedLanguage;
    private string? cachedNameSignature;
    private ReportDisplayCache? displayCache;
    private ColumnPlan? tablePlan;
    private float cachedTableWidth = -1f;
    private Vector2 scroll;

    private int exportGeneration;
    private int activeExportGeneration;
    private Task<ColonyReportHtmlExportResult>? exportTask;
    private ColonyReportHtmlExportResult? exportCompleted;
    private string? exportError;

    internal Dialog_ColonySummaryReport(StatsView view, string colonyKey, string? scopeName = null)
    {
        this.view = view;
        this.colonyKey = colonyKey;
        explicitScopeName = scopeName;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;

        StartLoad(colonyKey);
    }

    private Dialog_ColonySummaryReport(string? campaignId, string? fallbackFactionName, string? fallbackWorldName)
    {
        isInGameEntry = true;
        inGameCampaignId = campaignId;
        this.fallbackFactionName = fallbackFactionName;
        this.fallbackWorldName = fallbackWorldName;
        inGameBuild = new();
        colonyKey = "";
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = false;
    }

    internal static Dialog_ColonySummaryReport CreateForInGame(
        string? campaignId,
        string? fallbackFactionName,
        string? fallbackWorldName)
        => new(campaignId, fallbackFactionName, fallbackWorldName);

    public override Vector2 InitialSize => new(
        Math.Min(1160f, Verse.UI.screenWidth * 0.92f),
        Math.Min(780f, Verse.UI.screenHeight * 0.90f));

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            if (ProfileService.RestoredPreferencesPending)
                ProfileService.ApplyRestoredPreferences();

            if (exportTask is { IsCompleted: true } doneExport)
            {
                var matchedGeneration = activeExportGeneration == exportGeneration;
                exportTask = null;
                if (matchedGeneration)
                {
                    if (doneExport.IsCompletedSuccessfully)
                        exportCompleted = doneExport.Result;
                    else
                        exportError = doneExport.Exception?.GetBaseException().Message ?? "Unknown error";
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
                displayCache = null;
                tablePlan = null;
            }
            var nameSig = ColonyPresentation.Signature(Dialog_RimworldStatistics.ActivePreferences);
            var namesChanged = cachedNameSignature != nameSig;
            if (namesChanged)
                cachedNameSignature = nameSig;

            InGameLoadedState? inGameState = null;
            ColonySummaryReport? currentReport;
            ColonyChronicle? currentChronicle;
            if (isInGameEntry)
            {
                if (ProfileService.DataOperationRunning)
                {
                    colonyName = FallbackColonyName();
                    DrawWindowTitle(rect, null);
                    Widgets.Label(new Rect(rect.x, rect.y + 36f, rect.width, Math.Max(0f, rect.height - 36f)), "RWS_Backup_Working".Translate());
                    return;
                }

                var session = ProfileService.TryGetSession(out var loadFailure);
                if (session is null)
                {
                    colonyName = FallbackColonyName();
                    DrawWindowTitle(rect, null);
                    var waitBody = new Rect(rect.x, rect.y + 36f, rect.width, Math.Max(0f, rect.height - 36f));
                    if (loadFailure is null)
                    {
                        Widgets.Label(waitBody, "RWS_Loading".Translate());
                        return;
                    }
                    if (!ReferenceEquals(loggedInGameSessionFailure, loadFailure))
                    {
                        loggedInGameSessionFailure = loadFailure;
                        Log.Error($"[RimworldStatistics] The record file could not be opened.\n{loadFailure}");
                    }
                    Dialog_RimworldStatistics.DrawFailure(waitBody, "RWS_SessionLoadFailed".Translate(), loadFailure, running: false, ProfileService.RetryLoading);
                    return;
                }

                inGameState = ResolveInGameState(session);
                if (inGameState is null)
                {
                    colonyName = FallbackColonyName();
                    DrawWindowTitle(rect, null);
                    var waitBody = new Rect(rect.x, rect.y + 36f, rect.width, Math.Max(0f, rect.height - 36f));
                    if (inGameBuild!.Failure is { } buildFailure)
                        Dialog_RimworldStatistics.DrawFailure(waitBody, "RWS_ViewBuildFailed".Translate(), buildFailure, inGameBuild.Running, inGameBuild.Retry);
                    else
                        Widgets.Label(waitBody, "RWS_Loading".Translate());
                    return;
                }

                if (inGameState.ActiveColonyKey is null)
                {
                    view = inGameState.View;
                    colonyKey = "";
                    colonyName = FallbackColonyName();
                    DrawWindowTitle(rect, null);
                    DrawUnrecordedBody(new Rect(rect.x, rect.y + 36f, rect.width, Math.Max(0f, rect.height - 36f)));
                    return;
                }

                if (!ReferenceEquals(view, inGameState.View) || colonyKey != inGameState.ActiveColonyKey || languageChanged || namesChanged)
                {
                    view = inGameState.View;
                    colonyKey = inGameState.ActiveColonyKey;
                    RefreshColonyName();
                    displayCache = null;
                }

                currentReport = inGameState.Report;
                currentChronicle = inGameState.Chronicle;
            }
            else
            {
                if (languageChanged || namesChanged)
                    RefreshColonyName();
                if (reportTask is { IsCompletedSuccessfully: true } t)
                    (currentReport, currentChronicle) = t.Result;
                else
                    (currentReport, currentChronicle) = (null, null);
            }

            var activeView = view!;
            DrawWindowTitle(rect, currentReport?.Overview.FactionName);

            var y = rect.y + 36f;
            var controls = new ControlRow(new Rect(rect.x, y, rect.width, 28f));

            if (activeView.Colonies.Count > 1)
            {
                var switchLabel = (string)"RWS_Report_SwitchColony".Translate();
                var switchRect = controls.Next(switchLabel, 130f, 220f);
                if (Widgets.ButtonText(switchRect, switchLabel.Truncate(Math.Max(0f, switchRect.width - 12f))))
                    ShowColonyMenu(activeView);
            }

            var lineageLabel = (string)"RWS_Lineage_Open".Translate();
            var lineageRect = controls.Next(lineageLabel, 125f, 220f);
            if (Widgets.ButtonText(lineageRect, lineageLabel.Truncate(Math.Max(0f, lineageRect.width - 12f))))
                Find.WindowStack.Add(new Dialog_ColonySaveLineage(activeView, colonyKey));
            TooltipHandler.TipRegion(lineageRect, "RWS_Lineage_OpenTip".Translate());

            var chronicleLabel = (string)"RWS_Chronicle_Open".Translate();
            var chronicleRect = controls.Next(chronicleLabel, 120f, 215f);
            if (Widgets.ButtonText(chronicleRect, chronicleLabel.Truncate(Math.Max(0f, chronicleRect.width - 12f))))
                Find.WindowStack.Add(new Dialog_ColonyChronicle(activeView, colonyKey));
            TooltipHandler.TipRegion(chronicleRect, "RWS_Chronicle_OpenTip".Translate());

            var exportLabel = (string)"RWS_Report_ExportHtml".Translate();
            var exportRect = controls.Next(exportLabel, 140f, 250f);
            if (Widgets.ButtonText(exportRect, exportLabel.Truncate(Math.Max(0f, exportRect.width - 12f)),
                active: currentReport is not null && exportTask is null) && currentReport is not null)
            {
                StartHtmlExport(currentReport, currentChronicle);
            }
            TooltipHandler.TipRegion(exportRect, "RWS_Report_ExportHtmlTip".Translate());

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

            if (isInGameEntry)
            {
                var allStatsLabel = (string)"RWS_InGame_OpenAllStats".Translate();
                var allStatsRect = controls.Next(allStatsLabel, 125f, 230f);
                if (Widgets.ButtonText(allStatsRect, allStatsLabel.Truncate(Math.Max(0f, allStatsRect.width - 12f))))
                {
                    OpenAllStatisticsWindow();
                    return;
                }
                TooltipHandler.TipRegion(allStatsRect, "RWS_InGame_OpenAllStatsTip".Translate());
            }

            y += controls.Height + 4f;

            var exportStatus = exportTask is not null ? (string)"RWS_Report_Exporting".Translate()
                : exportError is not null ? (string)"RWS_Report_ExportError".Translate(exportError)
                : exportCompleted is { } done ? (string)"RWS_Report_ExportDone".Translate(done.FilePath)
                : null;
            if (exportStatus is not null)
            {
                var statusRect = new Rect(rect.x, y, rect.width, 22f);
                GUI.color = exportError is not null ? ColorLibrary.RedReadable : new Color(0.82f, 0.88f, 0.92f);
                Widgets.Label(statusRect, exportStatus.Truncate(statusRect.width));
                GUI.color = Color.white;
                TooltipHandler.TipRegion(statusRect, exportStatus);
                y += 24f;
            }

            if (inGameState is not null)
                DrawInGameSavedNotice(ref y, rect, inGameState);

            var body = new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y));
            if (!isInGameEntry)
            {
                if (reportTask is null || !reportTask.IsCompleted)
                {
                    Widgets.Label(body, "RWS_Loading".Translate());
                    return;
                }
                if (!reportTask.IsCompletedSuccessfully)
                {
                    _ = reportTask.Exception;
                    Widgets.Label(body, "RWS_WindowError".Translate());
                    return;
                }
            }
            if (currentReport is null)
            {
                Widgets.Label(body, "RWS_NoData".Translate());
                return;
            }

            var display = EnsureDisplayCache(currentReport, currentChronicle, lang);
            DrawReportBody(body, display);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] Colony summary report failed to draw.\n{e}", "RWS.report.window".GetHashCode());
            Widgets.Label(rect.ContractedBy(20f), "RWS_WindowError".Translate());
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }
    }

    private void DrawWindowTitle(Rect rect, string? rawFactionName)
    {
        var titleText = (string)"RWS_Report_Title".Translate(colonyName);
        if (rawFactionName is { Length: > 0 } raw && !string.Equals(colonyName, raw, StringComparison.Ordinal))
            titleText += " (" + (string)"RWS_Report_OriginalFactionSubtitle".Translate(raw) + ")";

        Text.Font = GameFont.Medium;
        var titleRect = new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 36f), 32f);
        var shownTitle = titleText.Truncate(titleRect.width);
        Widgets.Label(titleRect, shownTitle);
        if (shownTitle != titleText)
            TooltipHandler.TipRegion(titleRect, titleText);
        Text.Font = GameFont.Small;
    }

    private void DrawInGameSavedNotice(ref float y, Rect rect, InGameLoadedState state)
    {
        string notice;
        var isCurrentColony = state.Resolution.Found && state.ActiveColonyKey == state.Resolution.ColonyKey;
        if (isCurrentColony)
        {
            if (state.ActiveRecordedPoint.HasObservedPoint)
            {
                var timeText = Dialog_RimworldStatistics.Local(state.ActiveRecordedPoint.LastRecordedUtc);
                var dayText = state.ActiveRecordedPoint.LastRecordedTicks is { } ticks
                    ? Dialog_RimworldStatistics.DaysWithYears(ticks)
                    : Dialog_RimworldStatistics.Dash;
                notice = (string)"RWS_InGame_SavedNotice".Translate(timeText, dayText);
            }
            else
            {
                notice = (string)"RWS_InGame_SavedNoticeEndingOnly".Translate();
            }
        }
        else
        {
            var timeText = Dialog_RimworldStatistics.Local(state.ActiveRecordedPoint.LastRecordedUtc);
            var dayText = state.ActiveRecordedPoint.HasObservedPoint && state.ActiveRecordedPoint.LastRecordedTicks is { } ticks
                ? Dialog_RimworldStatistics.DaysWithYears(ticks)
                : Dialog_RimworldStatistics.Dash;
            notice = (string)"RWS_InGame_OtherColonySavedNotice".Translate(timeText, dayText);
        }

        var textWidth = Math.Max(80f, rect.width - 24f);
        var boxHeight = Math.Max(28f, Text.CalcHeight(notice, textWidth) + 10f);
        var boxRect = new Rect(rect.x, y, rect.width, boxHeight);
        Widgets.DrawBoxSolid(boxRect, new Color(0.14f, 0.23f, 0.30f, 0.62f));
        Widgets.DrawBoxSolid(new Rect(rect.x, y, 4f, boxHeight), new Color(0.42f, 0.76f, 0.92f));
        Widgets.Label(new Rect(rect.x + 12f, y + 5f, textWidth, boxHeight - 10f), notice);
        y += boxHeight + 6f;
    }

    private void DrawUnrecordedBody(Rect rect)
    {
        var innerWidth = Math.Max(120f, rect.width - 28f);
        var heading = (string)"RWS_InGame_UnrecordedTitle".Translate();
        var explanation = (string)"RWS_InGame_UnrecordedBody".Translate();
        var openLabel = (string)"RWS_InGame_OpenAllStats".Translate();

        var headingHeight = Math.Max(24f, Text.CalcHeight(heading, innerWidth) + 4f);
        var bodyHeight = Math.Max(28f, Text.CalcHeight(explanation, innerWidth) + 6f);
        var cardHeight = 16f + headingHeight + 6f + bodyHeight + 12f + 32f + 16f;
        var cardRect = new Rect(rect.x, rect.y + 4f, rect.width, Math.Min(Math.Max(0f, rect.height - 4f), cardHeight));

        Widgets.DrawMenuSection(cardRect);
        Widgets.DrawBoxSolid(new Rect(cardRect.x, cardRect.y, 4f, cardRect.height), new Color(0.42f, 0.76f, 0.92f));

        var cy = cardRect.y + 14f;
        GUI.color = new Color(0.72f, 0.88f, 0.96f);
        Widgets.Label(new Rect(cardRect.x + 16f, cy, innerWidth, headingHeight), heading);
        GUI.color = Color.white;
        cy += headingHeight + 6f;

        Widgets.Label(new Rect(cardRect.x + 16f, cy, innerWidth, bodyHeight), explanation);
        cy += bodyHeight + 12f;

        var buttonWidth = Math.Min(innerWidth, Math.Max(180f, Text.CalcSize(openLabel).x + 28f));
        var buttonRect = new Rect(cardRect.x + 16f, cy, buttonWidth, 32f);
        if (Widgets.ButtonText(buttonRect, openLabel.Truncate(Math.Max(0f, buttonRect.width - 12f))))
            OpenAllStatisticsWindow();
        TooltipHandler.TipRegion(buttonRect, "RWS_InGame_OpenAllStatsTip".Translate());
    }

    private void OpenAllStatisticsWindow()
    {
        Close(doCloseSound: false);
        InGameStatisticsLauncher.OpenAllStatistics();
    }

    private InGameLoadedState? ResolveInGameState(ProfileSession session)
    {
        var options = ProfileService.Options;
        var visibleUnits = MetricDisplayPolicy.AllDetailsVisibleUnits;
        var scope = ViewScope.All;
        if (!ReferenceEquals(inGameSession, session) || inGameIncludesDevMode != options.IncludeDevMode)
        {
            inGameSession = session;
            inGameIncludesDevMode = options.IncludeDevMode;
            inGameBuild!.Reset();
        }

        var liveCampaignId = Current.ProgramState == ProgramState.Playing
            ? GameComponent_RimworldStatistics.CurrentOrNull?.campaignId
            : null;
        if (liveCampaignId != inGameCampaignId)
        {
            inGameCampaignId = liveCampaignId;
            selectedColonyOverride = null;
        }

        // Flush any queued profile mutations before reading Revision (#322).
        _ = session.TryGetCachedView(options, visibleUnits, scope);
        var key = (session.Revision, inGameCampaignId, selectedColonyOverride);
        var capturedCampaignId = inGameCampaignId;
        var capturedOverride = selectedColonyOverride;
        var failure = inGameBuild!.Update(key, () => Task.Run(() =>
        {
            var builtView = session.GetView(options, visibleUnits, scope);
            var resolution = InGameColonyAccess.Resolve(builtView, capturedCampaignId);
            string? targetKey = null;
            if (capturedOverride is { Length: > 0 } ov && builtView.Colonies.Any(c => c.Key == ov))
                targetKey = ov;
            else if (resolution.Found)
                targetKey = resolution.ColonyKey;

            var (report, chronicle) = targetKey is not null
                ? session.ReadColonySummaryAndChronicle(builtView, targetKey, options.IncludeDevMode)
                : (null, null);
            var recordedPoint = targetKey is not null
                ? (targetKey == resolution.ColonyKey
                    ? resolution.RecordedPoint
                    : InGameColonyAccess.DescribeRecordedPoint(builtView, targetKey))
                : RecordedPointInfo.None;
            return new InGameLoadedState(builtView, resolution, targetKey, report, chronicle, recordedPoint);
        }));
        if (failure is not null)
            Log.Error("[RimworldStatistics] The in-game colony summary could not be calculated. It is calculated again when the "
                + $"record changes or on Retry.\n{failure}");
        return inGameBuild.Failure is null ? inGameBuild.Value : null;
    }

    private void StartLoad(string targetColonyKey)
    {
        colonyKey = targetColonyKey;
        RefreshColonyName();
        scroll = Vector2.zero;
        displayCache = null;
        exportGeneration++;
        exportTask = null;
        exportCompleted = null;
        exportError = null;

        var session = ProfileService.Session;
        var currentView = view!;
        var includeDev = ProfileService.Options.IncludeDevMode;
        reportTask = Task.Run(() => session.ReadColonySummaryAndChronicle(currentView, targetColonyKey, includeDev));
    }

    private void RefreshColonyName()
    {
        if (view is null)
        {
            colonyName = FallbackColonyName();
            return;
        }
        var stats = view.Stats.Colonies.FirstOrDefault(c => c.Key == colonyKey);
        colonyName = stats is not null ? Dialog_RimworldStatistics.ColonyName(stats) : colonyKey;
    }

    private string FallbackColonyName()
        => fallbackFactionName is { Length: > 0 } name
            ? name
            : (string)"RWS_UnnamedColony".Translate(fallbackWorldName is { Length: > 0 } world ? world : Dialog_RimworldStatistics.Dash);

    private void SelectColony(string targetColonyKey)
    {
        if (!isInGameEntry)
        {
            StartLoad(targetColonyKey);
            return;
        }

        var resolvedCurrent = inGameBuild?.Value?.Resolution.ColonyKey;
        selectedColonyOverride = targetColonyKey == resolvedCurrent ? null : targetColonyKey;
        scroll = Vector2.zero;
        displayCache = null;
        exportGeneration++;
        exportTask = null;
        exportCompleted = null;
        exportError = null;
    }

    private void ShowColonyMenu(StatsView activeView)
    {
        if (activeView.Stats.Colonies.Count == 0)
            return;
        var currentColonyKey = isInGameEntry
            ? inGameBuild?.Value?.Resolution.ColonyKey
            : Current.ProgramState == ProgramState.Playing
                ? InGameColonyAccess.Resolve(activeView, GameComponent_RimworldStatistics.CurrentOrNull?.campaignId).ColonyKey
                : null;
        Find.WindowStack.Add(new ColonyPickerDialog(
            activeView.Stats.Colonies,
            Dialog_RimworldStatistics.ActivePreferences,
            picked => SelectColony(picked.Key),
            heading: (string)"RWS_Report_SwitchColony".Translate(),
            selectedColonyKey: colonyKey,
            currentInGameColonyKey: currentColonyKey));
    }

    private void StartHtmlExport(ColonySummaryReport report, ColonyChronicle? chronicle)
    {
        exportCompleted = null;
        exportError = null;
        try
        {
            var display = EnsureDisplayCache(report, chronicle, LanguageDatabase.activeLanguage);
            var text = BuildHtmlText(display);
            var directory = Path.Combine(ProfileService.DirectoryPath, "exports");
            var gen = ++exportGeneration;
            activeExportGeneration = gen;
            exportTask = Task.Run(() => ColonyReportHtmlExport.Write(report, text, directory));
        }
        catch (Exception ex)
        {
            exportError = ex.Message;
        }
    }

    private ColonyReportHtmlText BuildHtmlText(ReportDisplayCache display)
    {
        static ColonyReportHtmlItemText MapItem(DisplayRow r) => new(
            r.Label, r.ModSource, r.ValueText, r.KindText, r.RankText, r.StateText, r.RangeText);

        var endingLines = display.EndingFootnote is { Length: > 0 } foot
            ? display.EndingLines.Append(foot).ToArray()
            : display.EndingLines;

        return new ColonyReportHtmlText
        {
            ColonyName = colonyName,
            ScopeName = display.ScopeLabel,
            ModVersion = RimworldStatisticsMod.InstalledVersion,
            Language = LanguageDatabase.activeLanguage?.folderName ?? "en",
            PageTitle = (string)"RWS_Report_Title".Translate(colonyName),
            Subtitle = (string)"RWS_Report_HtmlSubtitle".Translate(display.ScopeLabel),
            OriginalFactionLine = display.OriginalFactionLine,
            StatusBanner = display.Banner,
            SectionOverview = (string)"RWS_Report_SectionOverview".Translate(),
            SectionEndings = (string)"RWS_Report_SectionEndings".Translate(),
            SectionCoverage = (string)"RWS_Report_SectionCoverage".Translate(),
            SectionKeyRecords = (string)"RWS_Report_SectionKeyRecords".Translate(),
            SectionTopRanks = (string)"RWS_Report_SectionTopRanks".Translate(),
            SectionModMetrics = (string)"RWS_Report_SectionModMetrics".Translate(),
            OverviewFields = display.OverviewFields,
            EndingLines = endingLines,
            EndingsEmptyText = (string)"RWS_Endings_None".Translate(),
            CoverageSummaryLine = display.CoverageLine,
            ColMetric = (string)"RWS_Details_Col_Name".Translate(),
            ColValue = (string)"RWS_Details_Col_Value".Translate(),
            ColKind = (string)"RWS_Report_Col_Kind".Translate(),
            ColRank = (string)"RWS_Report_Col_Rank".Translate(),
            ColState = (string)"RWS_Details_Col_State".Translate(),
            ColRange = (string)"RWS_Report_Col_Range".Translate(),
            KeyRecords = display.KeyRecords.Select(MapItem).ToArray(),
            TopRanks = display.TopRanks.Select(MapItem).ToArray(),
            ModMetrics = display.ModMetrics.Select(MapItem).ToArray(),
            ChronicleSection = display.ChronicleSection,
            FooterNote = (string)"RWS_Report_HtmlFooterNote".Translate(),
        };
    }

    private void DrawReportBody(Rect outRect, ReportDisplayCache display)
    {
        var innerWidth = Math.Max(120f, outRect.width - 16f);
        if (tablePlan is null || Math.Abs(cachedTableWidth - innerWidth) > 0.5f)
        {
            cachedTableWidth = innerWidth;
            tablePlan = ColumnLayout.Resolve(
                [new(185, 4), new(95, 2), new(95, 2), new(88, 1), new(120, 2), new(105, 2)],
                innerWidth);
        }

        // Compute content height before scroll view.
        var overviewCols = Math.Max(1, (int)((innerWidth + 12f) / 260f));
        var overviewRows = (display.OverviewFields.Length + overviewCols - 1) / overviewCols;
        var overviewCardHeight = 16f + overviewRows * 42f;

        var bannerHeight = display.Banner is { } b ? Math.Max(28f, Text.CalcHeight(b, innerWidth - 20f) + 12f) + 8f : 0f;
        var endingsTextHeight = display.EndingLines.Length == 0
            ? 24f
            : display.EndingLines.Sum(l => Math.Max(20f, Text.CalcHeight("• " + l, innerWidth - 24f) + 2f));
        if (display.EndingFootnote is { } fn)
            endingsTextHeight += Math.Max(20f, Text.CalcHeight(fn, innerWidth - 24f) + 4f);
        var endingsCardHeight = 16f + endingsTextHeight;
        var coverageCardHeight = 16f + Math.Max(22f, Text.CalcHeight(display.CoverageLine, innerWidth - 24f));

        static float SectionTableHeight(DisplayRow[] rows)
            => HeaderRowHeight + rows.Sum(r => r.ModSource is not null ? ModRowHeight : BaseRowHeight) + 6f;

        var totalHeight = bannerHeight
            + 26f + overviewCardHeight + 12f
            + 26f + endingsCardHeight + 12f
            + 26f + coverageCardHeight + 14f
            + 26f + SectionTableHeight(display.KeyRecords) + 12f;

        if (display.TopRanks.Length > 0)
            totalHeight += 26f + SectionTableHeight(display.TopRanks) + 12f;
        if (display.ModMetrics.Length > 0)
            totalHeight += 26f + SectionTableHeight(display.ModMetrics) + 12f;
        if (display.AnyPartialRank)
            totalHeight += 26f;

        var viewRect = new Rect(0f, 0f, innerWidth, totalHeight + 8f);
        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        try
        {
            var cy = 0f;
            if (display.Banner is { } banner)
            {
                var boxHeight = bannerHeight - 8f;
                var bannerRect = new Rect(0f, cy, innerWidth, boxHeight);
                Widgets.DrawBoxSolid(bannerRect, new Color(0.28f, 0.22f, 0.12f, 0.55f));
                Widgets.DrawBoxSolid(new Rect(0f, cy, 4f, boxHeight), new Color(0.92f, 0.72f, 0.32f));
                Widgets.Label(new Rect(12f, cy + 6f, innerWidth - 20f, boxHeight - 12f), banner);
                cy += bannerHeight;
            }

            // Overview card.
            DrawSectionHeading(ref cy, innerWidth, "RWS_Report_SectionOverview".Translate());
            var overviewRect = new Rect(0f, cy, innerWidth, overviewCardHeight);
            Widgets.DrawMenuSection(overviewRect);
            var cellWidth = (innerWidth - 20f - (overviewCols - 1) * 12f) / overviewCols;
            for (var i = 0; i < display.OverviewFields.Length; i++)
            {
                var col = i % overviewCols;
                var row = i / overviewCols;
                var fx = 10f + col * (cellWidth + 12f);
                var fy = cy + 8f + row * 42f;
                var (fieldKey, fieldVal) = display.OverviewFields[i];
                GUI.color = new Color(0.72f, 0.76f, 0.82f);
                Widgets.Label(new Rect(fx, fy, cellWidth, 18f), fieldKey.Truncate(cellWidth));
                GUI.color = Color.white;
                var shownVal = fieldVal.Truncate(cellWidth);
                var valRect = new Rect(fx, fy + 18f, cellWidth, 22f);
                Widgets.Label(valRect, shownVal);
                if (shownVal != fieldVal)
                    TooltipHandler.TipRegion(valRect, fieldVal);
            }
            cy += overviewCardHeight + 12f;

            // Endings card.
            DrawSectionHeading(ref cy, innerWidth, "RWS_Report_SectionEndings".Translate());
            var endingsRect = new Rect(0f, cy, innerWidth, endingsCardHeight);
            Widgets.DrawMenuSection(endingsRect);
            var ey = cy + 8f;
            if (display.EndingLines.Length == 0)
            {
                GUI.color = new Color(0.75f, 0.78f, 0.82f);
                Widgets.Label(new Rect(12f, ey, innerWidth - 24f, 22f), "RWS_Endings_None".Translate());
                GUI.color = Color.white;
                ey += 24f;
            }
            else
            {
                foreach (var line in display.EndingLines)
                {
                    var bullet = "• " + line;
                    var lh = Math.Max(20f, Text.CalcHeight(bullet, innerWidth - 24f) + 2f);
                    Widgets.Label(new Rect(12f, ey, innerWidth - 24f, lh), bullet);
                    ey += lh;
                }
            }
            if (display.EndingFootnote is { } foot)
            {
                var fh = Math.Max(20f, Text.CalcHeight(foot, innerWidth - 24f) + 4f);
                GUI.color = new Color(0.76f, 0.79f, 0.84f);
                Widgets.Label(new Rect(12f, ey, innerWidth - 24f, fh), foot);
                GUI.color = Color.white;
            }
            cy += endingsCardHeight + 12f;

            // Coverage card.
            DrawSectionHeading(ref cy, innerWidth, "RWS_Report_SectionCoverage".Translate());
            var covRect = new Rect(0f, cy, innerWidth, coverageCardHeight);
            Widgets.DrawMenuSection(covRect);
            Widgets.Label(new Rect(12f, cy + 8f, innerWidth - 24f, coverageCardHeight - 16f), display.CoverageLine);
            cy += coverageCardHeight + 14f;

            // Interactive metric tables.
            DrawMetricSection(ref cy, innerWidth, "RWS_Report_SectionKeyRecords".Translate(), display.KeyRecords);
            if (display.TopRanks.Length > 0)
                DrawMetricSection(ref cy, innerWidth, "RWS_Report_SectionTopRanks".Translate(), display.TopRanks);
            if (display.ModMetrics.Length > 0)
                DrawMetricSection(ref cy, innerWidth, "RWS_Report_SectionModMetrics".Translate(), display.ModMetrics);

            if (display.AnyPartialRank)
            {
                GUI.color = new Color(0.78f, 0.80f, 0.84f);
                Widgets.Label(new Rect(0f, cy, innerWidth, 24f), "RWS_Report_PartialRankFootnote".Translate());
                GUI.color = Color.white;
            }
        }
        finally
        {
            Widgets.EndScrollView();
        }
    }

    private static void DrawSectionHeading(ref float y, float width, string heading)
    {
        var prevColor = GUI.color;
        GUI.color = new Color(0.58f, 0.82f, 0.94f);
        Widgets.Label(new Rect(0f, y, width, 23f), heading);
        Widgets.DrawBoxSolid(new Rect(0f, y + 23f, width, 1f), new Color(1f, 1f, 1f, 0.14f));
        GUI.color = prevColor;
        y += 26f;
    }

    private void DrawMetricSection(ref float y, float width, string heading, DisplayRow[] rows)
    {
        DrawSectionHeading(ref y, width, heading);

        var widths = tablePlan!.Widths;
        string[] headers =
        [
            "RWS_Details_Col_Name".Translate(),
            "RWS_Details_Col_Value".Translate(),
            "RWS_Report_Col_Kind".Translate(),
            "RWS_Report_Col_Rank".Translate(),
            "RWS_Details_Col_State".Translate(),
            "RWS_Report_Col_Range".Translate(),
        ];
        bool[] rightAligned = [false, true, false, true, false, true];

        var prevAnchor = Text.Anchor;
        var prevColor = GUI.color;
        GUI.color = new Color(0.78f, 0.80f, 0.84f);
        var hx = 0f;
        for (var c = 0; c < headers.Length; c++)
        {
            var cell = new Rect(hx + 4f, y, Math.Max(0f, widths[c] - 8f), HeaderRowHeight);
            Text.Anchor = rightAligned[c] ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
            var shown = headers[c].Truncate(cell.width);
            Widgets.Label(cell, shown);
            if (shown != headers[c])
                TooltipHandler.TipRegion(cell, headers[c]);
            hx += widths[c];
        }
        GUI.color = prevColor;
        Text.Anchor = prevAnchor;
        y += HeaderRowHeight;

        for (var i = 0; i < rows.Length; i++)
        {
            var r = rows[i];
            var rh = r.ModSource is not null ? ModRowHeight : BaseRowHeight;
            var rowRect = new Rect(0f, y, tablePlan.TotalWidth, rh);
            if (i % 2 == 1)
                Widgets.DrawHighlight(rowRect, 0.22f);
            if (r.Chartable)
                Widgets.DrawHighlightIfMouseover(rowRect);

            var hovered = r.Chartable && Mouse.IsOver(rowRect);
            var rx = 0f;

            // Column 0: Metric label (+ optional MOD source + mini chart icon).
            var iconReserved = r.Chartable ? 18f : 0f;
            var nameWidth = Math.Max(0f, widths[0] - 8f - iconReserved);
            if (r.ModSource is { } modSource)
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rx + 4f, y + 2f, nameWidth, 21f), r.Label.Truncate(nameWidth));
                GUI.color = new Color(0.75f, 0.77f, 0.81f);
                Widgets.Label(new Rect(rx + 4f, y + 21f, nameWidth, 19f), modSource.Truncate(nameWidth));
                GUI.color = Color.white;
            }
            else
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(rx + 4f, y, nameWidth, rh), r.Label.Truncate(nameWidth));
            }
            if (r.Chartable)
            {
                var iconRect = new Rect(rx + widths[0] - 18f, y + (rh - 12f) * 0.5f, 12f, 12f);
                MetricVisualStyle.DrawMiniChartIcon(iconRect, hovered);
            }
            rx += widths[0];

            string[] rest = [r.ValueText, r.KindText, r.RankText, r.StateText, r.RangeText];
            for (var c = 0; c < rest.Length; c++)
            {
                var colIndex = c + 1;
                var cell = new Rect(rx + 4f, y, Math.Max(0f, widths[colIndex] - 8f), rh);
                Text.Anchor = rightAligned[colIndex] ? TextAnchor.MiddleRight : TextAnchor.MiddleLeft;
                GUI.color = colIndex == 1 && r.IsMissingOrNa ? new Color(0.68f, 0.68f, 0.70f)
                    : colIndex == 4 && r.IsPartial ? new Color(0.92f, 0.79f, 0.52f)
                    : Color.white;
                Widgets.Label(cell, rest[c].Truncate(cell.width));
                rx += widths[colIndex];
            }

            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(rowRect, r.Tooltip);
            if (r.Chartable && Widgets.ButtonInvisible(rowRect))
                Find.WindowStack.Add(new Dialog_MetricChart(view!, r.Item.MetricKey, colonyKey));

            y += rh;
        }

        y += 12f;
    }

    private ReportDisplayCache EnsureDisplayCache(ColonySummaryReport report, ColonyChronicle? chronicle, object? lang)
    {
        var scopeLabel = explicitScopeName ?? (string)"RWS_Groups_All".Translate();
        if (displayCache is { } cached
            && ReferenceEquals(cached.Report, report)
            && ReferenceEquals(cached.Chronicle, chronicle)
            && ReferenceEquals(cached.Language, lang)
            && cached.ColonyName == colonyName
            && cached.ScopeLabel == scopeLabel)
        {
            return cached;
        }

        var ov = report.Overview;
        string? banner = null;
        var banners = new List<string>(3);
        if (ov.Excluded)
            banners.Add((string)"RWS_Report_BannerExcluded".Translate());
        if (ov.OutOfScope)
            banners.Add((string)"RWS_Report_BannerOutOfScope".Translate(scopeLabel));
        if (!ov.HasObservedPoint)
            banners.Add((string)"RWS_Report_BannerEndingOnly".Translate());
        if (banners.Count > 0)
            banner = string.Join("\n", banners);

        var permadeathText = ov.Permadeath switch
        {
            true => (string)"RWS_Report_PermadeathYes".Translate(),
            false => (string)"RWS_Report_PermadeathReloadAnytime".Translate(),
            null => Dialog_RimworldStatistics.Dash,
        };
        var versionsText = ov.GameVersions.Count > 0
            ? Dialog_RimworldStatistics.ShortVersions(ov.GameVersions)
            : Dialog_RimworldStatistics.Dash;

        var hasCustomDisplayName = ov.FactionName is { Length: > 0 } rawFaction
            && !string.Equals(colonyName, rawFaction, StringComparison.Ordinal);
        var originalFactionLine = hasCustomDisplayName
            ? (string)"RWS_Report_OriginalFactionSubtitle".Translate(ov.FactionName!)
            : null;

        var lineageText = FormatLineageSummary(ov);
        var overviewList = new List<(string Key, string Value)>(11);
        if (hasCustomDisplayName)
            overviewList.Add(((string)"RWS_Report_Field_OriginalFaction".Translate(), ov.FactionName!));
        overviewList.Add(((string)"RWS_Col_World".Translate(), ov.WorldName ?? Dialog_RimworldStatistics.Dash));
        overviewList.Add(((string)"RWS_Report_Field_Scenario".Translate(), ov.Scenario ?? Dialog_RimworldStatistics.Dash));
        overviewList.Add(((string)"RWS_Report_Field_Storyteller".Translate(), ov.Storyteller ?? Dialog_RimworldStatistics.Dash));
        overviewList.Add(((string)"RWS_Report_Field_Difficulty".Translate(), ov.Difficulty ?? Dialog_RimworldStatistics.Dash));
        overviewList.Add(((string)"RWS_Report_Field_Permadeath".Translate(), permadeathText));
        overviewList.Add(((string)"RWS_Col_Versions".Translate(), versionsText));
        overviewList.Add(((string)"RWS_Col_Saves".Translate(), ov.SaveCount.ToString(Inv)));
        overviewList.Add(((string)"RWS_Col_LastPlayed".Translate(), Dialog_RimworldStatistics.Local(ov.LastPlayedUtc)));
        overviewList.Add(((string)"RWS_Report_Field_Scope".Translate(), scopeLabel));
        overviewList.Add(((string)"RWS_Report_Field_Lineage".Translate(), lineageText));
        var overviewFields = overviewList.ToArray();

        var endingLines = BuildEndingLines(ov);
        var endingNotes = new List<string>(2);
        if (ov.RecoveredEndingCount > 0)
            endingNotes.Add((string)"RWS_Report_RecoveredEndingsNote".Translate(ov.RecoveredEndingCount));
        if (ov.HasDevOnlyEndings && !ProfileService.Options.IncludeDevMode)
            endingNotes.Add((string)"RWS_Report_DevOnlyEndingsNote".Translate());
        var endingFootnote = endingNotes.Count > 0 ? string.Join(" · ", endingNotes) : null;

        var cov = report.Coverage;
        var coverageLine = (string)"RWS_Report_CoverageSummary".Translate(
            cov.TotalMetrics, cov.CompleteCount, cov.SnapshotOnlyCount, cov.PartialCount, cov.UnknownCount, cov.NotApplicableCount);
        if (cov.ExperimentalCount > 0)
            coverageLine += " · " + (string)"RWS_Report_CoverageExperimental".Translate(cov.ExperimentalCount);

        var keyRows = report.KeyRecords.Select(BuildDisplayRow).ToArray();
        var topRows = report.TopRankedHighlights.Select(BuildDisplayRow).ToArray();
        var modRows = report.ModHighlights.Select(BuildDisplayRow).ToArray();
        var anyPartialRank = keyRows.Concat(topRows).Concat(modRows).Any(r => r.Item.Rank is not null && r.Item.RankHasPartial);
        var chronicleSection = chronicle is not null && view is not null
            ? ColonyChronicleFormatter.BuildReportSection(view, chronicle)
            : null;

        displayCache = new ReportDisplayCache(
            report, chronicle, lang, colonyName, originalFactionLine, scopeLabel, banner, overviewFields, endingLines,
            endingFootnote, coverageLine, keyRows, topRows, modRows, chronicleSection, anyPartialRank);
        return displayCache;
    }

    private static string FormatLineageSummary(ColonyReportOverview ov)
    {
        var state = ov.LineageState switch
        {
            CheckpointLineage.Linear => (string)"RWS_Lineage_State_SingleChain".Translate(),
            CheckpointLineage.Branched => (string)"RWS_Lineage_State_Branched".Translate(),
            CheckpointLineage.Unknown => (string)"RWS_Lineage_State_Gaps".Translate(),
            _ => (string)"RWS_Lineage_State_None".Translate(),
        };
        if (ov.LineageNodeCount <= 0)
            return state;

        var details = new List<string>(4) { (string)"RWS_Lineage_SummaryNodes".Translate(ov.LineageNodeCount) };
        if (ov.LineageBranchCount > 0)
            details.Add((string)"RWS_Lineage_SummaryBranches".Translate(ov.LineageBranchCount));
        if (ov.LineageMissingParentCount > 0)
            details.Add((string)"RWS_Lineage_SummaryMissingParents".Translate(ov.LineageMissingParentCount));
        if (ov.LineageUnprovenSaveCount > 0)
            details.Add((string)"RWS_Lineage_SummaryUnproven".Translate(ov.LineageUnprovenSaveCount));
        return $"{state} ({string.Join(", ", details)})";
    }

    private static string[] BuildEndingLines(ColonyReportOverview ov)
    {
        var list = new List<string>();
        foreach (var pair in ov.CountedEndings.GroupBy(e => (e.Type, e.Ordinal)).OrderBy(g => g.Key.Type).ThenBy(g => g.Key.Ordinal))
        {
            var best = pair.OrderByDescending(e => e.Departed).ThenBy(e => e.DevMode).First();
            var time = Dialog_RimworldStatistics.Local(pair.Min(e => e.RealTimeUtc));
            var source = best.Source == EndingSource.SeededTrace
                ? (string)"RWS_Source_SeededTrace".Translate()
                : (string)"RWS_Source_Live".Translate();
            if (best.DevMode)
                source += (string)"RWS_DevModeMark".Translate();
            var departed = best.Source == EndingSource.SeededTrace
                ? Dialog_RimworldStatistics.Dash
                : best.Departed.ToString(Inv);
            list.Add((string)"RWS_Report_EndingLine".Translate(
                Dialog_RimworldStatistics.EndingLabel(pair.Key.Type),
                (pair.Key.Ordinal + 1L).ToString(Inv),
                departed,
                time,
                source));
        }

        foreach (var trace in ov.UnexplainedTraces)
        {
            list.Add((string)"RWS_Report_EndingTraceLine".Translate(
                Dialog_RimworldStatistics.EndingLabel(trace),
                (string)"RWS_AtLeastOnce".Translate(),
                (string)"RWS_Source_SaveTrace".Translate()));
        }

        return list.ToArray();
    }

    private DisplayRow BuildDisplayRow(ColonyReportMetricItem item)
    {
        var def = item.Definition;
        var val = item.Value;
        var label = item.IsBasic ? BasicPerColonyLabel(item.MetricKey) : MetricText.Label(def);
        var modSource = item.IsBasic ? null : MetricText.ModSourceLabel(def);
        var valueText = MetricText.Format(def, val);
        var kindText = KindLabel(item.ValueKind);
        var rankText = FormatRank(item);

        var stateText = item.IsBasic
            ? (val.Known ? (string)"RWS_Coverage_SnapshotOnly".Translate() : (string)"RWS_Coverage_Unknown".Translate())
            : MetricText.State(def, val.Coverage);
        if (!item.IsBasic && def.Acquisition is not null && !def.Published)
            stateText = (string)"RWS_Exp_ExperimentalRow".Translate() + " · " + stateText;

        var rangeText = FormatRange(def, val, item.IsBasic);
        var chartable = MetricLookup.IsKnownKey(view!, item.MetricKey);

        var tip = new StringBuilder(192);
        tip.Append(label);
        if (modSource is not null)
            tip.Append('\n').Append(modSource);
        tip.Append('\n').Append((string)"RWS_Details_Col_Value".Translate()).Append(": ").Append(valueText);
        tip.Append('\n').Append((string)"RWS_Report_Col_Kind".Translate()).Append(": ").Append(kindText);
        tip.Append('\n').Append((string)"RWS_Report_Col_Rank".Translate()).Append(": ").Append(rankText);
        tip.Append('\n').Append((string)"RWS_Details_Col_State".Translate()).Append(": ").Append(stateText);
        tip.Append('\n').Append((string)"RWS_Report_Col_Range".Translate()).Append(": ").Append(rangeText);
        if (!item.IsBasic)
            tip.Append(MetricText.PerDay(def, val));
        if (chartable)
            tip.Append("\n\n").Append((string)"RWS_Chart_OpenTip".Translate());

        var isMissingOrNa = !val.Known || val.Coverage == MetricCoverage.NotApplicable;
        var isPartial = val.Coverage == MetricCoverage.Partial;

        return new DisplayRow(item, label, modSource, valueText, kindText, rankText, stateText, rangeText,
            tip.ToString(), chartable, isMissingOrNa, isPartial);
    }

    private static string BasicPerColonyLabel(string key) => key switch
    {
        BasicMetrics.PeakWealth => "RWS_Col_PeakWealth".Translate(),
        BasicMetrics.LastWealth => "RWS_Col_LastWealth".Translate(),
        _ => MetricText.BasicLabel(key),
    };

    private static string KindLabel(ColonyReportValueKind kind) => kind switch
    {
        ColonyReportValueKind.CumulativeTotal => "RWS_Report_Kind_Cumulative".Translate(),
        ColonyReportValueKind.PeakOrExtreme => "RWS_Report_Kind_Peak".Translate(),
        ColonyReportValueKind.LatestSnapshot => "RWS_Report_Kind_Snapshot".Translate(),
        _ => "RWS_Report_Kind_MeanRate".Translate(),
    };

    private static string FormatRank(ColonyReportMetricItem item) => item.RankStatus switch
    {
        ColonyRankStatus.Ranked when item.Rank is { } r =>
            $"#{r.ToString(Inv)} / {item.MeasuredColonies.ToString(Inv)}" + (item.RankHasPartial ? "*" : ""),
        ColonyRankStatus.Excluded => "RWS_Report_Rank_Excluded".Translate(),
        ColonyRankStatus.OutOfScope => "RWS_Report_Rank_OutOfScope".Translate(),
        _ => Dialog_RimworldStatistics.Dash,
    };

    private static string FormatRange(MetricDefinition def, NumericValue val, bool isBasic)
    {
        if (val.ThroughTick is not { } through)
            return Dialog_RimworldStatistics.Dash;
        if (val.SinceTick is { } since)
            return (string)"RWS_Report_RangeSpan".Translate(Dialog_RimworldStatistics.Days(since), Dialog_RimworldStatistics.Days(through));
        if (!isBasic && val.Coverage == MetricCoverage.Complete && def.Provenance != MetricProvenance.SaveSnapshot)
            return (string)"RWS_Report_RangeSpan".Translate(Dialog_RimworldStatistics.Days(0), Dialog_RimworldStatistics.Days(through));
        return (string)"RWS_Report_RangePoint".Translate(Dialog_RimworldStatistics.Days(through));
    }
}
