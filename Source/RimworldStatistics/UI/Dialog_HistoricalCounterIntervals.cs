using System.Globalization;
using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>On-demand save evidence kept outside live and lifetime statistics.</summary>
internal sealed class Dialog_HistoricalCounterIntervals : Window
{
    private sealed record Row(int StartTick, string[] Cells);

    private readonly StatsView? view;
    private readonly Task<IReadOnlyList<HistoricalCounterInterval>> pending;
    private readonly StatsTable table = new(26f, new(130f, 2f), new(140f, 3f), new(140f, 3f),
        new(112f, 1f), new(112f, 1f), new(110f, 1f), new(116f, 1f));
    private Vector2 scroll;
    private object? language;
    private string? nameSignature;
    private string[] headers = [];
    private Row[] allRows = [];
    private string[][] rows = [];
    // U27 search/sort are session-local (the dialog is rebuilt on open), like the other tabs' shaped caches.
    private string search = "";
    private bool sortAscending;
    private string shapedSearch = "";
    private bool shapedSortAscending;
    private bool shapeDirty = true;

    internal Dialog_HistoricalCounterIntervals(ProfileSession session, StatsView? view = null)
    {
        this.view = view;
        doCloseX = true;
        absorbInputAroundWindow = true;
        pending = Task.Run(session.ReadHistoricalCounterIntervals);
    }

    public override Vector2 InitialSize => new(1000f, 520f);

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            var heading = (string)"RWS_HistoricalIntervals_Title".Translate();
            var explanation = (string)"RWS_HistoricalIntervals_Explanation".Translate();
            Verse.Widgets.Label(new Rect(rect.x, rect.y, Math.Max(0f, rect.width - 34f), 28f), heading);
            var height = Math.Max(28f, Text.CalcHeight(explanation, rect.width));
            Verse.Widgets.Label(new Rect(rect.x, rect.y + 30f, rect.width, height), explanation);
            var body = new Rect(rect.x, rect.y + 38f + height, rect.width, Math.Max(0f, rect.height - 38f - height));

            if (!pending.IsCompleted)
            {
                Verse.Widgets.Label(body, "RWS_Loading".Translate());
                return;
            }
            if (!pending.IsCompletedSuccessfully)
            {
                _ = pending.Exception;
                Verse.Widgets.Label(body, "RWS_HistoricalIntervals_Error".Translate());
                return;
            }
            if (pending.Result.Count == 0)
            {
                Verse.Widgets.Label(body, "RWS_HistoricalIntervals_Empty".Translate());
                return;
            }

            var activeLanguage = LanguageDatabase.activeLanguage;
            var activePrefs = Dialog_RimworldStatistics.ActivePreferences;
            var currentSignature = ColonyPresentation.Signature(activePrefs);
            if (allRows.Length == 0 || !ReferenceEquals(language, activeLanguage) || nameSignature != currentSignature)
            {
                language = activeLanguage;
                nameSignature = currentSignature;
                headers = ["RWS_Col_Colony".Translate(), "RWS_HistoricalIntervals_ParentSave".Translate(),
                    "RWS_HistoricalIntervals_ChildSave".Translate(), "RWS_HistoricalIntervals_Start".Translate(), "RWS_HistoricalIntervals_End".Translate(),
                    "RWS_HistoricalIntervals_Raids".Translate(), "RWS_HistoricalIntervals_Deaths".Translate()];
                allRows = pending.Result.Select(row => new Row(row.StartTick, new[]
                {
                    ResolveColonyDisplay(row, activePrefs),
                    Boundary(row.ParentSaveName, row.ParentCheckpointId),
                    Boundary(row.ChildSaveName, row.ChildCheckpointId),
                    row.StartTick.ToString("N0", CultureInfo.CurrentCulture),
                    row.EndTick.ToString("N0", CultureInfo.CurrentCulture),
                    row.RaidsIncrease.ToString("N0", CultureInfo.CurrentCulture),
                    row.ColonistDeathsIncrease.ToString("N0", CultureInfo.CurrentCulture),
                })).ToArray();
                shapeDirty = true;
            }

            var controls = new ControlRow(new Rect(body.x, body.y, body.width, 28f));
            var searchLabel = (string)"RWS_HistoricalIntervals_Search".Translate();
            var labelRect = controls.Next(searchLabel, 56f, 96f);
            using (UiScopes.GuiState())
            {
                Text.Anchor = TextAnchor.MiddleLeft;
                Verse.Widgets.Label(labelRect, searchLabel.Truncate(Math.Max(0f, labelRect.width - 8f)));
            }
            var searchRect = controls.Next(Math.Max(140f, Math.Min(body.width * 0.4f, 320f)));
            var nextSearch = Verse.Widgets.TextField(searchRect, search) ?? "";
            if (nextSearch != search)
                search = nextSearch;

            var sortText = sortAscending
                ? (string)"RWS_HistoricalIntervals_SortAsc".Translate()
                : (string)"RWS_HistoricalIntervals_SortDesc".Translate();
            var sortRect = controls.Next(sortText, 100f, 160f);
            var shownSort = sortText.Truncate(Math.Max(0f, sortRect.width - 12f));
            var sortTip = (string)"RWS_HistoricalIntervals_Sort".Translate();
            if (shownSort != sortText)
                sortTip += "\n" + sortText;
            TooltipHandler.TipRegion(sortRect, sortTip);
            if (Verse.Widgets.ButtonText(sortRect, shownSort))
                sortAscending = !sortAscending;

            var copyText = (string)"RWS_HistoricalIntervals_Copy".Translate();
            var copyRect = controls.Next(copyText, 70f, 120f);
            var shownCopy = copyText.Truncate(Math.Max(0f, copyRect.width - 12f));
            if (shownCopy != copyText)
                TooltipHandler.TipRegion(copyRect, copyText);
            var copyRequested = Verse.Widgets.ButtonText(copyRect, shownCopy);

            // Shape only when the inputs change; Copy therefore sees the same filtered/sorted rows as the table.
            if (shapeDirty || search != shapedSearch || sortAscending != shapedSortAscending)
                ShapeRows();
            if (copyRequested)
                CopyRows();

            var top = body.y + controls.Height + 6f;
            var tableRect = new Rect(body.x, top, body.width, Math.Max(0f, body.yMax - top));
            if (rows.Length == 0)
            {
                Verse.Widgets.Label(new Rect(body.x, top, body.width, 28f), "RWS_Details_NoMatch".Translate());
                return;
            }
            table.Draw(tableRect, ref scroll, headers, rows);
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] The historical intervals window failed to draw.\n{e}", "RWS.historicalIntervals".GetHashCode());
            Verse.Widgets.Label(rect.ContractedBy(20f), "RWS_WindowError".Translate());
        }
        finally
        {
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            Text.WordWrap = true;
            GUI.color = Color.white;
        }
    }

    /// <summary>
    /// U27: filter the cached displayed cells with the shared case-insensitive query syntax, then order by the raw
    /// start tick (the formatted cell is not numeric). Only runs when the query, direction, or source rows change.
    /// </summary>
    private void ShapeRows()
    {
        var query = SearchQuery.Parse(search);
        var matching = allRows.Where(row => query.Matches(row.Cells));
        rows = (sortAscending
            ? matching.OrderBy(row => row.StartTick)
            : matching.OrderByDescending(row => row.StartTick))
            .Select(row => row.Cells).ToArray();
        shapedSearch = search;
        shapedSortAscending = sortAscending;
        shapeDirty = false;
        scroll = Vector2.zero;
    }

    /// <summary>U27: copy exactly the rows currently listed, one tab-separated line each, with LF line endings.</summary>
    private void CopyRows()
    {
        GUIUtility.systemCopyBuffer = string.Join("\n", rows.Select(row => string.Join("\t", row)));
    }

    private string ResolveColonyDisplay(HistoricalCounterInterval row, ViewPreferences? preferences)
    {
        if (view is not null && !string.IsNullOrWhiteSpace(row.CampaignId))
        {
            var resolved = InGameColonyAccess.Resolve(view, row.CampaignId);
            if (resolved is { Found: true, ColonyKey: { } key }
                && view.Stats.Colonies.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal)) is { } stats)
            {
                return Dialog_RimworldStatistics.ColonyName(stats, preferences);
            }
        }

        var fpKey = !string.IsNullOrWhiteSpace(row.FingerprintHash) ? "fp:" + row.FingerprintHash : "";
        string[]? aliases = !string.IsNullOrWhiteSpace(row.CampaignId) ? ["id:" + row.CampaignId] : null;
        return ColonyPresentation.TryGetCustomName(preferences, fpKey, aliases)
            ?? row.ColonyName
            ?? Dialog_RimworldStatistics.Dash;
    }

    private static string Boundary(string? name, string id) => name is null ? id : $"{name} ({id})";
}
