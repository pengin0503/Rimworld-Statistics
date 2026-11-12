namespace RimworldStatistics.Core;

public enum ModRegistrationFailure
{
    None,
    InvalidDefinition,
    DuplicateMetric,
}

public sealed record ModRegistrationResult(bool Accepted, ModRegistrationFailure Failure);

/// <summary>Deterministic in-process catalog; rejects duplicate owners and existing vanilla keys.</summary>
public sealed class ModMetricProviderRegistry
{
    private readonly Dictionary<string, ModMetricDescriptor> _byKey = new(StringComparer.Ordinal);
    private readonly IReadOnlySet<string> _reserved;

    public ModMetricProviderRegistry(IEnumerable<string>? reserved = null)
    {
        _reserved = (reserved ?? MetricRegistry.ImplementedCatalog.Select(d => d.Key))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public ModRegistrationResult Register(ModMetricDescriptor? descriptor)
    {
        if (descriptor is null || !descriptor.IsValid())
            return new ModRegistrationResult(false, ModRegistrationFailure.InvalidDefinition);
        var key = descriptor.StableKey;
        if (_reserved.Contains(key) || _byKey.ContainsKey(key))
            return new ModRegistrationResult(false, ModRegistrationFailure.DuplicateMetric);
        _byKey.Add(key, descriptor);
        return new ModRegistrationResult(true, ModRegistrationFailure.None);
    }

    public IReadOnlyList<ModMetricDescriptor> Descriptors
        => _byKey.Values.OrderBy(d => d.StableKey, StringComparer.Ordinal).ToArray();

    public IReadOnlyList<MetricDefinition> BuildCatalog()
        => Descriptors.Select(d => d.ToDefinition()).ToArray();
}
