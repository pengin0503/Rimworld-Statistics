using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>One statistic offered by <see cref="MetricPickerDialog"/>: its key, shown label and MOD source.</summary>
internal sealed record MetricPickerChoice(string Key, string Label, string? Source);

/// <summary>A searchable list of statistics; picking one closes the window and reports its key.</summary>
internal sealed class MetricPickerDialog : Window
{
    private const float ItemHeight = 28f;

    private readonly IReadOnlyList<MetricPickerChoice> all;
    private readonly HashSet<string> allKeys;
    private readonly Dictionary<string, MetricPickerChoice> byKey;
    private readonly Action<string> onPick;
    private readonly string? heading;
    private readonly string? selectedKey;
    private string search = "";
    private string? priorSearch;
    private IReadOnlyList<MetricPickerChoice> filtered = [];
    private IReadOnlyList<MetricPickerChoice> recent = [];
    private string? priorRecentSignature;
    private Vector2 scroll;

    public MetricPickerDialog(IReadOnlyList<MetricPickerChoice> all, Action<string> onPick, string? heading = null,
        string? selectedKey = null)
    {
        this.all = all;
        this.onPick = onPick;
        this.heading = heading;
        this.selectedKey = selectedKey;
        allKeys = new HashSet<string>(StringComparer.Ordinal);
        byKey = new Dictionary<string, MetricPickerChoice>(StringComparer.Ordinal);
        foreach (var choice in all)
        {
            allKeys.Add(choice.Key);
            byKey.TryAdd(choice.Key, choice);
        }
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
    }

    public override Vector2 InitialSize => new(460f, 520f);

    public override void DoWindowContents(Rect rect)
    {
        try
        {
            var y = rect.y;
            if (heading is { } text)
            {
                var headingRect = new Rect(rect.x, y, Math.Max(0f, rect.width - 32f), 26f);
                var shown = text.Truncate(headingRect.width);
                Verse.Widgets.Label(headingRect, shown);
                if (shown != text)
                    TooltipHandler.TipRegion(headingRect, text);
                y += 28f;
            }
            Verse.Widgets.Label(new Rect(rect.x, y, Math.Max(0f, rect.width - 32f), 24f), "RWS_Rankings_Search".Translate());
            var next = Verse.Widgets.TextField(new Rect(rect.x, y + 26f, rect.width, 28f), search) ?? "";
            if (next != search)
            {
                search = next;
                scroll = Vector2.zero;
            }
            if (priorSearch != search)
            {
                priorSearch = search;
                var term = search.Trim();
                filtered = term.Length == 0
                    ? all
                    : all.Where(c => c.Label.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                        || (c.Source?.Contains(term, StringComparison.CurrentCultureIgnoreCase) ?? false)).ToArray();
            }
            RefreshRecent();

            var listTop = y + 60f;
            var listRect = new Rect(rect.x, listTop, rect.width, Math.Max(0f, rect.yMax - listTop));
            // The optional title row and the recent rows share the scroll view with the search results.
            var recentRows = recent.Count == 0 ? 0 : recent.Count + 1;
            var totalRows = recentRows + filtered.Count;
            if (totalRows == 0)
            {
                Verse.Widgets.Label(listRect, "RWS_Details_NoMatch".Translate());
                return;
            }
            var viewWidth = Math.Max(0f, listRect.width - 16f);
            string? picked = null;
            using (UiScopes.ScrollView(listRect, ref scroll, new Rect(0f, 0f, viewWidth, totalRows * ItemHeight)))
            using (UiScopes.GuiState())
            {
                var first = Math.Max(0, (int)(scroll.y / ItemHeight));
                var last = Math.Min(totalRows - 1, (int)((scroll.y + listRect.height) / ItemHeight) + 1);
                var anchor = Text.Anchor;
                for (var i = first; i <= last; i++)
                {
                    if (recentRows > 0 && i == 0)
                    {
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Verse.Widgets.Label(new Rect(4f, 0f, Math.Max(0f, viewWidth - 8f), ItemHeight),
                            "RWS_Picker_RecentlyUsed".Translate());
                        Text.Anchor = anchor;
                        continue;
                    }
                    if (i < recentRows)
                    {
                        var recentChoice = recent[i - 1];
                        if (DrawChoiceRow(recentChoice, i * ItemHeight, viewWidth))
                            picked = recentChoice.Key;
                        continue;
                    }
                    var choice = filtered[i - recentRows];
                    if (DrawChoiceRow(choice, i * ItemHeight, viewWidth))
                        picked = choice.Key;
                }
                Text.Anchor = anchor;
            }
            if (picked is { } pickedKey)
            {
                Dialog_RimworldStatistics.UpdateActivePreferences(p => RecentList.Add(p.RecentMetricKeys, pickedKey));
                onPick(pickedKey);
                Close();
            }
        }
        catch (Exception e)
        {
            Log.ErrorOnce($"[RimworldStatistics] The metric picker window failed to draw.\n{e}", "RWS.metricPicker".GetHashCode());
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

    private void RefreshRecent()
    {
        var prefs = Dialog_RimworldStatistics.ActivePreferences;
        var keys = prefs.RecentMetricKeys;
        var signature = search + "\u0000" + string.Join("\u0000", keys);
        if (string.Equals(signature, priorRecentSignature, StringComparison.Ordinal))
            return;
        priorRecentSignature = signature;
        var visible = RecentList.Visible(keys, allKeys);
        var found = new List<MetricPickerChoice>(visible.Count);
        foreach (var key in visible)
        {
            if (byKey.TryGetValue(key, out var choice))
                found.Add(choice);
        }
        recent = found.ToArray();
    }

    private bool DrawChoiceRow(MetricPickerChoice choice, float top, float width)
    {
        var row = new Rect(0f, top, width, ItemHeight - 1f);
        if (string.Equals(choice.Key, selectedKey, StringComparison.Ordinal))
            Verse.Widgets.DrawBoxSolid(row, new Color(0.26f, 0.44f, 0.56f, 0.35f));
        Verse.Widgets.DrawHighlightIfMouseover(row);
        var label = choice.Source is null ? choice.Label : $"{choice.Label} · {choice.Source}";
        var shown = label.Truncate(Math.Max(0f, row.width - 8f));
        var anchor = Text.Anchor;
        Text.Anchor = TextAnchor.MiddleLeft;
        Verse.Widgets.Label(new Rect(4f, row.y, Math.Max(0f, row.width - 8f), row.height), shown);
        Text.Anchor = anchor;
        if (shown != label)
            TooltipHandler.TipRegion(row, label);
        return Verse.Widgets.ButtonInvisible(row);
    }
}
