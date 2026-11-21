using System.Text;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>Game IL/Def-referenced provisional 1.7 historical research recovery.</summary>
public sealed class HistoricalResearchStateReaderTests
{
    private static SaveReadResult Read(
        string version, string[]? modIds, params (string Def, string Progress)[] projects)
    {
        var source = new SaveXmlBuilder().Minimal().MetaMods(version, modIds).Build();
        var xml = XDocument.Parse(Encoding.UTF8.GetString(source));
        var pairs = new XElement("progress",
            new XElement("keys", projects.Select(p => new XElement("li", p.Def))),
            new XElement("values", projects.Select(p => new XElement("li", p.Progress))));
        xml.Root!.Element("game")!.Add(new XElement("researchManager", pairs));
        var bytes = Encoding.UTF8.GetBytes(xml.ToString(SaveOptions.DisableFormatting));
        using var stream = new MemoryStream(bytes);
        return new SaveReader().ReadDetailed(stream, "fixture.rws", bytes.Length, DateTime.UnixEpoch);
    }

    [Theory]
    [InlineData("1.7.4985 rev1269")]
    [InlineData("1.7.5000 rev1275")]
    public void BothProvisional17BuildsRecoverMatchingOfficialProjectCounts(string version)
    {
        var r = Read(version, new[] { "ludeon.rimworld", "ludeon.rimworld.biotech" },
            ("TreeSowing", "1000"), ("Deathrest", "1000"), ("PoweredArmor", "6000"),
            ("JumpPack", "500")); // Royalty not active: never included
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        var metrics = Assert.IsType<MetricObservation>(r.Metrics);
        var finished = metrics.Metrics[ResearchStateSnapshot.Finished].Values[""];
        Assert.Equal(3, finished.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, finished.Coverage); // source build compatibility is assumed
        var rate = metrics.Metrics[ResearchStateSnapshot.FinishedRate].Values[""];
        Assert.Equal(3d, rate.Numerator);
        Assert.True(rate.Denominator > 50);
        Assert.True(rate.Denominator < 122);
        Assert.Equal(MetricEvidence.Experimental, metrics.EvidenceByAcquisition!["research_state"]);
    }

    [Fact]
    public void UnknownModProjectAndChangedModSetNeverClaimCompleteCoverage()
    {
        var r = Read("1.7.5000 rev1275", new[] { "ludeon.rimworld", "custom.mod" },
            ("PoweredArmor", "6000"), ("OtherModResearch", "700000"));
        var count = r.Metrics!.Metrics[ResearchStateSnapshot.Finished].Values[""];
        Assert.Equal(1, count.IntegerValue);  // unknown project NOT asserted complete
        Assert.Equal(MetricCoverage.Partial, count.Coverage);
    }

    [Fact]
    public void DuplicateAndInvalidResearchProgressCannotInflateCounts()
    {
        var r = Read("1.7.5000 rev1275", new[] { "ludeon.rimworld" },
            ("TreeSowing", "1000"), ("TreeSowing", "100000"),
            ("PoweredArmor", "NaN"), ("MicroelectronicsBasics", "-100"));
        var metric = r.Metrics!.Metrics[ResearchStateSnapshot.Finished].Values[""];
        Assert.Equal(MetricCoverage.Partial, metric.Coverage);
        Assert.Equal(0, metric.IntegerValue); // ambiguous repeated key excluded, bad values ignored
    }

    [Theory]
    [InlineData("1.5.4104 rev435")]
    [InlineData("1.6.4566 rev607")]
    public void NoUnsupportedOlderBuildBackfill(string version)
    {
        var r = Read(version, new[] { "ludeon.rimworld" }, ("TreeSowing", "1000"));
        Assert.False(r.Metrics!.Metrics.ContainsKey(ResearchStateSnapshot.Finished));
    }

    [Fact]
    public void NoResearchManagerNeverInventsFinishedZero()
    {
        var b = new SaveXmlBuilder().Minimal().MetaMods("1.7.5000 rev1275", "ludeon.rimworld");
        var r = NumericSaveReaderTests.ReadDetailed(b);
        Assert.False(r.Metrics!.Metrics.ContainsKey(ResearchStateSnapshot.Finished));
    }

    [Fact]
    public void ReferenceIncludesAll122ResolvedOfficialResearchDefs()
        => Assert.Equal(122, HistoricalResearchStateReader.ReferenceCount);
}
