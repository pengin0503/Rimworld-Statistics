namespace RimworldStatistics.Core;

public enum ModProviderAvailability
{
    Available,
    MissingPackage,
    MissingVersion,
    IncompatibleVersion,
    MissingDef,
    InvalidDefinition,
}

public sealed record ModProviderAvailabilityResult(ModProviderAvailability State, string? Detail = null)
{
    public bool Available => State == ModProviderAvailability.Available;
}

/// <summary>Pure availability check; the game-specific inventory adapter is separate.</summary>
public static class ModProviderDiscovery
{
    public static ModProviderAvailabilityResult Resolve(ModMetricDescriptor descriptor,
        IEnumerable<ModPackageInfo> installedMods, IReadOnlySet<string>? presentDefs = null)
    {
        if (!descriptor.IsValid())
            return new(ModProviderAvailability.InvalidDefinition);
        var inventory = installedMods.Where(p => p != null)
            .GroupBy(p => p.PackageId ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Version, StringComparer.OrdinalIgnoreCase);
        // The owner MOD is always mandatory, whether or not explicitly listed as an extra requirement.
        var required = new[] { new ModPackageRequirement(descriptor.OwnerPackageId) }
            .Concat(descriptor.RequiredPackages ?? Array.Empty<ModPackageRequirement>())
            .ToArray();
        foreach (var r in required)
        {
            if (!inventory.TryGetValue(r.PackageId, out var actual))
                return new(ModProviderAvailability.MissingPackage, r.PackageId);
            if (r.MinVersion is null && r.MaxVersionExclusive is null)
                continue;
            if (string.IsNullOrEmpty(actual) || !Version.TryParse(actual, out var parsed))
                return new(ModProviderAvailability.MissingVersion, r.PackageId);
            if (r.MinVersion is { } min && parsed < Version.Parse(min)
                || r.MaxVersionExclusive is { } max && parsed >= Version.Parse(max))
                return new(ModProviderAvailability.IncompatibleVersion, r.PackageId);
        }
        foreach (var requiredDef in descriptor.RequiredDefNames ?? Array.Empty<string>())
            if (presentDefs is null || !presentDefs.Contains(requiredDef))
                return new(ModProviderAvailability.MissingDef, requiredDef);
        return new(ModProviderAvailability.Available);
    }
}
