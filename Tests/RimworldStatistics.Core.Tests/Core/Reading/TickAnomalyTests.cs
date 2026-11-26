using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>S36: an impossible tickManager becomes a diagnosable Damaged anomaly instead of statistics or a crash.</summary>
public sealed class TickAnomalyTests
{
    private static SaveReadResult Read(SaveXmlBuilder builder)
    {
        var bytes = builder.Build();
        using var stream = new MemoryStream(bytes);
        return new SaveReader().ReadDetailed(stream, "a.rws", bytes.Length, DateTime.UnixEpoch);
    }

    private static void AssertAnomaly(SaveReadResult result, string detail)
    {
        Assert.Equal(ObservationStatus.Error, result.Observation.Status);
        Assert.StartsWith(SaveReader.TickAnomalyPrefix, result.Observation.ErrorReason, StringComparison.Ordinal);
        Assert.Contains(detail, result.Observation.ErrorReason, StringComparison.Ordinal);
        Assert.Equal(ImportProblemKind.Damaged, ImportProblems.Classify(result.Observation));
        Assert.Null(result.Metrics);
        Assert.Null(result.Checkpoint);
        Assert.Equal(0, result.Observation.Values.TicksGame);
        Assert.Equal(0, result.Observation.Identity.GameStartAbsTick);
    }

    [Fact]
    public void ANegativeTicksGameIsAnAnomaly()
        => AssertAnomaly(Read(new SaveXmlBuilder().Minimal().Tick(ticksGame: -1, start: 15000)), "ticksGame is negative");

    [Fact]
    public void ATicksGameAboveTheSaneMaximumIsAnAnomaly()
        => AssertAnomaly(Read(new SaveXmlBuilder().Minimal().Tick(ticksGame: SaveReader.MaxSaneTicksGame + 1, start: 0)),
            "above the sane maximum");

    [Fact]
    public void TicksGameTextBeyondLongIsTheSameAnomaly_NotAnOverflowAccident()
    {
        var result = Read(new SaveXmlBuilder().Minimal().TickRaw("99999999999999999999", start: 0));
        AssertAnomaly(result, "ticksGame is not an integer");
        Assert.DoesNotContain("OverflowException", result.Observation.ErrorReason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANegativeGameStartAbsTickIsAnAnomaly()
        => AssertAnomaly(Read(new SaveXmlBuilder().Minimal().Tick(ticksGame: 60000, start: -5)), "gameStartAbsTick is negative");

    [Fact]
    public void TheSaneMaximumItselfIsAccepted()
    {
        var result = Read(new SaveXmlBuilder().Minimal().Tick(ticksGame: SaveReader.MaxSaneTicksGame, start: 0));
        Assert.Equal(ObservationStatus.Ok, result.Observation.Status);
        Assert.Null(result.Observation.ErrorReason);
        Assert.NotNull(result.Metrics);
        Assert.Equal(SaveReader.MaxSaneTicksGame, result.Observation.Values.TicksGame);
    }

    [Fact]
    public void NormalSavesAreUnaffected()
    {
        var result = Read(new SaveXmlBuilder().Minimal().Tick(ticksGame: 60000, start: 15000));
        Assert.Equal(ObservationStatus.Ok, result.Observation.Status);
        Assert.Equal((60000, 15000), (result.Observation.Values.TicksGame, result.Observation.Identity.GameStartAbsTick));
        Assert.NotNull(result.Metrics);
    }
}
