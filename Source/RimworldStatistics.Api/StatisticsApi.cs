using System.Threading;

namespace RimworldStatistics.Api;

/// <summary>1.x experimental ABI. No types from RimWorld, Harmony or the private Core assembly occur in this contract.</summary>
public static class StatisticsApi
{
    public const int ApiVersionMajor = 1;
    public const int ApiVersionMinor = 1;
    private static IStatisticsHost? host;

    /// <summary>Returns false when the host is absent or cannot satisfy the requested major/minor version.</summary>
    public static bool TryGetHost(int requiredMajor, int minimumMinor, out IStatisticsHost? result)
    {
        result = null;
        if (requiredMajor != ApiVersionMajor || minimumMinor < 0 || minimumMinor > ApiVersionMinor)
            return false;
        var candidate = Volatile.Read(ref host);
        if (candidate is null || candidate.ApiMajor != requiredMajor || candidate.ApiMinor < minimumMinor)
            return false;
        result = candidate;
        return true;
    }

    /// <summary>Called by Rimworld Statistics at startup. A competing host cannot replace a registered host.</summary>
    public static bool InstallHost(IStatisticsHost candidate)
        => candidate is not null && candidate.ApiMajor == ApiVersionMajor
            && candidate.ApiMinor <= ApiVersionMinor
            && Interlocked.CompareExchange(ref host, candidate, null) is null;
}

public enum ApiMetricKind { Event, SaveSnapshot }
public enum ApiEventMode { Count, Sum, Mean }
public enum ApiRegistrationResult { Accepted, InvalidDefinition, DuplicateMetric, ConflictingFamily, Closed, Failed }
public enum ApiRecordResult { Recorded, Duplicate, Disabled, InvalidEvent, CapExceeded, Failed }

/// <summary>MOD dependency; a version bound requires parseable version metadata or registration stays inactive.</summary>
public sealed record ApiPackageRequirement(string PackageId, string? MinimumVersion = null, string? MaximumVersionExclusive = null);

/// <summary>Stable numeric metric declaration, copied on registration. No personal/pawn data or arbitrary payloads.</summary>
public sealed record ApiMetricDefinition
{
    public string ProviderId { get; init; } = "";
    public string OwnerPackageId { get; init; } = "";
    public string FamilyId { get; init; } = "";
    public string MetricName { get; init; } = "";
    public int Revision { get; init; } = 1;
    public ApiMetricKind Kind { get; init; } = ApiMetricKind.Event;
    public ApiEventMode EventMode { get; init; } = ApiEventMode.Count;
    public string Category { get; init; } = "Colony";
    public string Unit { get; init; } = "Count";
    public string Overall { get; init; } = "Sum";
    public string Selection { get; init; } = "Representative";
    public string ProviderVersion { get; init; } = "1.0.0";
    public string? MinimumOwnerVersion { get; init; }
    public string? MaximumOwnerVersionExclusive { get; init; }
    public string? RequiredDlc { get; init; }
    public string? LabelKey { get; init; }
    public string? DescriptionKey { get; init; }
    public IReadOnlyList<ApiPackageRequirement> RequiredPackages { get; init; } = Array.Empty<ApiPackageRequirement>();

    public string MetricId => "mod." + OwnerPackageId.ToLowerInvariant() + "." +
        FamilyId.ToLowerInvariant() + "." + MetricName.ToLowerInvariant();
    public string FamilyAcquisitionId => "mod." + OwnerPackageId.ToLowerInvariant() + "." + FamilyId.ToLowerInvariant();
}

/// <summary>Called only by an external provider, never exposes mutable game/profile objects.</summary>
public interface IStatisticsHost
{
    int ApiMajor { get; }
    int ApiMinor { get; }

    /// <summary>Call in the MOD constructor (after Statistics in load order). Snapshot readers run after successful saves.</summary>
    ApiRegistrationResult Register(ApiMetricDefinition metric, Func<double?>? captureSnapshot = null);

    /// <summary>Call for one confirmed, game-thread event. uniqueEventId is hashed before persistence.</summary>
    ApiRecordResult RecordEvent(string metricId, string uniqueEventId, double value = 1);
}
