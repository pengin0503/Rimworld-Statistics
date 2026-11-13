namespace RimworldStatistics.Core.Tests;

public sealed class ExperimentalAcquisitionPolicyTests
{
    [Fact]
    public void DefaultsToOff_AndDoesNotOpenUnknownOrDerivedUnits()
    {
        var selected = new ExperimentalSelection(false, new[] { "research", "work" });
        Assert.Empty(ExperimentalAcquisitionPolicy.Resolve(selected, AcquisitionUnits.All));

        var enabled = new ExperimentalSelection(true, new[] { "research", "ending_party", "made_up", "research" });
        Assert.Equal(new[] { "research" }, ExperimentalAcquisitionPolicy.Resolve(enabled, AcquisitionUnits.All).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void OnlyExactExplicitIdsAreOpened_NewCatalogEntriesDoNotAutoEnable()
    {
        var selected = new ExperimentalSelection(true, new[] { "research", "RESEARCH", "work" });
        var later = AcquisitionUnits.All.Concat(new[] { new AcquisitionUnit("new_kind", AcquisitionKind.Family, AcquisitionStatus.Unverified) }).ToArray();
        var open = ExperimentalAcquisitionPolicy.Resolve(selected, later);
        Assert.True(open.SetEquals(new[] { "research", "work" }));
        Assert.DoesNotContain("new_kind", open);
    }

    [Fact]
    public void VerifiedUnitsAreIndependentOfTheExperimentalToggle()
    {
        var units = new[]
        {
            new AcquisitionUnit("approved", AcquisitionKind.Family, AcquisitionStatus.Verified),
            new AcquisitionUnit("pending", AcquisitionKind.Snapshot, AcquisitionStatus.Unverified),
            new AcquisitionUnit("derived", AcquisitionKind.Derived, AcquisitionStatus.Verified),
        };
        Assert.Equal(new[] { "approved" },
            ExperimentalAcquisitionPolicy.Resolve(new ExperimentalSelection(false, new[] { "pending" }), units).ToArray());
        Assert.True(ExperimentalAcquisitionPolicy.Resolve(new ExperimentalSelection(true, new[] { "pending" }), units)
            .SetEquals(new[] { "approved", "pending" }));
    }

    [Fact]
    public void ResolverDetachesFromMutableUserSettings()
    {
        var ids = new List<string> { "research" };
        var resolved = ExperimentalAcquisitionPolicy.Resolve(new ExperimentalSelection(true, ids), AcquisitionUnits.All);
        ids.Clear();
        ids.Add("work");
        Assert.Contains("research", resolved);
        Assert.DoesNotContain("work", resolved);
    }

    [Fact]
    public void AutoAllTokenOptsInEveryNonDerivedUnitIncludingFutureOptionalFamilies()
    {
        var units = AcquisitionUnits.All.Concat(new[]
        {
            new AcquisitionUnit("mod.example.future", AcquisitionKind.Family, AcquisitionStatus.Unverified),
            new AcquisitionUnit("mod.example.derived", AcquisitionKind.Derived, AcquisitionStatus.Unverified),
        }).ToArray();
        var selected = ExperimentalAcquisitionPolicy.Resolve(
            new ExperimentalSelection(true, new[] { ExperimentalAcquisitionPolicy.AllUnitsToken }), units);
        Assert.Contains("mod.example.future", selected);
        Assert.Contains("research", selected);
        Assert.DoesNotContain("mod.example.derived", selected);
        Assert.DoesNotContain("mod.example.future", ExperimentalAcquisitionPolicy.Resolve(
            new ExperimentalSelection(false, new[] { ExperimentalAcquisitionPolicy.AllUnitsToken }), units));
        Assert.DoesNotContain("mod.example.future", ExperimentalAcquisitionPolicy.Resolve(
            new ExperimentalSelection(true, Array.Empty<string>()), units));
    }

}
