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
        Assert.Equal("Hospitality", label);
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
