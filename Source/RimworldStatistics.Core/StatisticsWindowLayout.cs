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
}
