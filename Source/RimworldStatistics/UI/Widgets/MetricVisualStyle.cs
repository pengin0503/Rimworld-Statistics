using RimworldStatistics.Core;
using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal static class MetricVisualStyle
{
    internal static Color StateColor(MetricVisualKind kind) => kind switch
    {
        MetricVisualKind.Complete => new Color(0.76f, 0.89f, 0.78f),
        MetricVisualKind.Partial => new Color(0.92f, 0.79f, 0.52f),
        MetricVisualKind.Experimental => new Color(0.78f, 0.74f, 0.92f),
        MetricVisualKind.Missing => new Color(0.64f, 0.64f, 0.67f),
        MetricVisualKind.NotApplicable => new Color(0.55f, 0.55f, 0.58f),
        _ => Color.white,
    };

    internal static Color ValueColor(MetricVisualKind kind) => kind switch
    {
        MetricVisualKind.Missing or MetricVisualKind.NotApplicable => new Color(0.68f, 0.68f, 0.70f),
        MetricVisualKind.Partial => new Color(0.86f, 0.84f, 0.78f),
        _ => Color.white,
    };

    /// <summary>Draws a crisp 12x12 mini 3-bar chart icon using solid boxes so it renders reliably across bitmap fonts and UI scales.</summary>
    internal static void DrawMiniChartIcon(Rect rect, bool hovered)
    {
        if (rect.width < 6f || rect.height < 6f)
            return;
        var color = hovered ? new Color(0.56f, 0.82f, 0.95f, 0.95f) : new Color(0.72f, 0.78f, 0.84f, 0.65f);
        var barWidth = Math.Max(2f, (rect.width - 3f) / 3f);
        var h1 = Math.Max(3f, rect.height * 0.42f);
        var h2 = Math.Max(5f, rect.height * 0.92f);
        var h3 = Math.Max(4f, rect.height * 0.67f);
        Widgets.DrawBoxSolid(new Rect(rect.x, rect.yMax - h1, barWidth, h1), color);
        Widgets.DrawBoxSolid(new Rect(rect.x + barWidth + 1.5f, rect.yMax - h2, barWidth, h2), color);
        Widgets.DrawBoxSolid(new Rect(rect.x + (barWidth + 1.5f) * 2f, rect.yMax - h3, barWidth, h3), color);
    }
}
