using UnityEngine;
using Verse;

namespace RimworldStatistics.UI;

internal struct ControlRow(Rect bounds)
{
    private const float Gap = 6f;
    private float used;
    private float offset;

    public readonly float Height => offset + bounds.height;

    public Rect Next(string label, float minimum, float maximum) =>
        Next(Math.Clamp(Text.CalcSize(label).x + 20f, minimum, maximum));

    public Rect Next(float width, bool fill = false)
    {
        width = Math.Min(width, bounds.width);
        if (used > 0 && used + width > bounds.width)
        {
            used = 0;
            offset += bounds.height + Gap;
        }

        var rect = new Rect(bounds.x + used, bounds.y + offset, fill ? bounds.width - used : width, bounds.height);
        used += rect.width + Gap;
        return rect;
    }
}
