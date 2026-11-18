namespace RimworldStatistics.Core.Tests;

public sealed class ModMetricProviderRegistryTests
{
    private static ModMetricDescriptor Metric(string family = "recruitment", string metric = "arrivals") => new()
    {
        ProviderId = "pengin.statistics.recruitment", OwnerPackageId = "pengin0503.recruitment",
        FamilyId = family, MetricName = metric, Revision = 1, Source = MetricObservationSource.Checkpoint,
        Category = MetricCategory.Population, Unit = MetricUnit.Count, Overall = OverallAggregation.Sum,
        RequiredPackages = new[] { new ModPackageRequirement("pengin0503.recruitment", "1.0.0") },
    };

    [Fact]
    public void CanonicalIdIsNamespaceStableWithoutUsingHashes()
    {
        var item = Metric();
        Assert.Equal("mod.pengin0503.recruitment.recruitment.arrivals", item.StableKey);
        Assert.Equal("mod.pengin0503.recruitment.recruitment", item.AcquisitionId);
        Assert.Equal(item.StableKey, item.ToDefinition().Key);
        Assert.Equal(1, item.ToDefinition().Revision);
        Assert.Equal(MetricObservationSource.Checkpoint, item.ToDefinition().Source);
        Assert.Equal("mod.pengin0503.recruitment.other.arrivals", Metric("other").StableKey);
    }

    [Fact]
    public void InvalidIdsAndDuplicateRegistrationAreRejectedWithoutMutatingPriorDefinitions()
    {
        var registry = new ModMetricProviderRegistry();
        Assert.True(registry.Register(Metric()).Accepted);
        Assert.Equal(ModRegistrationFailure.DuplicateMetric, registry.Register(Metric()).Failure);
        Assert.Equal(ModRegistrationFailure.DuplicateMetric,
            registry.Register(Metric() with { ProviderId = "another.provider", Revision = 2 }).Failure);
        Assert.Equal(ModRegistrationFailure.InvalidDefinition,
            registry.Register(Metric() with { MetricName = " with space" }).Failure);
        Assert.Equal(ModRegistrationFailure.InvalidDefinition,
            registry.Register(Metric() with { OwnerPackageId = "  bad.mod" }).Failure);
        Assert.Equal(ModRegistrationFailure.InvalidDefinition,
            registry.Register(Metric() with { Revision = 0 }).Failure);
        Assert.Single(registry.BuildCatalog());
    }

    [Fact]
    public void RegisteredCatalogIsStableIndependentOfRegistrationOrder()
    {
        var beforeCount = MetricRegistry.Catalog.Count;
        var a = new[] { Metric("z"), Metric("a"), Metric("b") };
        var left = new ModMetricProviderRegistry();
        var right = new ModMetricProviderRegistry();
        foreach (var d in a) Assert.True(left.Register(d).Accepted);
        foreach (var d in a.Reverse()) Assert.True(right.Register(d).Accepted);
        Assert.Equal(3, left.BuildCatalog().Count);
        Assert.Equal(left.BuildCatalog().Select(x => x.Key), right.BuildCatalog().Select(x => x.Key));
        Assert.All(left.BuildCatalog(), d => Assert.Equal(1, d.Revision));
        Assert.Equal(beforeCount, MetricRegistry.Catalog.Count);
    }
}
