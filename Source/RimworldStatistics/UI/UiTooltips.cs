using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>
/// Two-stage tooltips (U36): the concise text normally, the technical identifier while Shift is held. The Shift state is
/// read when the tooltip is registered, which is the only point at which Verse's tooltip system takes a text.
/// </summary>
internal static class UiTooltips
{
    public static void Tip(Rect rect, string concise, string? technical)
    {
        if (!string.IsNullOrEmpty(technical) && Event.current is { shift: true })
            TooltipHandler.TipRegion(rect, technical);
        else
            TooltipHandler.TipRegion(rect, concise);
    }

    /// <summary>The same, keeping the lazy text getter and unique ID of <see cref="TooltipHandler.TipRegion(Rect, System.Func{string}, int)"/>.</summary>
    public static void Tip(Rect rect, Func<string> concise, string? technical, int uniqueId)
    {
        if (!string.IsNullOrEmpty(technical) && Event.current is { shift: true })
            TooltipHandler.TipRegion(rect, new TipSignal(technical!, uniqueId));
        else
            TooltipHandler.TipRegion(rect, concise, uniqueId);
    }
}
