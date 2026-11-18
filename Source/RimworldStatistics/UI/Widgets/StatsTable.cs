using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal sealed class StatsTable(float rowHeight, params ColumnSpec[] columns)
{
    private float available = -1;
    private ColumnPlan? plan;
    private object? data;
    private object? language;
    private string[][] display = [];
    private string?[][] tips = [];

    public void Draw(Rect rect, ref Vector2 scroll, string[] headers, IReadOnlyList<string[]> rows, IReadOnlyList<bool>? rightAligned = null)
    {
        if (rect.height <= rowHeight)
            return;

        var width = Math.Max(0, rect.width - 16f);
        var currentLanguage = LanguageDatabase.activeLanguage;
        if (plan is null || available != width)
        {
            available = width;
            plan = ColumnLayout.Resolve(columns, width);
            data = null;
        }
        if (!ReferenceEquals(data, rows) || !ReferenceEquals(language, currentLanguage))
        {
            data = rows;
            language = currentLanguage;
            display = new string[rows.Count][];
            tips = new string?[rows.Count][];
        }

        var previousAnchor = Text.Anchor;
        var previousColor = GUI.color;
        try
        {
            Text.Anchor = TextAnchor.MiddleLeft;
            GUI.BeginGroup(new Rect(rect.x, rect.y, width, rowHeight));
            try
            {
                GUI.color = new Color(0.8f, 0.8f, 0.8f);
                DrawCells(new Rect(-scroll.x, 0, plan.TotalWidth, rowHeight), headers, null, rightAligned);
            }
            finally
            {
                GUI.color = previousColor;
                GUI.EndGroup();
            }

            var viewport = new Rect(rect.x, rect.y + rowHeight, rect.width, Math.Max(0f, rect.height - rowHeight));
            if (viewport.height <= 0f)
                return;
            Widgets.BeginScrollView(viewport, ref scroll, new Rect(0, 0, plan.TotalWidth, rows.Count * rowHeight));
            try
            {
                var first = Math.Max(0, (int)(scroll.y / rowHeight));
                var last = Math.Min(rows.Count - 1, (int)((scroll.y + viewport.height) / rowHeight) + 1);
                for (var i = first; i <= last; i++)
                {
                    var row = new Rect(0, i * rowHeight, plan.TotalWidth, rowHeight);
                    if (i % 2 == 1)
                        Widgets.DrawHighlight(row, 0.25f);
                    if (display[i] is null)
                    {
                        var source = rows[i];
                        display[i] = new string[source.Length];
                        tips[i] = new string?[source.Length];
                        for (var c = 0; c < source.Length; c++)
                        {
                            var shown = source[c].Truncate(Math.Max(0, plan.Widths[c] - 8f));
                            display[i][c] = shown;
                            if (shown != source[c])
                                tips[i][c] = source[c];
                        }
                    }
                    DrawCells(row, display[i], tips[i], rightAligned);
                }
            }
            finally
            {
                Widgets.EndScrollView();
            }
        }
        finally
        {
            GUI.color = previousColor;
            Text.Anchor = previousAnchor;
        }
    }

    private void DrawCells(Rect row, string[] cells, string?[]? fullText, IReadOnlyList<bool>? rightAligned)
    {
        var x = row.x;
        for (var i = 0; i < cells.Length; i++)
        {
            var width = plan!.Widths[i];
            var cell = new Rect(x + 4f, row.y, Math.Max(0, width - 8f), row.height);
            Text.Anchor = rightAligned is not null && i < rightAligned.Count && rightAligned[i]
                ? TextAnchor.MiddleRight
                : TextAnchor.MiddleLeft;
            var shown = fullText is null ? cells[i].Truncate(cell.width) : cells[i];
            Widgets.Label(cell, shown);
            var tip = fullText is null && shown != cells[i] ? cells[i] : fullText?[i];
            if (tip is not null)
                TooltipHandler.TipRegion(cell, tip);
            x += width;
        }
    }
}
