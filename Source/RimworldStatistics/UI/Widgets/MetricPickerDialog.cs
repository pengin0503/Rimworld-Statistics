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
    private readonly Action<string> onPick;
    private readonly string? heading;
    private readonly string? selectedKey;
    private string search = "";
    private string? priorSearch;
    private IReadOnlyList<MetricPickerChoice> filtered = [];
    private Vector2 scroll;

    public MetricPickerDialog(IReadOnlyList<MetricPickerChoice> all, Action<string> onPick, string? heading = null,
        string? selectedKey = null)
    {
        this.all = all;
        this.onPick = onPick;
        this.heading = heading;
        this.selectedKey = selectedKey;
        doCloseX = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
    }

    public override Vector2 InitialSize => new(460f, 520f);

    public override void DoWindowContents(Rect rect)
    {
        var y = rect.y;
        if (heading is { } text)
        {
            var headingRect = new Rect(rect.x, y, Math.Max(0f, rect.width - 32f), 26f);
            var shown = text.Truncate(headingRect.width);
            Widgets.Label(headingRect, shown);
            if (shown != text)
                TooltipHandler.TipRegion(headingRect, text);
            y += 28f;
        }
        Widgets.Label(new Rect(rect.x, y, Math.Max(0f, rect.width - 32f), 24f), "RWS_Rankings_Search".Translate());
        var next = Widgets.TextField(new Rect(rect.x, y + 26f, rect.width, 28f), search) ?? "";
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

        var listTop = y + 60f;
        var listRect = new Rect(rect.x, listTop, rect.width, Math.Max(0f, rect.yMax - listTop));
        if (filtered.Count == 0)
        {
            Widgets.Label(listRect, "RWS_Details_NoMatch".Translate());
            return;
        }
        var viewWidth = Math.Max(0f, listRect.width - 16f);
        Widgets.BeginScrollView(listRect, ref scroll, new Rect(0f, 0f, viewWidth, filtered.Count * ItemHeight));
        var first = Math.Max(0, (int)(scroll.y / ItemHeight));
        var last = Math.Min(filtered.Count - 1, (int)((scroll.y + listRect.height) / ItemHeight) + 1);
        var anchor = Text.Anchor;
        for (var i = first; i <= last; i++)
        {
            var choice = filtered[i];
            var row = new Rect(0f, i * ItemHeight, viewWidth, ItemHeight - 1f);
            if (string.Equals(choice.Key, selectedKey, StringComparison.Ordinal))
                Widgets.DrawBoxSolid(row, new Color(0.26f, 0.44f, 0.56f, 0.35f));
            Widgets.DrawHighlightIfMouseover(row);
            var label = choice.Source is null ? choice.Label : $"{choice.Label} · {choice.Source}";
            var shown = label.Truncate(Math.Max(0f, row.width - 8f));
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(4f, row.y, Math.Max(0f, row.width - 8f), row.height), shown);
            Text.Anchor = anchor;
            if (shown != label)
                TooltipHandler.TipRegion(row, label);
            if (Widgets.ButtonInvisible(row))
            {
                onPick(choice.Key);
                Close();
            }
        }
        Widgets.EndScrollView();
    }
}
