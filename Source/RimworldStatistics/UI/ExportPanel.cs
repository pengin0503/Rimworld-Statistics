using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal static class ExportPanel
{
    /// <summary>Two control rows plus the status line.</summary>
    internal const float PreferredHeight = 84f;

    private static Task<ExportSummary>? pending;
    private static ExportSummary? completed;
    private static string? error;
    private static int filesWritten;
    private static string? directoryName;
    private static bool copied;
    private static string? copyError;

    public static void Draw(Rect rect, ProfileSession session, StatsView? view, string scope)
    {
        if (pending is { IsCompleted: true } task)
        {
            if (task.IsCompletedSuccessfully)
            {
                completed = task.Result;
                directoryName = Path.GetFileName(completed.Directory);
            }
            else
            {
                error = task.Exception?.GetBaseException().Message;
            }
            pending = null;
        }

        directoryName ??= StatisticsExport.DefaultDirectoryName(DateTime.UtcNow);

        // Row 1: export button and the editable export-folder name.
        const float rowHeight = 26f;
        var exportWidth = Math.Min(300f, Math.Max(140f, rect.width * 0.36f));
        var nameLabel = (string)"RWS_Export_Name".Translate();
        var nameLabelWidth = Math.Clamp(Text.CalcSize(nameLabel).x + 8f, 60f, 180f);
        const float fieldMinimum = 120f;
        if (rect.width < exportWidth + nameLabelWidth + 4f + fieldMinimum)
            exportWidth = Math.Max(100f, rect.width - nameLabelWidth - 4f - fieldMinimum);
        if (exportWidth < 100f)
        {
            // Too narrow for the label and the field: keep the field usable and hide the label.
            nameLabelWidth = 0f;
            exportWidth = Math.Min(200f, Math.Max(100f, rect.width * 0.4f));
        }

        string label = "RWS_Export_Button".Translate(scope);
        var exportRect = new Rect(rect.x, rect.y, exportWidth, rowHeight);
        var shown = label.Truncate(exportRect.width - 12f);
        if (shown != label)
            TooltipHandler.TipRegion(exportRect, label);
        if (Widgets.ButtonText(exportRect, shown, active: view is not null && pending is null))
        {
            var requested = StatisticsExport.SanitizeDirectoryName(directoryName,
                StatisticsExport.DefaultDirectoryName(DateTime.UtcNow));
            directoryName = requested;
            Start(session, view!, scope, requested);
        }

        var fieldX = exportRect.xMax + 8f + nameLabelWidth;
        if (nameLabelWidth > 0f)
        {
            var nameRect = new Rect(exportRect.xMax + 8f, rect.y + 4f, nameLabelWidth, 18f);
            Widgets.Label(nameRect, nameLabel.Truncate(nameRect.width));
            fieldX = nameRect.xMax + 4f;
        }
        var fieldRect = new Rect(fieldX, rect.y, Math.Max(0f, rect.xMax - fieldX), rowHeight);
        if (fieldRect.width >= 60f)
            directoryName = Widgets.TextField(fieldRect, directoryName ?? "") ?? "";

        // Row 2: instant copies of the same summary table the CSV export writes, then the export actions.
        var controls = new ControlRow(new Rect(rect.x, rect.y + rowHeight + 4f, rect.width, rowHeight));
        var markdownLabel = (string)"RWS_Export_CopyMarkdown".Translate();
        var markdownRect = controls.Next(markdownLabel, 150f, 280f);
        if (Widgets.ButtonText(markdownRect, markdownLabel.Truncate(markdownRect.width - 12f), active: view is not null))
            Copy(view!, scope, markdown: true);
        if (markdownLabel.Truncate(markdownRect.width - 12f) != markdownLabel)
            TooltipHandler.TipRegion(markdownRect, markdownLabel);

        var tsvLabel = (string)"RWS_Export_CopyTsv".Translate();
        var tsvRect = controls.Next(tsvLabel, 130f, 260f);
        if (Widgets.ButtonText(tsvRect, tsvLabel.Truncate(tsvRect.width - 12f), active: view is not null))
            Copy(view!, scope, markdown: false);
        if (tsvLabel.Truncate(tsvRect.width - 12f) != tsvLabel)
            TooltipHandler.TipRegion(tsvRect, tsvLabel);

        if (completed is { } done)
        {
            string open = "RWS_Backup_OpenFolder".Translate();
            var openRect = controls.Next(open, 84f, 150f);
            if (Widgets.ButtonText(openRect, open.Truncate(openRect.width - 12f)))
                Application.OpenURL(new Uri(Path.GetFullPath(done.Directory) + Path.DirectorySeparatorChar).AbsoluteUri);
            string copy = "RWS_Backup_CopyPath".Translate();
            var copyRect = controls.Next(copy, 84f, 150f);
            if (Widgets.ButtonText(copyRect, copy.Truncate(copyRect.width - 12f)))
                GUIUtility.systemCopyBuffer = done.Directory;
        }

        string? status = pending is not null ? "RWS_Export_Progress".Translate(Volatile.Read(ref filesWritten), StatisticsExport.TotalFileCount)
            : error is not null ? "RWS_Export_Error".Translate(error)
            : copyError is not null ? "RWS_Export_CopyFailed".Translate(copyError)
            : copied ? "RWS_Export_Copied".Translate()
            : completed is { } result ? "RWS_Export_Done".Translate(result.Directory)
            : null;
        if (status is null)
            return;
        var statusRect = new Rect(rect.x, rect.y + rowHeight + 4f + controls.Height + 4f, rect.width, 22f);
        Widgets.Label(statusRect, status.Truncate(statusRect.width));
        TooltipHandler.TipRegion(statusRect, status);
    }

    private static void Start(ProfileSession session, StatsView view, string scope, string? requestedName)
    {
        completed = null;
        error = null;
        copied = false;
        copyError = null;
        Volatile.Write(ref filesWritten, 0);
        try
        {
            var text = CreateText(view, scope, out var units);
            var directory = Path.Combine(ProfileService.DirectoryPath, "exports");
            pending = Task.Run(() =>
            {
                var histories = session.ReadExportHistories(view);
                return StatisticsExport.Write(view, text, id => units.GetValueOrDefault(id, UnitRecordingState.NotRequired),
                    directory, default, n => Volatile.Write(ref filesWritten, n), histories, null, requestedName);
            });
        }
        catch (Exception failure)
        {
            error = failure.Message;
        }
    }

    /// <summary>Builds the clipboard text from the E8 table builder, so copied rows are the exported rows.</summary>
    private static void Copy(StatsView view, string scope, bool markdown)
    {
        copied = false;
        copyError = null;
        try
        {
            var text = CreateText(view, scope, out var units);
            var table = StatisticsExport.BuildSummaryTable(view, text,
                id => units.GetValueOrDefault(id, UnitRecordingState.NotRequired));
            GUIUtility.systemCopyBuffer = markdown ? StatisticsExport.RenderMarkdown(table) : StatisticsExport.RenderTsv(table);
            copied = true;
        }
        catch (Exception failure)
        {
            copyError = failure.Message;
        }
    }

    /// <summary>The localized labels and unit states shared by the export task and the copy actions.</summary>
    private static ExportText CreateText(StatsView view, string scope, out Dictionary<string, UnitRecordingState> units)
    {
        var definitions = view.Numeric.Overall.Select(row => row.Definition)
            .Concat(view.Numeric.Colonies.SelectMany(colony => colony.Rows.Select(row => row.Definition)))
            .DistinctBy(d => d.Key, StringComparer.Ordinal).ToArray();
        var labels = definitions.ToDictionary(d => d.Key, MetricText.Label, StringComparer.Ordinal);
        foreach (var key in BasicMetrics.Keys)
            labels.TryAdd(key, MetricText.BasicLabel(key));
        var names = view.Stats.Colonies.ToDictionary(c => c.Key, Dialog_RimworldStatistics.ColonyName, StringComparer.Ordinal);
        var values = new Dictionary<NumericRow, string>();
        string ColonyName(string key) => names.GetValueOrDefault(key) ?? key;
        foreach (var row in view.Numeric.Overall)
            Capture(row);
        units = definitions.Select(d => d.Acquisition).OfType<string>().Distinct(StringComparer.Ordinal)
            .ToDictionary(id => id, UnitRecordingStates.Get, StringComparer.Ordinal);
        return new ExportText(labels, names, values, LanguageDatabase.activeLanguage?.folderName ?? "",
            scope, RimworldStatisticsMod.InstalledVersion);

        void Capture(NumericRow row)
        {
            if (row.Value.Known)
                values[row] = DetailsTab.FormatRowValue(row, ColonyName);
            foreach (var child in row.Breakdowns)
                Capture(child);
        }
    }
}
