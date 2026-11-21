using RimworldStatistics.VgeExtraction;
using RimworldStatistics.Core;

namespace RimworldStatistics.Core.Tests;

public sealed class VgeExtractionStatisticsTests
{
    [Theory]
    [InlineData(true, true, true, 1, 1)]
    [InlineData(true, true, true, 3, 3)]
    [InlineData(false, true, true, 1, 0)]
    [InlineData(true, false, true, 1, 0)]
    [InlineData(true, true, false, 1, 0)]
    [InlineData(true, true, true, 0, 0)]
    [InlineData(true, true, true, -1, 0)]
    public void RecordsOnlyActualExtractedItemsPlaced(
        bool successfulPlacement, bool colonistUser, bool correctExtractCall,
        int amount, long expected)
        => Assert.Equal(expected, VgeExtractionRules.ItemsPlaced(
            successfulPlacement, colonistUser, correctExtractCall, amount));
}
