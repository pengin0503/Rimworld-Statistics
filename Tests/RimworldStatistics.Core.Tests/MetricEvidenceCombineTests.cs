namespace RimworldStatistics.Core.Tests;

public sealed class MetricEvidenceCombineTests
{
    private static readonly MetricEvidence[] All = Enum.GetValues<MetricEvidence>();

    /// <summary>The implementation shipped through v1.5.0, kept here as the oracle.</summary>
    private static MetricEvidence Oracle(IEnumerable<MetricEvidence> values)
    {
        var seen = new HashSet<MetricEvidence>(values);
        if (seen.Count == 0 || seen.Contains(MetricEvidence.UnknownLegacy))
            return MetricEvidence.UnknownLegacy;
        return seen.Count == 1 ? seen.Single() : MetricEvidence.Mixed;
    }

    [Fact]
    public void Sequence_MatchesTheOriginalForEveryInputUpToLengthThree()
    {
        Assert.Equal(Oracle(Array.Empty<MetricEvidence>()), MetricEvidencePolicy.Combine(Array.Empty<MetricEvidence>()));
        foreach (var a in All)
        {
            Assert.Equal(Oracle(new[] { a }), MetricEvidencePolicy.Combine(new[] { a }));
            foreach (var b in All)
            {
                Assert.Equal(Oracle(new[] { a, b }), MetricEvidencePolicy.Combine(new[] { a, b }));
                foreach (var c in All)
                    Assert.Equal(Oracle(new[] { a, b, c }), MetricEvidencePolicy.Combine(new[] { a, b, c }));
            }
        }
    }

    [Fact]
    public void Pair_MatchesTheSequenceForm()
    {
        foreach (var a in All)
            foreach (var b in All)
                Assert.Equal(Oracle(new[] { a, b }), MetricEvidencePolicy.Combine(a, b));
    }

    [Fact]
    public void Pair_DoesNotAllocate()
    {
        _ = MetricEvidencePolicy.Combine(MetricEvidence.Verified, MetricEvidence.Experimental);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
            _ = MetricEvidencePolicy.Combine(MetricEvidence.Verified, MetricEvidence.Experimental);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
