namespace RimworldStatistics.Core.Tests.Common;

/// <summary>
/// Common contract: registering an optional MOD statistic, and what happens when its MOD is missing, has another version,
/// lacks a Def, collides with another statistic, fails while starting, or is removed later. Holds for every descriptor
/// shape, so a new integration is covered by registering through the same path.
/// </summary>
[Trait("Category", "Contract")]
public sealed class StatisticRegistrationContractTests
{
    public static TheoryData<MetricObservationSource, ColonySelection, bool> Shapes()
    {
        var data = new TheoryData<MetricObservationSource, ColonySelection, bool>();
        foreach (var source in new[] { MetricObservationSource.Checkpoint, MetricObservationSource.SaveSnapshot })
            foreach (var selection in Enum.GetValues<ColonySelection>())
                foreach (var breakdown in new[] { false, true })
                    data.Add(source, selection, breakdown);
        return data;
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void EveryShapeRegistersAndItsRecordsOutliveTheRemovalOfItsMod(
        MetricObservationSource source, ColonySelection selection, bool breakdown)
    {
        var descriptor = TestShapes.CounterMetric(source: source, selection: selection, breakdown: breakdown);
        var registry = new ModMetricProviderRegistry();
        Assert.True(registry.Register(descriptor).Accepted);
        var definition = Assert.Single(registry.BuildCatalog());
        Assert.Equal(descriptor.StableKey, definition.Key);
        Assert.Equal(descriptor.AcquisitionId, definition.Acquisition);
        Assert.Equal(source, definition.Source);
        Assert.Equal(descriptor.Revision, definition.Revision);

        // Visible only when its unit (or every optional MOD output) is shown; showing it never needs the MOD.
        Assert.DoesNotContain(MetricRegistry.Compose([descriptor], new HashSet<string>()), d => d.Key == descriptor.StableKey);
        Assert.Contains(MetricRegistry.Compose([descriptor], new HashSet<string> { descriptor.AcquisitionId }), d => d.Key == descriptor.StableKey);

        // Recorded at tick 100 with the MOD; the save at tick 200 was made after the MOD was removed.
        var colony = new TestColony("a");
        var profile = new TestProfile().Played(colony, 200).Remember(descriptor)
            .Import(Observation(colony, descriptor, "with-mod", 100, measured: true),
                Observation(colony, descriptor, "without-mod", 200, measured: false))
            .Reopen();

        Assert.Equal(definition, Assert.Single(MetricRegistry.Compose(profile.Profile.ModMetricDescriptors,
            new HashSet<string> { MetricRegistry.OptionalModVisibilityId }), d => d.Key == descriptor.StableKey));
        var view = profile.View();
        var row = view.Row(colony, descriptor.StableKey);
        Assert.Equal(7, row.Value.Number);
        // The value is the last one recorded, not a reading of the latest save: never complete, never zero.
        Assert.Equal(MetricCoverage.Partial, row.Value.Coverage);
        if (breakdown)
        {
            Assert.Equal(new[] { TestShapes.DefKey("A"), TestShapes.DefKey("B") }, row.Breakdowns.Select(b => b.Breakdown));
            Assert.Equal(new double?[] { 4, 3 }, row.Breakdowns.Select(b => b.Value.Number));
        }
        Assert.Equal(7, view.Overall(descriptor.StableKey).Value.Number);
    }

    [Theory]
    // owner and dependency present, no bounds
    [InlineData("owner=;dep=", "", "", "Available", null)]
    [InlineData("", "", "", "MissingPackage", "example.owner")]
    [InlineData("owner=", "", "", "MissingPackage", "example.dependency")]
    // package ids are case-insensitive, as in the game's mod list
    [InlineData("OWNER=;DEP=", "", "", "Available", null)]
    // version bounds: the minimum is included, the maximum is not
    [InlineData("owner=;dep=1.2.0", "1.2.0", "2.0.0", "Available", null)]
    [InlineData("owner=;dep=1.1.9", "1.2.0", "2.0.0", "IncompatibleVersion", "example.dependency")]
    [InlineData("owner=;dep=1.9.9", "1.2.0", "2.0.0", "Available", null)]
    [InlineData("owner=;dep=2.0.0", "1.2.0", "2.0.0", "IncompatibleVersion", "example.dependency")]
    // a bound with no readable version fails closed
    [InlineData("owner=;dep=", "1.2.0", "", "MissingVersion", "example.dependency")]
    [InlineData("owner=;dep=1.2-beta", "1.2.0", "", "MissingVersion", "example.dependency")]
    public void AMissingOrOtherVersionOfARequiredModFailsClosed(
        string installed, string min, string maxExclusive, string expected, string? detail)
    {
        var descriptor = TestShapes.CounterMetric(requires: new ModPackageRequirement("example.dependency",
            min.Length == 0 ? null : min, maxExclusive.Length == 0 ? null : maxExclusive));
        var mods = installed.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(entry =>
        {
            var (name, version) = (entry[..entry.IndexOf('=')], entry[(entry.IndexOf('=') + 1)..]);
            var id = name.Equals("owner", StringComparison.OrdinalIgnoreCase) ? TestShapes.Owner : "example.dependency";
            return new ModPackageInfo(name == name.ToUpperInvariant() ? id.ToUpperInvariant() : id,
                version.Length == 0 ? null : version);
        }).ToArray();

        var result = ModProviderDiscovery.Resolve(descriptor, mods);

        Assert.Equal(Enum.Parse<ModProviderAvailability>(expected), result.State);
        Assert.Equal(detail, result.Detail, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMissingDefFailsClosedEvenWhenTheDefListIsUnknown()
    {
        var descriptor = TestShapes.CounterMetric() with { RequiredDefNames = ["ExampleDef"] };
        var installed = new[] { new ModPackageInfo(TestShapes.Owner, null) };

        Assert.Equal(ModProviderAvailability.MissingDef, ModProviderDiscovery.Resolve(descriptor, installed).State);
        Assert.Equal(ModProviderAvailability.MissingDef,
            ModProviderDiscovery.Resolve(descriptor, installed, new HashSet<string> { "OtherDef" }).State);
        Assert.Equal(ModProviderAvailability.Available,
            ModProviderDiscovery.Resolve(descriptor, installed, new HashSet<string> { "ExampleDef" }).State);
        // The package is checked first: without the MOD, the Def is not even looked for.
        Assert.Equal(ModProviderAvailability.MissingPackage,
            ModProviderDiscovery.Resolve(descriptor, [], new HashSet<string> { "ExampleDef" }).State);
    }

    [Fact]
    public void AKeyCanBeOwnedOnceWhateverItsCaseOrProvider()
    {
        var registry = new ModMetricProviderRegistry();
        var first = TestShapes.CounterMetric();
        Assert.True(registry.Register(first).Accepted);

        var sameKeys = new[]
        {
            first with { ProviderId = "another.provider" },
            first with { OwnerPackageId = "Example.Owner" },
            first with { FamilyId = "ACTIONS", MetricName = "Completed" },
            first with { Revision = 2, Source = MetricObservationSource.SaveSnapshot },
        };
        foreach (var other in sameKeys)
        {
            Assert.Equal(first.StableKey, other.StableKey);
            Assert.Equal(ModRegistrationFailure.DuplicateMetric, registry.Register(other).Failure);
        }
        Assert.Same(first, Assert.Single(registry.Descriptors));
    }

    [Fact]
    public void ABuiltInKeyCannotBeTakenByAModDescriptor()
    {
        // Every built-in output whose key has the optional-MOD form is reserved for its built-in definition.
        var taken = MetricRegistry.ImplementedCatalog.Select(d => d.Key).Where(k => k.StartsWith("mod.", StringComparison.Ordinal))
            .Select(k => k.Split('.')).Where(parts => parts.Length >= 4).ToArray();
        Assert.NotEmpty(taken);
        foreach (var parts in taken)
        {
            var descriptor = TestShapes.CounterMetric(owner: string.Join('.', parts[1..^2]), family: parts[^2]) with { MetricName = parts[^1] };
            Assert.True(descriptor.IsValid(), descriptor.StableKey);
            Assert.Equal(ModRegistrationFailure.DuplicateMetric, new ModMetricProviderRegistry().Register(descriptor).Failure);
        }
    }

    [Fact]
    public void AnOptionalFamilyCannotTakeTheNameOfABuiltInOrAnotherOptionalFamily()
    {
        var builtIn = LiveFamilies.All[0];
        var optional = TestShapes.FamilyOf(TestShapes.CounterMetric());
        var before = LiveFamilies.Active;

        Assert.Throws<ArgumentException>(() => LiveFamilies.SetOptionalFamilies(
            [new LiveFamily { Name = builtIn.Name, Revision = builtIn.Revision, Build = _ => { } }]));
        Assert.Throws<ArgumentException>(() => LiveFamilies.SetOptionalFamilies([optional, TestShapes.FamilyOf(TestShapes.CounterMetric())]));
        // A rejected set changes nothing that is recording.
        Assert.Equal(before.Select(f => f.Name), LiveFamilies.Active.Select(f => f.Name));
    }

    [Fact]
    public void AFailingInstallerOrReporterIsIsolatedToItsOwnUnit()
    {
        var guard = new ModProviderExecutionGuard();
        var reported = new List<string>();
        Assert.False(guard.TryRun("mod.a", () => throw new InvalidOperationException("broken hook"),
            (unit, _) => { reported.Add(unit); throw new InvalidOperationException("broken log"); }));
        Assert.False(guard.TryRun("mod.a", () => throw new InvalidOperationException("again"), (unit, _) => reported.Add(unit)));
        Assert.True(guard.TryRun("mod.b", () => { }));
        Assert.Equal(["mod.a"], reported);   // reported once, and a failing report did not reach the game

        var hooks = new DeferredHookRegistry();
        var installed = new List<string>();
        hooks.Defer("mod.a", () => throw new IndexOutOfRangeException());
        hooks.Defer("mod.b", () => { installed.Add("b"); return true; });
        Assert.False(hooks.Run("mod.a"));
        Assert.True(hooks.Run("mod.b"));
        Assert.Equal(["mod.b"], hooks.Installed);
        Assert.Equal(["b"], installed);
    }

    public static TheoryData<string> OrdinaryFailures() => new()
    {
        nameof(InvalidOperationException), nameof(IndexOutOfRangeException), nameof(InvalidCastException),
        nameof(FormatException), nameof(NotSupportedException), nameof(KeyNotFoundException), nameof(OverflowException),
    };

    [Theory]
    [MemberData(nameof(OrdinaryFailures))]
    public void AFamilyThatFailsToBuildItsValuesDropsOnlyItsOwnValues(string failure)
    {
        var healthy = TestShapes.CounterMetric("healthy");
        var broken = TestShapes.CounterMetric("broken");
        var families = new[]
        {
            TestShapes.FamilyOf(healthy),
            new LiveFamily { Name = broken.AcquisitionId, Revision = 1, Build = _ => throw Failure(failure) },
        };
        var game = GameSession.NewGame(new TestColony("a"), families);
        game.Record(healthy, 2);
        game.Record(broken, 5);

        var saved = game.Save(100, "c1");

        Assert.Equal(2, saved.Observation.Scalar(healthy.StableKey, healthy.Revision)!.IntegerValue);
        Assert.DoesNotContain(broken.StableKey, saved.Observation.Metrics.Keys);
        // The broken family's totals stay in the checkpoint for a fixed version to read.
        Assert.Contains(broken.AcquisitionId, CheckpointJson.Parse(saved.Json).Payload!.Families.Keys);
    }

    private static Exception Failure(string name) => name switch
    {
        nameof(InvalidOperationException) => new InvalidOperationException(),
        nameof(IndexOutOfRangeException) => new IndexOutOfRangeException(),
        nameof(InvalidCastException) => new InvalidCastException(),
        nameof(FormatException) => new FormatException(),
        nameof(NotSupportedException) => new NotSupportedException(),
        nameof(KeyNotFoundException) => new KeyNotFoundException(),
        nameof(OverflowException) => new OverflowException(),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    /// <summary>An observation of the descriptor's source; <paramref name="measured"/> false is a save made without the MOD.</summary>
    internal static MetricObservation Observation(TestColony colony, ModMetricDescriptor descriptor, string key, int tick, bool measured)
    {
        var observation = new MetricObservation
        {
            Key = key, ParentKey = descriptor.Source == MetricObservationSource.Checkpoint && tick > 100 ? "with-mod" : null,
            PayloadHash = key, Source = descriptor.Source, Identity = colony.Identity.Clone(), SaveTick = tick,
            ObservedUtc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc).AddSeconds(tick),
        };
        if (!measured)
            return observation;
        var coverage = descriptor.Source == MetricObservationSource.Checkpoint ? MetricCoverage.Complete : MetricCoverage.SnapshotOnly;
        var entry = MetricEntry.Of(descriptor.Revision, new MetricValue { IntegerValue = 7, Coverage = coverage, SinceTick = 0, ThroughTick = tick });
        if (descriptor.HasBreakdown)
        {
            entry.Values[TestShapes.DefKey("A")] = new MetricValue { IntegerValue = 4, Coverage = coverage, SinceTick = 0, ThroughTick = tick };
            entry.Values[TestShapes.DefKey("B")] = new MetricValue { IntegerValue = 3, Coverage = coverage, SinceTick = 0, ThroughTick = tick };
        }
        observation.Metrics[descriptor.StableKey] = entry;
        observation.EvidenceByAcquisition = new Dictionary<string, MetricEvidence> { [descriptor.AcquisitionId] = MetricEvidence.Experimental };
        return observation;
    }
}
