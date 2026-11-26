using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Import status (spec §9.2-4): last scan, counts, failures, rescan/progress/cancel, record file and backups.</summary>
public static class ImportTab
{
    private static Vector2 scroll;
    private static DateTime backupsCheckedUtc;
    private static int backupCount;
    private static DateTime presentCheckedUtc;
    private static string[]? presentFiles;

    private sealed record Snapshot(ProfileSession Session, long Revision, object? Language, IReadOnlyList<string> PresentFor,
        DateTime? LastScan, string[][] Problems, string? ProblemSummary, string[] FailedFiles, SavePresenceSummary Presence);
    private static Snapshot? cached;
    private static readonly StatsTable ProblemsTable = new(24f, new ColumnSpec(0f, 1f));

    public static void Draw(Rect rect, ProfileSession session, StatsView? view = null)
    {
        var language = LanguageDatabase.activeLanguage;
        var present = PresentFilesForThisFrame();
        if (!session.TryReadWithRevision((profile, revision) =>
        {
            if (cached is { } previous && ReferenceEquals(previous.Session, session) && previous.Revision == revision
                && ReferenceEquals(previous.Language, language) && ReferenceEquals(previous.PresentFor, present))
                return previous;

            // Grouped by what the player can do about it; the reader's own reason stays visible after the group.
            var failed = profile.SaveObservations.Where(o => !o.Superseded && o.Status != ObservationStatus.Ok)
                .OrderBy(o => o.FileName, StringComparer.Ordinal)
                .Select(o => (Observation: o, Kind: ImportProblems.Classify(o) ?? ImportProblemKind.Unexpected))
                .ToArray();
            var problems = failed.Select(p => new[] { $"{p.Observation.FileName}  —  {ProblemLabel(p.Kind)}: {p.Observation.ErrorReason ?? ""}" })
                .ToArray();
            var failedFiles = failed.Select(p => p.Observation.FileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            string? summary = null;
            if (failed.Length > 0)
            {
                summary = (string)"RWS_Import_ProblemSummary".Translate(string.Join(" / ", failed.GroupBy(p => p.Kind)
                    .OrderBy(g => g.Key).Select(g => ProblemLabel(g.Key) + ": " + g.Count().ToString(CultureInfo.InvariantCulture))));
                if (failed.Any(p => ImportProblems.RetriedOnRescan(p.Kind)))
                    summary += " " + (string)"RWS_Import_RetriedOnRescan".Translate();
            }
            return new Snapshot(session, revision, language, present, profile.ImportState.LastScanUtc, problems, summary,
                failedFiles, SavePresence.Classify(profile, present));
        }, out var snapshot))
        {
            if (cached is not { } previous || !ReferenceEquals(previous.Session, session) || !ReferenceEquals(previous.Language, language))
            {
                Verse.Widgets.Label(rect, "RWS_Loading".Translate());
                return;
            }
            snapshot = previous;
        }
        cached = snapshot;
        var (lastScan, problems) = (snapshot.LastScan, snapshot.Problems);
        var run = ImportRunner.State;
        var y = rect.y;

        Line(rect, ref y, "RWS_Import_LastScan".Translate(lastScan is null ? (string)"RWS_Import_Never".Translate() : Dialog_RimworldStatistics.Local(lastScan)));
        var presence = snapshot.Presence;
        Line(rect, ref y, "RWS_Import_RecordedSaves".Translate(presence.Total, presence.Present, presence.Archived),
            presence.MissingFiles.Count > 0 ? string.Join(", ", presence.MissingFiles) : null);
        // Only the latest scan's own outcome (#337): a scan that runs or failed shows no counts of an earlier one.
        switch (run.Phase)
        {
            case ImportRunPhase.NotRun:
                Line(rect, ref y, "RWS_Import_NotRunThisSession".Translate());
                break;
            case ImportRunPhase.Running:
                Line(rect, ref y, run.Mode == ImportMode.ForcedRecheck
                    ? "RWS_Import_RecheckRunningNow".Translate() : "RWS_Import_RunningNow".Translate());
                break;
            case ImportRunPhase.Completed or ImportRunPhase.Cancelled when run.Result is { } result:
                var cancelledMark = run.Phase == ImportRunPhase.Cancelled ? (string)"RWS_Import_CancelledMark".Translate() : "";
                if (result.Mode == ImportMode.ForcedRecheck)
                {
                    Line(rect, ref y, (string)"RWS_Import_RecheckCounts".Translate(result.Read, result.Unchanged, result.Replaced,
                        result.Added, result.Failed, result.Skipped) + cancelledMark);
                    if (result.KeptAfterFailure > 0)
                        Line(rect, ref y, "RWS_Import_RecheckKept".Translate(result.KeptAfterFailure));
                }
                else
                {
                    Line(rect, ref y, (string)"RWS_Import_Counts".Translate(result.Read, result.Skipped, result.Failed) + cancelledMark);
                }
                break;
            default:
                using (UiScopes.GuiState())
                {
                    GUI.color = ColorLibrary.RedReadable;
                    Line(rect, ref y, "RWS_Import_Error".Translate(run.Error is { } error ? error.GetType().Name + ": " + error.Message : ""));
                }
                break;
        }

        // S12: what this run newly recorded or replaced, under the counts. The full list is the tooltip when the line is
        // too narrow; "+N more" counts names hidden by the line (not the result's 100-name cap).
        if (run.Result is { } last && run.Phase is ImportRunPhase.Completed or ImportRunPhase.Cancelled)
        {
            if (last.Excluded > 0)
                Line(rect, ref y, "RWS_Import_Excluded".Translate(last.Excluded),
                    last.ExcludedFiles.Count > 0 ? string.Join(", ", last.ExcludedFiles) : null);
            NameSummary(rect, ref y, "RWS_Import_AddedNames", last.AddedFiles);
            NameSummary(rect, ref y, "RWS_Import_ReplacedNames", last.ReplacedFiles);
        }

        y += 6f;
        var canScanOnTitleScreen = Current.ProgramState == ProgramState.Entry;
        if (ImportRunner.Running)
        {
            var p = run.Progress;
            var fraction = p is { Total: > 0 } ? p.Done / (float)p.Total : 0f;
            var cancelWidth = Math.Min(180f, Math.Max(80f, rect.width * 0.32f));
            var bar = new Rect(rect.x, y, Math.Max(0f, rect.width - cancelWidth - 12f), 32f);
            Verse.Widgets.FillableBar(bar, fraction);
            using (UiScopes.GuiState())
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                var full = p is null ? "…" : (string)"RWS_Import_Progress".Translate(p.Done, p.Total, p.Failed, p.CurrentFile ?? "");
                var shown = full.Truncate(Math.Max(0f, bar.width - 8f));
                Verse.Widgets.Label(bar, shown);
                if (shown != full)
                    TooltipHandler.TipRegion(bar, full);
            }
            var cancelLabel = (string)"RWS_Cancel".Translate();
            var cancelRect = new Rect(rect.xMax - cancelWidth, y, cancelWidth, 32f);
            if (Verse.Widgets.ButtonText(cancelRect, cancelLabel.Truncate(Math.Max(0f, cancelRect.width - 12f))))
                ImportRunner.Cancel();
            y += 40f;
        }
        else
        {
            var controls = new ControlRow(new Rect(rect.x, y, rect.width, 32f));
            var rescanLabel = (string)"RWS_Rescan".Translate();
            var rescanRect = controls.Next(rescanLabel, 110f, 200f);
            var shownRescan = rescanLabel.Truncate(Math.Max(0f, rescanRect.width - 12f));
            if (Verse.Widgets.ButtonText(rescanRect, shownRescan, active: canScanOnTitleScreen) && canScanOnTitleScreen)
                ImportRunner.StartIfIdle();
            if (!canScanOnTitleScreen)
                TooltipHandler.TipRegion(rescanRect, "RWS_Import_TitleScreenOnlyTip".Translate());
            else if (shownRescan != rescanLabel)
                TooltipHandler.TipRegion(rescanRect, rescanLabel);

            // A forced re-check reads every save in full, so it only runs after the player confirms it (#310).
            var recheckLabel = (string)"RWS_ForceRecheck".Translate();
            var recheckRect = controls.Next(recheckLabel, 130f, 240f);
            if (Verse.Widgets.ButtonText(recheckRect, recheckLabel.Truncate(Math.Max(0f, recheckRect.width - 12f)), active: canScanOnTitleScreen)
                && canScanOnTitleScreen)
            {
                ConfirmDialogs.Confirm("RWS_Confirm_Title", "RWS_ForceRecheck_Confirm".Translate(),
                    ImportRunner.StartForcedRecheckIfIdle);
            }
            TooltipHandler.TipRegion(recheckRect,
                canScanOnTitleScreen ? "RWS_ForceRecheckTip".Translate() : "RWS_Import_TitleScreenOnlyTip".Translate());

            var historicalLabel = (string)"RWS_HistoricalIntervals_Title".Translate();
            var historicalRect = controls.Next(historicalLabel, 140f, 280f);
            var shownHistorical = historicalLabel.Truncate(Math.Max(0f, historicalRect.width - 12f));
            if (Verse.Widgets.ButtonText(historicalRect, shownHistorical))
                UiScopes.OpenOrReplaceWindow(new Dialog_HistoricalCounterIntervals(session, view));
            if (shownHistorical != historicalLabel)
                TooltipHandler.TipRegion(historicalRect, historicalLabel);

            y += controls.Height + 8f;
        }

        // Record file, load source, backups, write errors.
        var load = session.LoadResult;
        Line(rect, ref y, "RWS_Import_ProfilePath".Translate(Path.Combine(ProfileService.DirectoryPath, "profile.json")));
        var loadText = (string)"RWS_Import_LoadSource".Translate(Dialog_RimworldStatistics.LoadSourceLabel(load.Source));
        if (load.CorruptFileRenamedTo != null)
            loadText += " " + "RWS_Import_CorruptRenamed".Translate(load.CorruptFileRenamedTo);
        if (load.ReadOnly)
            loadText += " " + "RWS_Import_ReadOnly".Translate();
        Line(rect, ref y, loadText);
        Line(rect, ref y, "RWS_Import_Backups".Translate(BackupCount()));
        if (session.LastSaveError is { } saveError && !load.ReadOnly)
        {
            using (UiScopes.GuiState())
            {
                GUI.color = ColorLibrary.RedReadable;
                Line(rect, ref y, "RWS_Import_SaveError".Translate(saveError.GetType().Name + ": " + saveError.Message));
            }
        }

        y += 8f;
        using (UiScopes.GuiState())
        {
            Text.Font = GameFont.Small;
            if (snapshot.ProblemSummary is { } summary)
                Line(rect, ref y, summary);
        }
        var actionsHeight = canScanOnTitleScreen && !ImportRunner.Running ? 40f : 0f;
        var title = (string)"RWS_Import_Problems".Translate(problems.Length);
        ProblemsTable.Draw(new Rect(rect.x, y, rect.width, Math.Max(0f, rect.yMax - y - actionsHeight)), ref scroll,
            [title], problems);
        if (actionsHeight > 0f)
            DrawFailedSaveActions(new Rect(rect.x, rect.yMax - 32f, rect.width, 32f), snapshot.FailedFiles);
    }

    /// <summary>S9: open/copy the saves folder and per-failed-save re-check/ignore actions.</summary>
    private static void DrawFailedSaveActions(Rect rect, string[] failedFiles)
    {
        var actions = new ControlRow(rect);
        var openLabel = (string)"RWS_Backup_OpenFolder".Translate();
        var openRect = actions.Next(openLabel, 90f, 190f);
        var shownOpen = openLabel.Truncate(Math.Max(0f, openRect.width - 12f));
        if (Verse.Widgets.ButtonText(openRect, shownOpen))
            Application.OpenURL(new Uri(Path.GetFullPath(GenFilePaths.SavedGamesFolderPath) + Path.DirectorySeparatorChar).AbsoluteUri);
        if (shownOpen != openLabel)
            TooltipHandler.TipRegion(openRect, openLabel);

        var copyLabel = (string)"RWS_Backup_CopyPath".Translate();
        var copyRect = actions.Next(copyLabel, 90f, 190f);
        var shownCopy = copyLabel.Truncate(Math.Max(0f, copyRect.width - 12f));
        if (Verse.Widgets.ButtonText(copyRect, shownCopy))
            GUIUtility.systemCopyBuffer = GenFilePaths.SavedGamesFolderPath;
        if (shownCopy != copyLabel)
            TooltipHandler.TipRegion(copyRect, copyLabel);

        var menuLabel = (string)"RWS_Import_FailedActions".Translate();
        var menuRect = actions.Next(menuLabel, 150f, 280f);
        var shownMenu = menuLabel.Truncate(Math.Max(0f, menuRect.width - 12f));
        if (Verse.Widgets.ButtonText(menuRect, shownMenu, active: failedFiles.Length > 0))
            Find.WindowStack.Add(new FloatMenu(FailedSaveOptions(failedFiles)));
        if (shownMenu != menuLabel)
            TooltipHandler.TipRegion(menuRect, menuLabel);
    }

    private static List<FloatMenuOption> FailedSaveOptions(string[] failedFiles)
    {
        var recheck = (string)"RWS_Import_RecheckThisSave".Translate();
        var ignore = (string)"RWS_Import_IgnoreThisSave".Translate();
        var options = new List<FloatMenuOption>(failedFiles.Length * 2);
        foreach (var file in failedFiles)
        {
            options.Add(new FloatMenuOption(file + " — " + recheck, () => ImportRunner.StartSingleSave(file)));
            options.Add(new FloatMenuOption(file + " — " + ignore, () => IgnoreSave(file)));
        }
        return options;
    }

    private static void IgnoreSave(string fileName)
    {
        var settings = RimworldStatisticsMod.Settings;
        settings.AddImportExclusion(fileName);
        RimworldStatisticsMod.Instance?.WriteSettings();
    }

    /// <summary>One "label: names" line, showing "+N more" when the line is too narrow; the full text is the tooltip.</summary>
    private static void NameSummary(Rect rect, ref float y, string labelKey, IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return;
        var prefix = (string)labelKey.Translate("");
        var full = prefix + string.Join(", ", names);
        if (Text.CalcSize(full).x <= rect.width)
        {
            Line(rect, ref y, full);
            return;
        }
        string? shown = null;
        for (var count = names.Count - 1; count >= 0; count--)
        {
            var more = (string)"RWS_Import_MoreNames".Translate(names.Count - count);
            var visible = count == 0 ? "" : string.Join(", ", names.Take(count)) + " ";
            var candidate = prefix + visible + more;
            if (Text.CalcSize(candidate).x <= rect.width)
            {
                shown = candidate;
                break;
            }
        }
        Line(rect, ref y, (shown ?? full).Truncate(Math.Max(0f, rect.width)), full);
    }

    /// <summary>Save file names present on disk, cached for two seconds; the latest scan's snapshot when one exists.</summary>
    private static IReadOnlyList<string> PresentFilesForThisFrame()
    {
        if (ImportRunner.LastResult is { } result)
            return result.PresentFiles;
        var now = DateTime.UtcNow;
        if (presentFiles is null || (now - presentCheckedUtc).TotalSeconds >= 2)
        {
            presentCheckedUtc = now;
            try
            {
                presentFiles = Directory.EnumerateFiles(GenFilePaths.SavedGamesFolderPath)
                    .Select(path => Path.GetFileName(path))
                    .Where(name => !string.IsNullOrEmpty(name) && ImportEngine.IsSaveFile(name))
                    .Select(name => name!)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                presentFiles = Array.Empty<string>();
            }
        }
        return presentFiles;
    }

    private static string ProblemLabel(ImportProblemKind kind) => kind switch
    {
        ImportProblemKind.ChangedDuringRead => "RWS_ImportProblem_ChangedDuringRead".Translate(),
        ImportProblemKind.Unreadable => "RWS_ImportProblem_Unreadable".Translate(),
        ImportProblemKind.Damaged => "RWS_ImportProblem_Damaged".Translate(),
        ImportProblemKind.NotASave => "RWS_ImportProblem_NotASave".Translate(),
        ImportProblemKind.MissingIdentity => "RWS_ImportProblem_MissingIdentity".Translate(),
        _ => "RWS_ImportProblem_Unexpected".Translate(),
    };

    private static readonly Dictionary<string, float> LineHeights = new(StringComparer.Ordinal);
    private static float lineWidth;
    private static object? lineLanguage;

    private static void Line(Rect rect, ref float y, string text, string? tooltip = null)
    {
        var language = LanguageDatabase.activeLanguage;
        if (lineWidth != rect.width || !ReferenceEquals(lineLanguage, language) || LineHeights.Count >= 32)
        {
            LineHeights.Clear();
            lineWidth = rect.width;
            lineLanguage = language;
        }
        if (!LineHeights.TryGetValue(text, out var h))
        {
            h = Math.Max(24f, Text.CalcHeight(text, rect.width));
            LineHeights[text] = h;
        }
        var lineRect = new Rect(rect.x, y, rect.width, h);
        Widgets.Label(lineRect, text);
        if (tooltip is not null)
            TooltipHandler.TipRegion(lineRect, tooltip);
        y += h + 2f;
    }

    /// <summary>profile.json.bak1–3 present, re-checked at most every two seconds.</summary>
    private static int BackupCount()
    {
        var now = DateTime.UtcNow;
        if ((now - backupsCheckedUtc).TotalSeconds >= 2)
        {
            backupsCheckedUtc = now;
            var path = Path.Combine(ProfileService.DirectoryPath, "profile.json");
            backupCount = Enumerable.Range(1, 3).Count(n => File.Exists(path + ".bak" + n.ToString(CultureInfo.InvariantCulture)));
        }
        return backupCount;
    }
}
