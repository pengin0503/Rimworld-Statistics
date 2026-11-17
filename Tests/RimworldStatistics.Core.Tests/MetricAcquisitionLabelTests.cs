namespace RimworldStatistics.Core.Tests;

public sealed class MetricAcquisitionLabelTests
{
    private static MetricDefinition BaseDef => MetricRegistry.Find(MetricKeys.QuestSuccess)!;

    private static MetricStatus Status(
        MetricDefinition def,
        int measured = 1,
        int targets = 2,
        UnitRecordingState recording = UnitRecordingState.Recording) => new(
        def,
        targets == 0 ? MetricAvailability.NoApplicableColonies
            : measured == 0 ? MetricAvailability.NoMeasuredColonies
            : measured < targets ? MetricAvailability.SomeMeasuredColonies
            : MetricAvailability.AllMeasuredColonies,
        false,
        def.ProviderId is not null || (def.Acquisition is not null && !def.Published)
            ? MetricVerification.Experimental
            : MetricVerification.Standard,
        recording,
        measured,
        targets,
        measured < targets ? new[] { "c2" } : Array.Empty<string>());

    private static string FakeTranslate(string key) => key switch
    {
        "RWS_Status_ExternalMod" => "External mod",
        _ => $"T[{key}]",
    };

    private static string FakeExternalModCategory(string cat) => $"External mod ({cat})";

    [Fact]
    public void EveryBuiltInAcquisitionUnitHasUniqueLabelKey()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var unit in AcquisitionUnits.All)
        {
            var key = MetricAcquisitionLabels.UnitLabelKey(unit.Id);
            Assert.NotNull(key);
            Assert.StartsWith("RWS_AcqUnit_", key, StringComparison.Ordinal);
            Assert.True(keys.Add(key!), $"Duplicate key for unit '{unit.Id}': {key}");
        }
    }

    [Fact]
    public void EveryProvenanceHasTranslatedGroupKey()
    {
        foreach (var provenance in Enum.GetValues<MetricProvenance>())
        {
            var def = BaseDef with { Acquisition = null, Provenance = provenance };
            var key = MetricAcquisitionLabels.ProvenanceGroupLabelKey(def.Provenance);
            Assert.StartsWith("RWS_AcqUnit_", key, StringComparison.Ordinal);
            Assert.NotEqual(provenance.ToString(), key);
        }
    }

    [Fact]
    public void UntranslatedExternalSdkDoesNotExposeRawIdsOrMergeSameLabelGroups()
    {
        var sdkA = BaseDef with
        {
            Key = "mod.alpha.unknown.events.a",
            StoredKey = "mod.alpha.unknown.events.a",
            Acquisition = "mod.alpha.unknown.events",
            ProviderId = "alpha.provider",
            OwnerPackageId = "alpha.secret.mod",
            Category = MetricCategory.Economy,
        };
        var sdkB = BaseDef with
        {
            Key = "mod.beta.unknown.events.b",
            StoredKey = "mod.beta.unknown.events.b",
            Acquisition = "mod.beta.unknown.events",
            ProviderId = "beta.provider",
            OwnerPackageId = "beta.secret.mod",
            Category = MetricCategory.Economy,
        };
        var emptyOwnerSdk = BaseDef with
        {
            Key = "mod.empty.unknown.c",
            StoredKey = "mod.empty.unknown.c",
            Acquisition = "mod.empty.unknown",
            ProviderId = "empty.provider",
            OwnerPackageId = "   ",
            Category = MetricCategory.Economy,
        };

        var groups = MetricAcquisitionLabels.BuildGroups(
            new[]
            {
                Status(sdkB, measured: 0, targets: 2),
                Status(sdkA, measured: 2, targets: 2),
                Status(emptyOwnerSdk, measured: 1, targets: 2),
            },
            modId => modId, // Simulate fallback returning raw package id
            FakeTranslate,
            c => $"Cat[{c}]",
            FakeExternalModCategory,
            StringComparer.Ordinal);

        Assert.Equal(3, groups.Count);
        foreach (var group in groups)
        {
            Assert.DoesNotContain("alpha.secret.mod", group.Label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("beta.secret.mod", group.Label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mod.alpha.unknown", group.Label, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("mod.beta.unknown", group.Label, StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(group.Label));
        }

        // Distinct GroupIds remain separate even when their display labels are identical
        Assert.Equal(
            new[] { "mod.alpha.unknown.events", "mod.beta.unknown.events", "mod.empty.unknown" },
            groups.Select(g => g.GroupId).ToArray());
        Assert.All(groups, g => Assert.Equal("External mod (Cat[Economy])", g.Label));
    }

    [Fact]
    public void ActiveModTitleIsPreferredOverFallbackWithoutExposingPackageId()
    {
        var modDef = BaseDef with
        {
            Key = "mod.custom.coolmod.kills",
            StoredKey = "mod.custom.coolmod.kills",
            Acquisition = "mod.custom.coolmod.combat",
            ProviderId = "custom.provider",
            OwnerPackageId = "custom.author.coolmod",
        };
        var label = MetricAcquisitionLabels.ResolveGroupLabel(
            modDef,
            pkg => pkg == "custom.author.coolmod" ? "Cool Combat Mod" : null,
            FakeTranslate,
            c => c.ToString(),
            FakeExternalModCategory);
        Assert.Equal("Cool Combat Mod", label);
    }

    [Fact]
    public void KnownModTitleIsUsedWhenModIsNotCurrentlyLoaded()
    {
        var modDef = BaseDef with
        {
            Key = "mod.orion.hospitality.guest_spend",
            StoredKey = "mod.orion.hospitality.guest_spend",
            Acquisition = "mod.orion.hospitality.purchases",
            ProviderId = "rws.hospitality",
            OwnerPackageId = "Orion.Hospitality",
        };
        var label = MetricAcquisitionLabels.ResolveGroupLabel(
            modDef,
            _ => null,
            FakeTranslate,
            c => c.ToString(),
            FakeExternalModCategory);
        Assert.Equal("Hospitality (Continued)", label);
    }

    [Theory]
    [InlineData("vanillaracesexpanded.saurid", "Vanilla Races Expanded - Saurid")]
    [InlineData("vanillaracesexpanded.phytokin", "Vanilla Races Expanded - Phytokin")]
    [InlineData("Dubwise.Rimefeller", "Rimefeller")]
    [InlineData("VanillaExpanded.VExplorationE", "Vanilla Landmarks Expanded")]
    public void ABuiltInProviderKeepsItsNameWhenLoadedRemovedOrLoadedUnderAnotherName(string packageId, string name)
    {
        // #303: the MOD is loaded, removed, or loaded in another MOD set whose title differs (another release or language).
        Assert.Equal(name, MetricAcquisitionLabels.SafeModDisplayName(packageId, _ => name));
        Assert.Equal(name, MetricAcquisitionLabels.SafeModDisplayName(packageId, _ => null));
        Assert.Equal(name, MetricAcquisitionLabels.SafeModDisplayName(packageId.ToUpperInvariant(), _ => name + " [1.6]"));
    }

    [Theory]
    [InlineData("English")]
    [InlineData("Japanese")]
    public void EveryBuiltInUnitIsNamedByItsTranslationNotItsId(string language)
    {
        // #320: the MOD settings and the Coverage tab name a unit with the same function.
        var strings = TranslationCopyRefinementTests.LoadLanguage(TranslationCopyRefinementTests.LanguagesRoot(), language);
        var owners = BuiltInModMetricCatalog.Definitions.Where(d => d.Acquisition is not null && d.OwnerPackageId is not null)
            .GroupBy(d => d.Acquisition!).ToDictionary(g => g.Key, g => g.First().OwnerPackageId);
        foreach (var unit in AcquisitionUnits.All)
        {
            var label = MetricAcquisitionLabels.UnitLabel(unit.Id, owners.GetValueOrDefault(unit.Id), _ => null,
                key => strings.GetValueOrDefault(key) ?? "");
            Assert.False(string.IsNullOrWhiteSpace(label), unit.Id);
            Assert.NotEqual(unit.Id, label);
            if (unit.Id.IndexOfAny(['_', '.']) >= 0)
                Assert.DoesNotContain(unit.Id, label!, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AModUnitWithoutItsOwnNameIsNamedByItsModAndAnUnknownOneByNothing()
    {
        Assert.Equal("Vanilla Races Expanded - Saurid", MetricAcquisitionLabels.UnitLabel(
            "mod.vanillaracesexpanded.saurid.births", "vanillaracesexpanded.saurid", _ => null, FakeTranslate));
        Assert.Equal("Sample statistics", MetricAcquisitionLabels.UnitLabel(
            "mod.pengin0503.rws.sdksample.events", "pengin0503.rws.sdksample", _ => "Sample statistics", FakeTranslate));
        Assert.Null(MetricAcquisitionLabels.UnitLabel(
            "mod.unknown.publisher.events", "unknown.publisher", _ => null, FakeTranslate));
        // A unit name that translates to nothing falls back to the MOD's name.
        Assert.Equal("Dubs Bad Hygiene", MetricAcquisitionLabels.UnitLabel(
            DbhStatistics.FixturesUnit, DbhStatistics.PackageId, _ => null, _ => " "));
    }

    [Fact]
    public void UnitsThatShareANameAreToldApartByTheirFirstStatistic()
    {
        var labels = MetricAcquisitionLabels.Distinguish(
            ["Hospitality (Continued)", "Research", "Hospitality (Continued)", "Shared", "Shared"],
            i => i switch
            {
                0 => ["Guest spending", "Bed income"],
                2 => ["Guests recruited"],
                3 => [" ", ""],
                _ => ["Ignored"],
            },
            StringComparer.Ordinal);
        Assert.Equal(
            ["Hospitality (Continued) · Bed income", "Research", "Hospitality (Continued) · Guests recruited", "Shared", "Shared · Ignored"],
            labels);
    }

    [Fact]
    public void EveryCatalogNameIsShownAsGivenAndNamesOneProvider()
    {
        var names = ModProviderNames.PackageIds.Select(id => ModProviderNames.Known(id)!).ToArray();
        Assert.All(names, name => Assert.False(string.IsNullOrWhiteSpace(name) || name != name.Trim()));
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ModProviderNames.NotOnSupportedList, id => Assert.NotNull(ModProviderNames.Known(id)));
    }

    [Fact]
    public void ModDisplayNamesUseTheCatalogThenTheLoadedNameAndNeverEchoUnknownIds()
    {
        Assert.Equal("Hospitality (Continued)", MetricAcquisitionLabels.SafeModDisplayName(
            "orion.hospitality", _ => "Active Hospitality"));
        Assert.Equal("Sample statistics", MetricAcquisitionLabels.SafeModDisplayName(
            "pengin0503.rws.sdksample", _ => " Sample statistics "));
        Assert.Null(MetricAcquisitionLabels.SafeModDisplayName("pengin0503.rws.sdksample", _ => null));
        Assert.Null(MetricAcquisitionLabels.SafeModDisplayName(
            "unknown.publisher.hiddenid", id => id.ToUpperInvariant()));
        Assert.Null(MetricAcquisitionLabels.SafeModDisplayName(" ", _ => "Anything"));
    }

    [Fact]
    public void CoverageGroupShowsMissingOrMixedStateWithoutClaimingCompletion()
    {
        var statuses = new[]
        {
            Status(BaseDef with { Key = "test.one", Acquisition = "test" }, measured: 0, targets: 2),
            Status(BaseDef with { Key = "test.two", Acquisition = "test" }, measured: 2, targets: 2),
        };
        IReadOnlyList<CoverageGroup> Build(IReadOnlyList<MetricStatus> entries)
            => MetricAcquisitionLabels.BuildGroups(entries, _ => null, FakeTranslate,
                c => c.ToString(), FakeExternalModCategory, StringComparer.Ordinal);
        var mixed = Assert.Single(Build(statuses));
        Assert.Equal(MetricVisualKind.Partial, mixed.VisualKind);
        Assert.Equal(MetricVisualKind.Missing, Assert.Single(Build([statuses[0]])).VisualKind);
        // These test definitions are unpublished acquisition metrics, so a fully
        // observed member is Experimental (not Complete) by existing policy.
        Assert.Equal(MetricVisualKind.Experimental, Assert.Single(Build([statuses[1]])).VisualKind);
    }

    [Fact]
    public void EmptyStatusesReturnsEmptyGroups()
    {
        var groups = MetricAcquisitionLabels.BuildGroups(
            Array.Empty<MetricStatus>(),
            _ => null,
            FakeTranslate,
            c => c.ToString(),
            FakeExternalModCategory,
            StringComparer.Ordinal);
        Assert.Empty(groups);
    }

    [Fact]
    public void GroupsStartCollapsedAndPreserveUserExpandedAcrossEmptyAndActiveSearches()
    {
        var userExpanded = new HashSet<string>(StringComparer.Ordinal);
        Assert.False(MetricAcquisitionLabels.IsGroupExpanded("trade", userExpanded, ""));
        Assert.False(MetricAcquisitionLabels.IsGroupExpanded("trade", userExpanded, "   "));

        // Search temporarily expands without mutating userExpanded
        Assert.True(MetricAcquisitionLabels.IsGroupExpanded("trade", userExpanded, "silver"));
        Assert.Empty(userExpanded);

        // User toggle persists when search clears
        userExpanded.Add("research");
        Assert.True(MetricAcquisitionLabels.IsGroupExpanded("research", userExpanded, ""));
        Assert.False(MetricAcquisitionLabels.IsGroupExpanded("trade", userExpanded, ""));
    }
}
