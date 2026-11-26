using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class DiagnosticStoppedNamesTests
{
    private static DiagnosticSummaryInput Input(IReadOnlyList<string>? units = null, IReadOnlyList<string>? patches = null) => new()
    {
        GeneratedUtc = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc),
        FailedProviderUnitNames = units ?? [],
        FailedPatchClassNames = patches ?? [],
    };

    [Fact]
    public void NamesAppearSortedUnderTheirHeaders()
    {
        var text = DiagnosticSummaryBuilder.FormatText(Input(
            ["mod.zeta.unit", "mod.alpha.unit", "mod.alpha.unit"],
            ["RWS.Patches.Zeta", "RWS.Patches.Alpha"]));

        Assert.Contains("[stopped_items]", text);
        Assert.Contains("failed_provider_units: mod.alpha.unit, mod.zeta.unit", text);
        Assert.Contains("failed_patch_classes: RWS.Patches.Alpha, RWS.Patches.Zeta", text);
        Assert.Contains("optional_units: total=", text);   // the counts are still there
    }

    [Fact]
    public void AtMostFiftyNamesAreRendered()
    {
        var units = Enumerable.Range(0, 60).Select(i => $"mod.unit.{i:00}").ToArray();
        var text = DiagnosticSummaryBuilder.FormatText(Input(units));
        Assert.Contains("mod.unit.00", text);
        Assert.Contains("mod.unit.49", text);
        Assert.DoesNotContain("mod.unit.50", text);
        Assert.DoesNotContain("mod.unit.59", text);
    }

    [Fact]
    public void AbsentListsKeepThePreviousOutputShape()
    {
        var without = DiagnosticSummaryBuilder.FormatText(Input());
        var explicitlyEmpty = DiagnosticSummaryBuilder.FormatText(new DiagnosticSummaryInput
        {
            GeneratedUtc = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc),
            FailedProviderUnitNames = [],
            FailedPatchClassNames = [],
        });

        Assert.Equal(without, explicitlyEmpty);
        Assert.DoesNotContain("[stopped_items]", without);
        Assert.DoesNotContain("failed_provider_units", without);
        Assert.DoesNotContain("failed_patch_classes", without);
    }

    [Fact]
    public void SuppliedPathsNeverReachTheSummary()
    {
        using var root = new TempDir();
        var dataDirectory = Path.Combine(root.Path, "RimworldStatistics");
        var profilePath = Path.Combine(dataDirectory, "profile.json");

        var input = Input(
            [dataDirectory, profilePath, "mod.good.unit"],
            [profilePath, "/home/bob_secret/.config/profile.json", @"C:\Users\AliceSecret\profile.json"])
            with
        {
            // Path-like values supplied through any other input field must be rejected too.
            GameVersion = dataDirectory,
            ModVersion = profilePath,
            LastSaveErrorType = profilePath,
        };
        var text = DiagnosticSummaryBuilder.FormatText(input);

        Assert.DoesNotContain(root.Path, text);
        Assert.DoesNotContain(dataDirectory, text);
        Assert.DoesNotContain(profilePath, text);
        Assert.DoesNotContain("bob_secret", text);
        Assert.DoesNotContain("AliceSecret", text);
        Assert.DoesNotContain(@"C:\", text);
        Assert.DoesNotContain("/home/", text);
        Assert.Contains("mod.good.unit", text);   // a real identifier still gets through
    }

    [Fact]
    public void TheDirectoryOfTheWrittenSummaryIsNotInTheText()
    {
        using var root = new TempDir();
        var file = DiagnosticSummaryBuilder.WriteFile(Input(["mod.unit.ok"]), root.Path);
        var text = File.ReadAllText(file.FilePath);
        Assert.DoesNotContain(root.Path, text);
        Assert.DoesNotContain("rws-diagnostic-summary", text);
    }
}
