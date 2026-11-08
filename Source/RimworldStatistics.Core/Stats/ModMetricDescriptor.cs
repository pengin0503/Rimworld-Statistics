using System.Text.Json;
using System.Text.Json.Serialization;

namespace RimworldStatistics.Core;

/// <summary>One optional MOD dependency. Unknown versions fail closed when a version bound is supplied.</summary>
public sealed record ModPackageRequirement(string PackageId, string? MinVersion = null, string? MaxVersionExclusive = null);
public sealed record ModPackageInfo(string PackageId, string? Version);

/// <summary>Stable description persisted into the profile so a removed MOD's values retain their meaning.</summary>
public sealed record ModMetricDescriptor
{
    public string ProviderId { get; init; } = "";
    public string OwnerPackageId { get; init; } = "";
    public string FamilyId { get; init; } = "";
    public string MetricName { get; init; } = "";
    public int Revision { get; init; } = 1;
    public MetricObservationSource Source { get; init; } = MetricObservationSource.SaveSnapshot;
    public MetricCategory Category { get; init; } = MetricCategory.Colony;
    public MetricUnit Unit { get; init; } = MetricUnit.Count;
    public OverallAggregation Overall { get; init; } = OverallAggregation.Sum;
    public ColonySelection Selection { get; init; } = ColonySelection.Representative;
    public bool HasBreakdown { get; init; }
    public string? RequiredDlc { get; init; }
    public string? LabelKey { get; init; }
    public string? DescriptionKey { get; init; }
    public IReadOnlyList<ModPackageRequirement> RequiredPackages { get; init; } = Array.Empty<ModPackageRequirement>();
    public IReadOnlyList<string> RequiredDefNames { get; init; } = Array.Empty<string>();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }

    [JsonIgnore]
    public string StableKey => "mod." + OwnerPackageId.ToLowerInvariant() + "." +
        FamilyId.ToLowerInvariant() + "." + MetricName.ToLowerInvariant();

    [JsonIgnore]
    public string AcquisitionId => "mod." + OwnerPackageId.ToLowerInvariant() + "." + FamilyId.ToLowerInvariant();

    public bool IsValid()
        => ValidId(ProviderId, allowDot: true) && ValidId(OwnerPackageId, allowDot: true)
            && ValidId(FamilyId, allowDot: false) && ValidId(MetricName, allowDot: false)
            && Revision >= 1 && Revision <= 9999
            && Source is MetricObservationSource.SaveSnapshot or MetricObservationSource.Checkpoint
            && Enum.IsDefined(Category) && Enum.IsDefined(Unit) && Enum.IsDefined(Overall)
            && Overall != OverallAggregation.Ranking && Enum.IsDefined(Selection)
            && (RequiredPackages ?? Array.Empty<ModPackageRequirement>()).All(r =>
                r is not null && ValidId(r.PackageId, allowDot: true) &&
                ValidVersion(r.MinVersion) && ValidVersion(r.MaxVersionExclusive))
            && (RequiredDefNames ?? Array.Empty<string>()).All(s => ValidId(s, allowDot: true));

    private static bool ValidVersion(string? value) => value is null || Version.TryParse(value, out _);

    private static bool ValidId(string? value, bool allowDot)
        => !string.IsNullOrEmpty(value) && value.Length <= 128
            && value == value.Trim() && char.IsAsciiLetterOrDigit(value[0])
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || allowDot && c == '.');

    public MetricDefinition ToDefinition()
    {
        if (!IsValid())
            throw new ArgumentException("Invalid MOD statistics descriptor", nameof(ModMetricDescriptor));
        return new MetricDefinition
        {
            Key = StableKey, StoredKey = StableKey, LedgerIds = ProviderId, Category = Category, Unit = Unit,
            Provenance = Source == MetricObservationSource.Checkpoint ? MetricProvenance.LiveAggregate : MetricProvenance.SaveSnapshot,
            Source = Source, Selection = Selection, Overall = Overall, HasBreakdown = HasBreakdown,
            RequiredDlc = RequiredDlc, Acquisition = AcquisitionId,
            ProviderId = ProviderId, OwnerPackageId = OwnerPackageId,
            DefinitionRevision = Revision,
            LabelKey = string.IsNullOrWhiteSpace(LabelKey) ? StableKey : LabelKey,
            DescriptionKey = string.IsNullOrWhiteSpace(DescriptionKey) ? StableKey : DescriptionKey,
        };
    }
}

/// <summary>What was actually installed when an observation was captured, not a claim of verification.</summary>
public sealed record ModProviderCapture(string OwnerPackageId, string? OwnerVersion, string ProviderVersion)
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; init; }
}
