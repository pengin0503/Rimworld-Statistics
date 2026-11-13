using RimworldStatistics.VqeCryptoforge;

namespace RimworldStatistics.Core.Tests;

public sealed class VqeCryptoforgeStatisticsTests
{
    [Theory]
    [InlineData(true, 7499, 7500, true, false, true)]
    [InlineData(true, 7498, 7499, true, false, false)]
    [InlineData(true, 7499, 7500, true, true, false)]
    [InlineData(true, 7499, 7500, false, false, false)]
    [InlineData(false, 7499, 7500, true, false, false)]
    [InlineData(true, 7500, 7501, true, false, false)]
    public void CountsOnlyATerminalScanTransitionOnAPlayerAttendedMap(bool attended,
        int before, int after, bool wasScanning, bool stillScanning, bool expected)
        => Assert.Equal(expected, VqeCryptoforgeRules.Completed(attended, before, after, 7500, wasScanning, stillScanning));
}
