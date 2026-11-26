namespace RimworldStatistics.Core.Tests;

/// <summary>Q21: every classified import problem has one stable code, and unknown text maps to the generic one.</summary>
public sealed class ImportErrorCodeTests
{
    [Fact]
    public void EveryClassifiedProblemKindMapsToExactlyOneCode()
    {
        var codes = Enum.GetValues<ImportProblemKind>().Select(ImportErrorCodes.CodeFor).ToArray();

        Assert.DoesNotContain(ImportErrorCode.None, codes);
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.All(codes, code =>
        {
            var text = code.Text();
            Assert.Matches("^RWS-E1[0-9]{3}$", text);
        });
    }

    [Theory]
    [InlineData(ObservationStatus.Ok, null, ImportErrorCode.None, "")]
    [InlineData(ObservationStatus.Error, "not a savegame", ImportErrorCode.NotASavegame, "RWS-E1005")]
    [InlineData(ObservationStatus.Error, "IOException: save changed during read", ImportErrorCode.ChangedDuringRead, "RWS-E1001")]
    [InlineData(ObservationStatus.Error, "UnauthorizedAccessException: denied", ImportErrorCode.Unreadable, "RWS-E1002")]
    [InlineData(ObservationStatus.Error, "XmlException: bad xml", ImportErrorCode.Damaged, "RWS-E1003")]
    [InlineData(ObservationStatus.Error, "TickAnomaly: ticksGame is negative", ImportErrorCode.TickAnomaly, "RWS-E1004")]
    [InlineData(ObservationStatus.Unidentifiable, "seedString,startingTile", ImportErrorCode.MissingIdentity, "RWS-E1006")]
    [InlineData(ObservationStatus.Error, "NullReferenceException: unknown reason", ImportErrorCode.Unexpected, "RWS-E1007")]
    [InlineData(ObservationStatus.Error, null, ImportErrorCode.Unexpected, "RWS-E1007")]
    public void ReasonTextMapsToItsStableCode(ObservationStatus status, string? reason, ImportErrorCode expected, string text)
    {
        var observation = new SaveObservation { Status = status, ErrorReason = reason };

        Assert.Equal(expected, ImportProblems.CodeFor(observation));
        Assert.Equal(expected, observation.ErrorCode);
        Assert.Equal(text, expected.Text());
    }

    [Fact]
    public void EveryClassifyOutcomeRoundTripsThroughItsCode()
    {
        var samples = new[]
        {
            (ObservationStatus.Error, "not a savegame"),
            (ObservationStatus.Error, "IOException: changed during read"),
            (ObservationStatus.Error, "FileNotFoundException: missing"),
            (ObservationStatus.Error, "InvalidDataException: bad data"),
            (ObservationStatus.Error, "SomeUnknownException: ?"),
            (ObservationStatus.Unidentifiable, "missing"),
        };

        foreach (var (status, reason) in samples)
        {
            var observation = new SaveObservation { Status = status, ErrorReason = reason };
            var kind = ImportProblems.Classify(observation)!.Value;
            var code = ImportProblems.CodeFor(observation);
            Assert.NotEqual(ImportErrorCode.None, code);
            Assert.Equal(code, ImportProblems.CodeFor(kind));
        }
    }
}
