using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Import status (spec §9.2-4): last scan, counts, failures, rescan/progress/cancel, record file and backups.</summary>
public static class ImportTab
{
    private static Vector2 outerScroll;
    private static float lastContentHeight;
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
        if (rect.height <= 0f || rect.width <= 0f)
            return;
        var (lastScan, problems) = (snapshot.LastScan, snapshot.Problems);
        var run = ImportRunner.State;
        var needsOuterScroll = lastContentHeight > rect.height + 0.5f;
        var innerWidth = Math.Max(1f, rect.width - (needsOuterScroll ? 16f : 0f));
        var outerView = new Rect(0f, 0f, innerWidth, Math.Max(rect.height, lastContentHeight));
        Widgets.BeginScrollView(rect, ref outerScroll, outerView);
        try
        {
            var localRect = new Rect(0f, 0f, innerWidth, outerView.height);
            var y = 0f;

            Line(localRect, ref y, "RWS_Import_LastScan".Translate(lastScan is null ? (string)"RWS_Import_Never".Translate() : Dialog_RimworldStatistics.Local(lastScan)));
            switch (run.Phase)
            {
                case ImportRunPhase.NotRun:
                    Line(localRect, ref y, "RWS_Import_NotRunThisSession".Translate());
                    break;
                case ImportRunPhase.Running:
                    Line(localRect, ref y, run.Mode == ImportMode.ForcedRecheck
                        ? "RWS_Import_RecheckRunningNow".Translate() : "RWS_Import_RunningNow".Translate());
                    break;
                case ImportRunPhase.Completed or ImportRunPhase.Cancelled when run.Result is { } result:
                    var cancelledMark = run.Phase == ImportRunPhase.Cancelled ? (string)"RWS_Import_CancelledMark".Translate() : "";
                    if (result.Mode == ImportMode.ForcedRecheck)
                    {
                        Line(localRect, ref y, (string)"RWS_Import_RecheckCounts".Translate(result.Read, result.Unchanged, result.Replaced,
                            result.Added, result.Failed, result.Skipped) + cancelledMark);
                        if (result.KeptAfterFailure > 0)
                            Line(localRect, ref y, "RWS_Import_RecheckKept".Translate(result.KeptAfterFailure));
                    }
                    else
                    {
                        Line(localRect, ref y, (string)"RWS_Import_Counts".Translate(result.Read, result.Skipped, result.Failed) + cancelledMark);
                    }
                    break;
                default:
                    GUI.color = ColorLibrary.RedReadable;
                    Line(localRect, ref y, "RWS_Import_Error".Translate(run.Error is { } error ? error.GetType().Name + ": " + error.Message : ""));
                    GUI.color = Color.white;
                    break;
            }

            y += 6f;
            if (ImportRunner.Running)
            {
                var p = run.Progress;
                var fraction = p is { Total: > 0 } ? p.Done / (float)p.Total : 0f;
                var cancelLabel = (string)"RWS_Cancel".Translate();
                var cancelWidth = Math.Min(Math.Max(120f, Text.CalcSize(cancelLabel).x + 24f), Math.Max(80f, localRect.width * 0.35f));
                var bar = new Rect(localRect.x, y, Math.Max(0f, localRect.width - cancelWidth - 10f), 32f);
                Widgets.FillableBar(bar, fraction);
                var prevAnchor = Text.Anchor;
                try
                {
                    Text.Anchor = TextAnchor.MiddleCenter;
                    var full = p is null ? "…" : (string)"RWS_Import_Progress".Translate(p.Done, p.Total, p.Failed, p.CurrentFile ?? "");
                    var shown = full.Truncate(Math.Max(0f, bar.width - 8f));
                    Widgets.Label(bar, shown);
                    if (shown != full)
                        TooltipHandler.TipRegion(bar, full);
                }
                finally
                {
                    Text.Anchor = prevAnchor;
                }
                var cancelRect = new Rect(localRect.xMax - cancelWidth, y, cancelWidth, 32f);
                var shownCancel = cancelLabel.Truncate(Math.Max(0f, cancelRect.width - 12f));
                if (shownCancel != cancelLabel)
                    TooltipHandler.TipRegion(cancelRect, cancelLabel);
                if (Widgets.ButtonText(cancelRect, shownCancel))
                    ImportRunner.Cancel();
                y += 40f;
            }
            else
            {
                var actions = new ControlRow(new Rect(localRect.x, y, localRect.width, 32f));
                bool ActionButton(string label, float min, float max, string? explicitTip = null)
                {
                    var btn = actions.Next(label, min, max);
                    var shown = label.Truncate(Math.Max(0f, btn.width - 12f));
                    if (explicitTip is not null)
                        TooltipHandler.TipRegion(btn, shown != label ? label + "\n" + explicitTip : explicitTip);
                    else if (shown != label)
                        TooltipHandler.TipRegion(btn, label);
                    return Widgets.ButtonText(btn, shown);
                }

                if (ActionButton("RWS_Rescan".Translate(), 120f, 240f))
                    ImportRunner.StartIfIdle();
                if (ActionButton("RWS_ForceRecheck".Translate(), 140f, 280f, "RWS_ForceRecheckTip".Translate()))
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("RWS_ForceRecheck_Confirm".Translate(),
                        ImportRunner.StartForcedRecheckIfIdle));
                if (ActionButton("RWS_HistoricalIntervals_Title".Translate(), 150f, 320f))
                    Find.WindowStack.Add(new Dialog_HistoricalCounterIntervals(session));

                y += actions.Height + 8f;
            }

            var load = session.LoadResult;
            Line(localRect, ref y, "RWS_Import_ProfilePath".Translate(Path.Combine(ProfileService.DirectoryPath, "profile.json")));
            var loadText = (string)"RWS_Import_LoadSource".Translate(Dialog_RimworldStatistics.LoadSourceLabel(load.Source));
            if (load.CorruptFileRenamedTo != null)
                loadText += " " + "RWS_Import_CorruptRenamed".Translate(load.CorruptFileRenamedTo);
            if (load.ReadOnly)
                loadText += " " + "RWS_Import_ReadOnly".Translate();
            Line(localRect, ref y, loadText);
            Line(localRect, ref y, "RWS_Import_Backups".Translate(BackupCount()));
            if (session.LastSaveError is { } saveError && !load.ReadOnly)
            {
                GUI.color = ColorLibrary.RedReadable;
                Line(localRect, ref y, "RWS_Import_SaveError".Translate(saveError.GetType().Name + ": " + saveError.Message));
                GUI.color = Color.white;
            }

            y += 6f;
            Text.Font = GameFont.Small;
            if (snapshot.ProblemSummary is { } summary)
                Line(localRect, ref y, summary);
            var title = (string)"RWS_Import_Problems".Translate(problems.Length);
            var minProblemsHeight = problems.Length == 0 ? 52f : Math.Min(168f, (problems.Length + 1) * 24f + 4f);
            var problemsHeight = Math.Max(minProblemsHeight, rect.height - y);
            ProblemsTable.Draw(new Rect(localRect.x, y, localRect.width, problemsHeight), ref scroll, [title], problems);
            lastContentHeight = y + problemsHeight;
        }
        finally
        {
            GUI.color = Color.white;
            Widgets.EndScrollView();
        }
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
