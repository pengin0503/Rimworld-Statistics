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

    private sealed record Problem(string File, ObservationStatus Status, string? Reason);

    public static void Draw(Rect rect, ProfileSession session)
    {
        if (!session.TryRead(p => (
            p.ImportState.LastScanUtc,
            p.SaveObservations.Where(o => !o.Superseded && o.Status != ObservationStatus.Ok)
                .OrderBy(o => o.FileName, StringComparer.Ordinal)
                .Select(o => new Problem(o.FileName, o.Status, o.ErrorReason)).ToList()), out var snapshot))
        {
            Widgets.Label(rect, "RWS_Loading".Translate());
            return;
        }
        var (lastScan, problems) = snapshot;
        var result = ImportRunner.LastResult;
        var y = rect.y;

        Line(rect, ref y, "RWS_Import_LastScan".Translate(lastScan is null ? (string)"RWS_Import_Never".Translate() : Dialog_RimworldStatistics.Local(lastScan)));
        Line(rect, ref y, result is null
            ? (string)"RWS_Import_NotRunThisSession".Translate()
            : (string)"RWS_Import_Counts".Translate(result.Read, result.Skipped, result.Failed) + (result.Cancelled ? (string)"RWS_Import_CancelledMark".Translate() : ""));
        if (ImportRunner.LastError is { } error)
        {
            GUI.color = ColorLibrary.RedReadable;
            Line(rect, ref y, "RWS_Import_Error".Translate(error.GetType().Name + ": " + error.Message));
            GUI.color = Color.white;
        }

        y += 6f;
        var buttonRect = new Rect(rect.x, y, 180f, 32f);
        if (ImportRunner.Running)
        {
            var p = ImportRunner.Progress;
            var fraction = p is { Total: > 0 } ? p.Done / (float)p.Total : 0f;
            Widgets.FillableBar(new Rect(rect.x, y, rect.width - 200f, 32f), fraction);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(new Rect(rect.x, y, rect.width - 200f, 32f),
                p is null ? "…" : (string)"RWS_Import_Progress".Translate(p.Done, p.Total, p.Failed, p.CurrentFile ?? ""));
            Text.Anchor = TextAnchor.UpperLeft;
            if (Widgets.ButtonText(new Rect(rect.xMax - 180f, y, 180f, 32f), "RWS_Cancel".Translate()))
                ImportRunner.Cancel();
        }
        else if (Widgets.ButtonText(buttonRect, "RWS_Rescan".Translate()))
        {
            ImportRunner.StartIfIdle();
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
        Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "RWS_Import_Problems".Translate(problems.Count));
        y += 26f;
        var outRect = new Rect(rect.x, y, rect.width, rect.yMax - y);
        var lineHeight = 24f;
        var viewRect = new Rect(0f, 0f, outRect.width - 16f, Math.Max(problems.Count * lineHeight, 1f));
        Widgets.BeginScrollView(outRect, ref scroll, viewRect);
        for (var i = 0; i < problems.Count; i++)
        {
            var pr = problems[i];
            var row = new Rect(0f, i * lineHeight, viewRect.width, lineHeight);
            if (i % 2 == 1)
                Widgets.DrawHighlight(row, 0.25f);
            var status = pr.Status == ObservationStatus.Error ? (string)"RWS_Status_Error".Translate() : (string)"RWS_Status_Unidentifiable".Translate();
            var text = $"{pr.File}  —  {status}: {pr.Reason ?? ""}";
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x + 4f, row.y, row.width - 8f, row.height), text);
            Text.Anchor = TextAnchor.UpperLeft;
            TooltipHandler.TipRegion(row, text);
        }
        Widgets.EndScrollView();
    }

    private static void Line(Rect rect, ref float y, string text)
    {
        var h = Math.Max(24f, Text.CalcHeight(text, rect.width));
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
