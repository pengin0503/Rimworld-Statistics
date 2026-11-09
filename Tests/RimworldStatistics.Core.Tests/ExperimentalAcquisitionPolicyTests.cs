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
    public void AuditedBuiltInAdapterOpensOnlyWhenOwnerInstalledWithoutExperimentalOptIn()
    {
        var descriptor = new ModMetricDescriptor
        {
            OwnerPackageId = "Orion.Hospitality", ProviderId = "rws.hospitality",
            FamilyId = "guests", MetricName = "present",
            Source = MetricObservationSource.SaveSnapshot,
            Category = MetricCategory.Social, Unit = MetricUnit.Count, Overall = OverallAggregation.Sum,
        };
        var automatic = new[] { descriptor };
        var present = ExperimentalAcquisitionPolicy.ResolveAutoAdapters(automatic,
            new[] { new ModPackageInfo("ORION.HOSPITALITY", null) });
        Assert.Contains(descriptor.AcquisitionId, present);
        Assert.Empty(ExperimentalAcquisitionPolicy.ResolveAutoAdapters(automatic, Array.Empty<ModPackageInfo>()));
        Assert.Empty(ExperimentalAcquisitionPolicy.ResolveAutoAdapters(new[]
        {
            descriptor with { RequiredPackages = new[] { new ModPackageRequirement("Orion.Hospitality", "1.0.0") } },
        }, new[] { new ModPackageInfo("orion.hospitality", null) }));
    }
}
