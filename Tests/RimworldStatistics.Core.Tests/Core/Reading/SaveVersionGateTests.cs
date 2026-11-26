namespace RimworldStatistics.Core.Tests;

/// <summary>S17: one central version gate, and no remaining scattered "1.7" comparisons in the reader.</summary>
public sealed class SaveVersionGateTests
{
    [Theory]
    [InlineData("1.7.4985 rev1269", true, true, true)]
    [InlineData("1.7.5011", true, true, true)]
    [InlineData("1.6.4566 rev607", false, true, true)]
    [InlineData("1.5.4104 rev435", false, true, true)]
    [InlineData("1.8.5000 rev1", false, false, false)]   // future, deliberately not admitted
    [InlineData("2.0.1 rev1", false, false, false)]
    [InlineData("1.4.3901 rev1234", false, false, false)]
    [InlineData("garbage", false, false, false)]
    [InlineData("1", false, false, false)]
    [InlineData("", false, false, false)]
    [InlineData(null, false, false, false)]
    public void GateAnswersTheThreeNamedQuestionsByVersion(string? version, bool schema, bool counters, bool timed)
    {
        var gate = SaveVersionGate.For(version);
        Assert.Equal(schema, gate.SupportsCurrentSchema);
        Assert.Equal(counters, gate.CounterDefaultsKnown);
        Assert.Equal(timed, gate.IsTimedVersion);
    }

    [Fact]
    public void FutureVersionsAreNeverAdmittedImplicitly()
    {
        // A positive list, not "current or later": admitting 1.8 must be an explicit gate change.
        Assert.False(SaveVersionGate.For("1.8.0").SupportsCurrentSchema);
        Assert.False(SaveVersionGate.For("1.8.0").CounterDefaultsKnown);
        Assert.False(SaveVersionGate.For("1.8.0").IsTimedVersion);
        Assert.True(SaveVersionGate.For("1.7.0").SupportsCurrentSchema);
    }

    [Theory]
    [InlineData("1.6.4566 rev607", "1.6")]
    [InlineData(" 1.7.5000 rev1275", "1.7")]
    [InlineData("1.5", "1.5")]
    [InlineData("1", null)]
    [InlineData("x.7.1", null)]
    [InlineData("1.x", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void MajorMinorKeepsItsParsingRules(string? version, string? expected)
        => Assert.Equal(expected, HistoryMetricReader.MajorMinor(version));

    [Fact]
    public void GateAndMajorMinorAgreeOnTheParsedVersion()
    {
        foreach (var version in new[] { "1.5.4104 rev435", "1.6.4566 rev607", "1.7.5000 rev1275", "1.8.1", "garbage", null })
            Assert.Equal(HistoryMetricReader.MajorMinor(version), SaveVersionGate.For(version).MajorMinor);
    }

    [Fact]
    public void UnknownGateAnswersFalseEverywhere()
    {
        Assert.Null(SaveVersionGate.Unknown.MajorMinor);
        Assert.False(SaveVersionGate.Unknown.SupportsCurrentSchema);
        Assert.False(SaveVersionGate.Unknown.CounterDefaultsKnown);
        Assert.False(SaveVersionGate.Unknown.IsTimedVersion);
        Assert.False(SaveVersionGate.For(null).SupportsCurrentSchema);
    }
}
