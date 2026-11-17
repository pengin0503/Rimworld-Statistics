namespace RimworldStatistics.Core.Tests;

/// <summary>#336: a provider's statistic never shows its key as its title or description.</summary>
public sealed class ProviderMetricTextTests
{
    private static MetricDefinition Sdk(string? labelKey = null, string? descriptionKey = null) => new ModMetricDescriptor
    {
        ProviderId = "pengin0503.rws.sdksample",
        OwnerPackageId = "pengin0503.rws.sdksample",
        FamilyId = "events",
        MetricName = "completed_runs",
        LabelKey = labelKey,
        DescriptionKey = descriptionKey,
    }.ToDefinition();

    // Like the game's language data: a key without a translation has none.
    private static string? Translate(string key) => key switch
    {
        "Sample_Label" => "Completed runs",
        "Sample_Desc" => "Runs the sample finished.",
        _ => null,
    };

    [Fact]
    public void AStatisticWithoutTranslationsShowsTheEndOfItsIdAndNoDescription()
    {
        var definition = Sdk();
        Assert.Equal("mod.pengin0503.rws.sdksample.events.completed_runs", definition.Key);
        Assert.Equal("completed runs", ProviderMetricText.Label(definition, Translate));
        Assert.Null(ProviderMetricText.Description(definition, Translate));
        // A lookup that echoes the key (as Translate() does for a missing key) is not a translation either.
        Assert.Null(ProviderMetricText.Description(definition, key => key));
        Assert.Equal("completed runs", ProviderMetricText.Label(definition, key => key));
    }

    [Fact]
    public void KeysWhoseTranslationsAreGoneAreNotShown()
    {
        // The MOD named its own keys, then was removed with its language files.
        var definition = Sdk("Missing_Label", "Missing_Desc");
        Assert.Equal("completed runs", ProviderMetricText.Label(definition, Translate));
        Assert.Null(ProviderMetricText.Description(definition, Translate));
        Assert.Null(ProviderMetricText.Description(definition, key => key));
        Assert.Null(ProviderMetricText.Description(definition, _ => "  "));
    }

    [Fact]
    public void TranslatedTextIsShownAsGiven()
    {
        var definition = Sdk("Sample_Label", "Sample_Desc");
        Assert.Equal("Completed runs", ProviderMetricText.Label(definition, Translate));
        Assert.Equal("Runs the sample finished.", ProviderMetricText.Description(definition, Translate));
    }
}
