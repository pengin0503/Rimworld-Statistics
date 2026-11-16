namespace RimworldStatistics.Core;

/// <summary>Persisted choices for the title-screen statistics window (independent of UI scaling).</summary>
public enum StatisticsWindowSizePreset
{
    Standard = 0,
    Large = 1,
    ExtraLarge = 2,
    FitScreen = 3,
}

/// <summary>Resolves a size in RimWorld UI coordinates without exceeding the usable screen area.</summary>
public static class StatisticsWindowLayout
{
    public static (float Width, float Height) Resolve(
        StatisticsWindowSizePreset preset, float screenWidth, float screenHeight)
    {
        const float screenFraction = 0.90f;
        var availableWidth = Math.Max(1f, screenWidth * screenFraction);
        var availableHeight = Math.Max(1f, screenHeight * screenFraction);

        var (requestedWidth, requestedHeight) = preset switch
        {
            StatisticsWindowSizePreset.Large => (1400f, 900f),
            StatisticsWindowSizePreset.ExtraLarge => (1600f, 1000f),
            StatisticsWindowSizePreset.FitScreen => (availableWidth, availableHeight),
            _ => (1000f, 720f), // Includes invalid values from older/edited settings.
        };
        return (Math.Min(requestedWidth, availableWidth), Math.Min(requestedHeight, availableHeight));
    }

    public readonly record struct HeaderLayout(float TitleWidth, float SelectorX, float SelectorWidth);

    public readonly record struct SplitPanelLayout(float LeftWidth, float RightXOffset, float RightWidth);

    public static HeaderLayout ResolveHeader(float availableWidth, float closeButtonReserve = 32f, float gap = 8f)
    {
        if (availableWidth <= 0f)
            return new HeaderLayout(0f, 0f, 0f);

        var reserve = Math.Max(0f, closeButtonReserve);
        var usable = Math.Max(0f, availableWidth - reserve);
        if (usable <= 0f)
            return new HeaderLayout(0f, 0f, 0f);

        var minSelector = Math.Min(135f, usable * 0.45f);
        var maxSelector = Math.Max(minSelector, Math.Min(245f, usable * 0.55f));
        var selectorWidth = Math.Clamp(availableWidth * 0.30f, minSelector, maxSelector);
        var selectorX = Math.Max(0f, usable - selectorWidth);
        var titleWidth = Math.Max(0f, selectorX - Math.Max(0f, gap));
        return new HeaderLayout(titleWidth, selectorX, selectorWidth);
    }

    public static SplitPanelLayout ResolveCollapsibleSplit(float totalWidth, bool leftCollapsed, float gap = 10f)
    {
        if (totalWidth <= 0f)
            return new SplitPanelLayout(0f, 0f, 0f);
        if (leftCollapsed)
            return new SplitPanelLayout(0f, 0f, totalWidth);

        var safeGap = Math.Max(0f, gap);
        var maxLeft = Math.Max(0f, totalWidth - safeGap - 120f);
        var minLeft = Math.Min(200f, maxLeft);
        var leftWidth = Math.Min(350f, Math.Clamp(totalWidth * 0.34f, minLeft, maxLeft));
        var rightXOffset = leftWidth > 0f ? leftWidth + safeGap : 0f;
        var rightWidth = Math.Max(0f, totalWidth - rightXOffset);
        return new SplitPanelLayout(leftWidth, rightXOffset, rightWidth);
    }
}
