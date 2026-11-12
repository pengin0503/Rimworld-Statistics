namespace RimworldStatistics.Core.Tests;

public sealed class StatisticsWindowLayoutTests
{
    [Theory]
    [InlineData(StatisticsWindowSizePreset.Standard, 1000f, 720f)]
    [InlineData(StatisticsWindowSizePreset.Large, 1400f, 900f)]
    [InlineData(StatisticsWindowSizePreset.ExtraLarge, 1600f, 1000f)]
    [InlineData(StatisticsWindowSizePreset.FitScreen, 2304f, 1296f)]
    public void PresetsKeepUnscaledDimensionsOnLargeScreen(
        StatisticsWindowSizePreset preset, float expectedWidth, float expectedHeight)
    {
        var result = StatisticsWindowLayout.Resolve(preset, 2560f, 1440f);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }

    [Theory]
    [InlineData(StatisticsWindowSizePreset.Standard)]
    [InlineData(StatisticsWindowSizePreset.Large)]
    [InlineData(StatisticsWindowSizePreset.ExtraLarge)]
    [InlineData(StatisticsWindowSizePreset.FitScreen)]
    public void EveryPresetFitsWithinNinetyPercentOfSmallScreen(StatisticsWindowSizePreset preset)
    {
        var result = StatisticsWindowLayout.Resolve(preset, 1280f, 720f);
        Assert.InRange(result.Width, 1f, 1152f);
        Assert.InRange(result.Height, 1f, 648f);
    }

    [Fact]
    public void InvalidSavedPreferenceFallsBackToStandard()
    {
        var result = StatisticsWindowLayout.Resolve((StatisticsWindowSizePreset)99, 2560f, 1440f);
        Assert.Equal(1000f, result.Width);
        Assert.Equal(720f, result.Height);
    }
}
