using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal static class ExportPanel
{
    private static Task<ExportSummary>? pending;
    private static ExportSummary? completed;
    private static string? error;
    private static int filesWritten;

    public static void Draw(Rect rect, StatsView? view, string scope)
    {
        if (pending is { IsCompleted: true } task)
        {
            if (task.IsCompletedSuccessfully)
                completed = task.Result;
            else
                error = task.Exception?.GetBaseException().Message;
            pending = null;
        }

        var controls = new ControlRow(new Rect(rect.x, rect.y, rect.width, 26f));
        string label = "RWS_Export_Button".Translate(scope);
        var button = controls.Next(label, 140f, 300f);
        var shown = label.Truncate(button.width - 12f);
        if (shown != label)
            TooltipHandler.TipRegion(button, label);
        if (Widgets.ButtonText(button, shown, active: view is not null && pending is null))
            Start(view!, scope);
        if (completed is { } done)
        {
            string open = "RWS_Backup_OpenFolder".Translate();
            if (Widgets.ButtonText(controls.Next(open, 84f, 150f), open))
                Application.OpenURL(new Uri(Path.GetFullPath(done.Directory) + Path.DirectorySeparatorChar).AbsoluteUri);
            string copy = "RWS_Backup_CopyPath".Translate();
            if (Widgets.ButtonText(controls.Next(copy, 84f, 150f), copy))
                GUIUtility.systemCopyBuffer = done.Directory;
        }

        string? status = pending is not null ? "RWS_Export_Progress".Translate(Volatile.Read(ref filesWritten), 6)
            : error is not null ? "RWS_Export_Error".Translate(error)
            : completed is { } result ? "RWS_Export_Done".Translate(result.Directory)
            : null;
        if (status is null)
            return;
        var statusRect = new Rect(rect.x, rect.y + controls.Height + 2f, rect.width, 24f);
        Widgets.Label(statusRect, status.Truncate(statusRect.width));
        TooltipHandler.TipRegion(statusRect, status);
    }

    private static void Start(StatsView view, string scope)
    {
        completed = null;
        error = null;
        Volatile.Write(ref filesWritten, 0);
        try
        {
            var definitions = view.Numeric.Overall.Select(row => row.Definition)
                .Concat(view.Numeric.Colonies.SelectMany(colony => colony.Rows.Select(row => row.Definition)))
                .DistinctBy(d => d.Key, StringComparer.Ordinal).ToArray();
            var labels = definitions.ToDictionary(d => d.Key, MetricText.Label, StringComparer.Ordinal);
            foreach (var key in BasicMetrics.Keys)
                labels.TryAdd(key, MetricText.BasicLabel(key));
            var names = view.Stats.Colonies.ToDictionary(c => c.Key, Dialog_RimworldStatistics.ColonyName, StringComparer.Ordinal);
            var values = new Dictionary<NumericRow, string>();
            foreach (var row in view.Numeric.Overall)
                Capture(row);
            var units = definitions.Select(d => d.Acquisition).OfType<string>().Distinct(StringComparer.Ordinal)
                .ToDictionary(id => id, UnitRecordingStates.Get, StringComparer.Ordinal);
            var text = new ExportText(labels, names, values, LanguageDatabase.activeLanguage?.folderName ?? "",
                scope, RimworldStatisticsMod.InstalledVersion);
            var directory = Path.Combine(ProfileService.DirectoryPath, "exports");
            pending = Task.Run(() => StatisticsExport.Write(view, text, id => units.GetValueOrDefault(id, UnitRecordingState.NotRequired),
                directory, fileCompleted: n => Volatile.Write(ref filesWritten, n)));

            void Capture(NumericRow row)
            {
                var definition = row.Definition;
                if (row.Value.Known)
                    values[row] = MetricText.Format(
                        row.Breakdown.Length != 0 && definition.BreakdownUnit is { } unit ? definition with { Unit = unit } : definition,
                        row.Value);
                foreach (var child in row.Breakdowns)
                    Capture(child);
            }
        }
        catch (Exception failure)
        {
            error = failure.Message;
        }
    }
}
