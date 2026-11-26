using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>S35: one automatic retry when a save changes during the read; only that failure, only once, never when cancelled.</summary>
public sealed class SaveReaderRetryTests
{
    private static SaveReadResult Result(ObservationStatus status, string? reason = null)
        => new(new SaveObservation { FileName = "a.rws", Status = status, ErrorReason = reason }, null);

    private static SaveReadResult Changed()
        => Result(ObservationStatus.Error, "IOException: save changed during read");

    private static SaveReadResult Damaged()
        => Result(ObservationStatus.Error, "InvalidDataException: broken");

    private static SaveReadResult Ok() => Result(ObservationStatus.Ok);

    [Fact]
    public void AChangedReadSucceedsOnTheRetry_AfterExactlyTwoAttempts()
    {
        var reader = new SaveReader();
        var attempts = 0;
        reader.ReadAttemptForTests = (_, _) => ++attempts == 1 ? Changed() : Ok();

        var result = reader.ReadUnlessCancelled("a.rws", CancellationToken.None)!;

        Assert.Equal(2, attempts);
        Assert.Equal(ObservationStatus.Ok, result.Observation.Status);
    }

    [Fact]
    public void AFileThatKeepsChangingEndsAsChangedAfterExactlyTwoAttempts()
    {
        var reader = new SaveReader();
        var attempts = 0;
        reader.ReadAttemptForTests = (_, _) =>
        {
            attempts++;
            return Changed();
        };

        var result = reader.ReadUnlessCancelled("a.rws", CancellationToken.None)!;

        Assert.Equal(SaveReader.MaxChangedDuringReadAttempts, attempts);
        Assert.Equal(2, attempts);
        Assert.Equal(ObservationStatus.Error, result.Observation.Status);
        Assert.Equal(ImportProblemKind.ChangedDuringRead, ImportProblems.Classify(result.Observation));
    }

    [Fact]
    public void OtherFailuresAreNotRetried()
    {
        var reader = new SaveReader();
        var attempts = 0;
        reader.ReadAttemptForTests = (_, _) =>
        {
            attempts++;
            return Damaged();
        };

        var result = reader.ReadUnlessCancelled("a.rws", CancellationToken.None)!;

        Assert.Equal(1, attempts);
        Assert.Equal(ImportProblemKind.Damaged, ImportProblems.Classify(result.Observation));
    }

    [Fact]
    public void AStoppedReadIsNotRetried()
    {
        var reader = new SaveReader();
        var attempts = 0;
        reader.ReadAttemptForTests = (_, _) =>
        {
            attempts++;
            return null;
        };

        Assert.Null(reader.ReadUnlessCancelled("a.rws", CancellationToken.None));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ACancelledReadMakesNoAttempt()
    {
        var reader = new SaveReader();
        var attempts = 0;
        reader.ReadAttemptForTests = (_, _) =>
        {
            attempts++;
            return Ok();
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Null(reader.ReadUnlessCancelled("a.rws", cts.Token));
        Assert.Equal(0, attempts);
    }

    [Fact]
    public void ACancellationRequestedBetweenAttemptsReturnsNullInsteadOfRetrying()
    {
        var reader = new SaveReader();
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        reader.ReadAttemptForTests = (_, _) =>
        {
            attempts++;
            cts.Cancel();
            return Changed();
        };

        Assert.Null(reader.ReadUnlessCancelled("a.rws", cts.Token));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void ARealFileReplacedBetweenAttemptsIsReadOnTheRetry()
    {
        using var dir = new TempDir();
        var path = dir.File("a.rws");
        File.WriteAllBytes(path, new SaveXmlBuilder().Minimal().World(seed: "old").Build());
        var replacement = new SaveXmlBuilder().Minimal().World(seed: "new").Build();

        var reader = new SaveReader();
        var attempts = 0;
        reader.ReadAttemptForTests = (p, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                // The game wrote a new save over this path before the import retried it.
                File.WriteAllBytes(p, replacement);
                return Changed();
            }
            // A fresh reader without the test seam performs the real second attempt.
            return new SaveReader().ReadDetailed(p);
        };

        var result = reader.ReadUnlessCancelled(path, CancellationToken.None)!;

        Assert.Equal(2, attempts);
        Assert.Equal(ObservationStatus.Ok, result.Observation.Status);
        Assert.Equal("new", result.Observation.Identity.SeedString);
    }

    [Fact]
    public void TheStreamOverloadStillReturnsAChangedReadWithoutRetrying()
    {
        var bytes = new SaveXmlBuilder().Minimal().Build();
        using var stream = new MemoryStream(bytes);
        var result = new SaveReader().ReadUnlessCancelled(stream, "a.rws", bytes.Length + 1, DateTime.UnixEpoch,
            CancellationToken.None)!;

        Assert.Equal(ObservationStatus.Error, result.Observation.Status);
        Assert.Equal(ImportProblemKind.ChangedDuringRead, ImportProblems.Classify(result.Observation));
        Assert.Null(result.Metrics);
    }
}
