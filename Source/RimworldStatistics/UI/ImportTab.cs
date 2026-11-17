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

    private sealed record Snapshot(ProfileSession Session, long Revision, object? Language, DateTime? LastScan, string[][] Problems,
        string? ProblemSummary);
    private static Snapshot? cached;
    private static readonly StatsTable ProblemsTable = new(24f, new ColumnSpec(0f, 1f));

    public static void Draw(Rect rect, ProfileSession session)
    {
        var language = LanguageDatabase.activeLanguage;
        if (!session.TryReadWithRevision((profile, revision) =>
        {
            if (cached is { } previous && ReferenceEquals(previous.Session, session) && previous.Revision == revision
                && ReferenceEquals(previous.Language, language))
                return previous;

            // Grouped by what the player can do about it; the reader's own reason stays visible after the group.
            var failed = profile.SaveObservations.Where(o => !o.Superseded && o.Status != ObservationStatus.Ok)
                .OrderBy(o => o.FileName, StringComparer.Ordinal)
                .Select(o => (Observation: o, Kind: ImportProblems.Classify(o) ?? ImportProblemKind.Unexpected))
                .ToArray();
            var problems = failed.Select(p => new[] { $"{p.Observation.FileName}  —  {ProblemLabel(p.Kind)}: {p.Observation.ErrorReason ?? ""}" })
                .ToArray();
            string? summary = null;
            if (failed.Length > 0)
            {
                summary = (string)"RWS_Import_ProblemSummary".Translate(string.Join(" / ", failed.GroupBy(p => p.Kind)
                    .OrderBy(g => g.Key).Select(g => ProblemLabel(g.Key) + ": " + g.Count().ToString(CultureInfo.InvariantCulture))));
                if (failed.Any(p => ImportProblems.RetriedOnRescan(p.Kind)))
                    summary += " " + (string)"RWS_Import_RetriedOnRescan".Translate();
            }
            return new Snapshot(session, revision, language, profile.ImportState.LastScanUtc, problems, summary);
        }, out var snapshot))
        {
            if (cached is not { } previous || !ReferenceEquals(previous.Session, session) || !ReferenceEquals(previous.Language, language))
            {
                Widgets.Label(rect, "RWS_Loading".Translate());
                return;
            }
            snapshot = previous;
        }
        cached = snapshot;
        var (lastScan, problems) = (snapshot.LastScan, snapshot.Problems);
        var run = ImportRunner.State;
        var y = rect.y;

        Line(rect, ref y, "RWS_Import_LastScan".Translate(lastScan is null ? (string)"RWS_Import_Never".Translate() : Dialog_RimworldStatistics.Local(lastScan)));
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
                GUI.color = ColorLibrary.RedReadable;
                Line(rect, ref y, "RWS_Import_Error".Translate(run.Error is { } error ? error.GetType().Name + ": " + error.Message : ""));
                GUI.color = Color.white;
                break;
        }

        y += 6f;
        var buttonRect = new Rect(rect.x, y, 180f, 32f);
        if (ImportRunner.Running)
        {
            var p = run.Progress;
            var fraction = p is { Total: > 0 } ? p.Done / (float)p.Total : 0f;
            var bar = new Rect(rect.x, y, Math.Max(0, rect.width - 200f), 32f);
            Widgets.FillableBar(bar, fraction);
            Text.Anchor = TextAnchor.MiddleCenter;
            var full = p is null ? "…" : (string)"RWS_Import_Progress".Translate(p.Done, p.Total, p.Failed, p.CurrentFile ?? "");
            var shown = full.Truncate(Math.Max(0, bar.width - 8f));
            Widgets.Label(bar, shown);
            if (shown != full)
                TooltipHandler.TipRegion(bar, full);
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(rect.xMax - 180f, y, 180f, 32f), "RWS_Cancel".Translate()))
                ImportRunner.Cancel();
        }
        else if (Widgets.ButtonText(buttonRect, "RWS_Rescan".Translate()))
        {
            ImportRunner.StartIfIdle();
        }
        var nextX = buttonRect.xMax + 8f;
        if (!ImportRunner.Running && rect.xMax - nextX > 45f)
        {
            // A forced re-check reads every save in full, so it only runs after the player confirms it (#310).
            var recheckLabel = (string)"RWS_ForceRecheck".Translate();
            var recheckRect = new Rect(nextX, y, Math.Min(Math.Max(160f, Text.CalcSize(recheckLabel).x + 24f), rect.xMax - nextX),
                buttonRect.height);
            if (Widgets.ButtonText(recheckRect, recheckLabel.Truncate(recheckRect.width - 12f)))
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RWS_ForceRecheck_Confirm".Translate(),
                    ImportRunner.StartForcedRecheckIfIdle));
            TooltipHandler.TipRegion(recheckRect, "RWS_ForceRecheckTip".Translate());
            nextX = recheckRect.xMax + 8f;
        }
        if (!ImportRunner.Running && rect.xMax - nextX > 45f)
        {
            var historicalLabel = (string)"RWS_HistoricalIntervals_Title".Translate();
            var historicalRect = new Rect(nextX, y,
                Math.Min(Math.Max(190f, Text.CalcSize(historicalLabel).x + 24f), rect.xMax - nextX), buttonRect.height);
            if (Widgets.ButtonText(historicalRect, historicalLabel.Truncate(historicalRect.width - 12f)))
                Find.WindowStack.Add(new Dialog_HistoricalCounterIntervals(session));
        }
        y += 44f;

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
            GUI.color = ColorLibrary.RedReadable;
            Line(rect, ref y, "RWS_Import_SaveError".Translate(saveError.GetType().Name + ": " + saveError.Message));
            GUI.color = Color.white;
        }

        y += 8f;
        Text.Font = GameFont.Small;
        if (snapshot.ProblemSummary is { } summary)
            Line(rect, ref y, summary);
        var title = (string)"RWS_Import_Problems".Translate(problems.Length);
        ProblemsTable.Draw(new Rect(rect.x, y, rect.width, rect.yMax - y), ref scroll, [title], problems);
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

    private static void Line(Rect rect, ref float y, string text)
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
        Widgets.Label(new Rect(rect.x, y, rect.width, h), text);
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
