using System.Security.Cryptography;
using System.Text;
using RimworldStatistics.Api;

namespace RimworldStatistics.Core;

/// <summary>Pure SDK validation and checkpoint operations; the external SDK has no separate store.</summary>
public sealed record RegisteredSdkMetric(ApiMetricDefinition Api, ModMetricDescriptor Descriptor, Func<double?>? Snapshot)
{
    // A metric is frozen at provider registration; game-time events must not rebuild these string keys.
    // The persisted spelling is intentionally identical to the SDK's original checkpoint format.
    public string SeenAccumulatorKey { get; } = "sdk.seen." + Descriptor.MetricName.ToLowerInvariant();
    public string ValueAccumulatorKey { get; } = "sdk.value." + Descriptor.MetricName.ToLowerInvariant();
}

public sealed class ExternalSdkMetricRegistry
{
    private readonly ModMetricProviderRegistry registry = new(
        MetricRegistry.Catalog.Select(d => d.Key).Concat(new[]
        {
            RecruitmentMetricPolicy.StartedKey, RecruitmentMetricPolicy.ActiveKey,
        }));
    private readonly Dictionary<string, RegisteredSdkMetric> entries = new(StringComparer.Ordinal);
    private bool sealedForStartup;

    public ApiRegistrationResult Register(ApiMetricDefinition? api, Func<double?>? snapshot)
    {
        if (sealedForStartup)
            return ApiRegistrationResult.Closed;
        if (api is null || !Enum.IsDefined(api.Kind) || !Enum.IsDefined(api.EventMode)
            || api.Kind == ApiMetricKind.SaveSnapshot != (snapshot is not null)
            || !Version.TryParse(api.ProviderVersion, out _)
            || api.RequiredPackages is null || api.RequiredPackages.Any(x => x is null))
            return ApiRegistrationResult.InvalidDefinition;
        if (!Enum.TryParse<MetricCategory>(api.Category, false, out var category) || !Enum.IsDefined(category)
            || !Enum.TryParse<MetricUnit>(api.Unit, false, out var unit) || !Enum.IsDefined(unit)
            || !Enum.TryParse<OverallAggregation>(api.Overall, false, out var overall) || !Enum.IsDefined(overall)
            || !Enum.TryParse<ColonySelection>(api.Selection, false, out var selection) || !Enum.IsDefined(selection))
            return ApiRegistrationResult.InvalidDefinition;
        if (api.Kind == ApiMetricKind.Event && (api.EventMode == ApiEventMode.Mean) != (overall == OverallAggregation.Ratio)
            || api.Kind == ApiMetricKind.SaveSnapshot && (overall == OverallAggregation.Ratio || api.EventMode != ApiEventMode.Count))
            return ApiRegistrationResult.InvalidDefinition;
        var packages = api.RequiredPackages.Select(x => new ModPackageRequirement(x.PackageId, x.MinimumVersion, x.MaximumVersionExclusive)).ToList();
        if (api.MinimumOwnerVersion is not null || api.MaximumOwnerVersionExclusive is not null)
            packages.Insert(0, new ModPackageRequirement(api.OwnerPackageId, api.MinimumOwnerVersion, api.MaximumOwnerVersionExclusive));
        var descriptor = new ModMetricDescriptor
        {
            ProviderId = api.ProviderId, OwnerPackageId = api.OwnerPackageId,
            FamilyId = api.FamilyId, MetricName = api.MetricName, Revision = api.Revision,
            Source = api.Kind == ApiMetricKind.Event ? MetricObservationSource.Checkpoint : MetricObservationSource.SaveSnapshot,
            Category = category, Unit = unit, Overall = overall, Selection = selection,
            RequiredDlc = api.RequiredDlc, RequiredPackages = packages,
            LabelKey = api.LabelKey, DescriptionKey = api.DescriptionKey,
        };
        if (!descriptor.IsValid())
            return ApiRegistrationResult.InvalidDefinition;
        if (entries.ContainsKey(descriptor.StableKey))
            return ApiRegistrationResult.DuplicateMetric;
        if (entries.Values.Any(x => x.Descriptor.AcquisitionId == descriptor.AcquisitionId &&
            (x.Descriptor.ProviderId != descriptor.ProviderId || x.Descriptor.Revision != descriptor.Revision ||
             x.Descriptor.Source != descriptor.Source || x.Api.ProviderVersion != api.ProviderVersion ||
             x.Descriptor.RequiredDlc != descriptor.RequiredDlc ||
             !x.Descriptor.RequiredPackages.Select(r => r.ToString()).SequenceEqual(
                 descriptor.RequiredPackages.Select(r => r.ToString()), StringComparer.Ordinal))))
            return ApiRegistrationResult.ConflictingFamily;
        if (descriptor.AcquisitionId == RecruitmentMetricPolicy.RequestsFamilyId ||
            descriptor.AcquisitionId == RecruitmentMetricPolicy.QueueFamilyId)
            return ApiRegistrationResult.ConflictingFamily;
        var registration = registry.Register(descriptor);
        if (!registration.Accepted)
            return registration.Failure == ModRegistrationFailure.DuplicateMetric
                ? ApiRegistrationResult.DuplicateMetric : ApiRegistrationResult.InvalidDefinition;
        // Never hold caller-owned mutable dependency lists after registering.
        var frozen = api with { RequiredPackages = api.RequiredPackages.ToArray() };
        entries.Add(descriptor.StableKey, new RegisteredSdkMetric(frozen, descriptor, snapshot));
        return ApiRegistrationResult.Accepted;
    }

    public RegisteredSdkMetric? Find(string id)
        => id is not null && entries.TryGetValue(id, out var value) ? value : null;

    public IReadOnlyList<RegisteredSdkMetric> Seal()
    {
        sealedForStartup = true;
        return entries.Values.OrderBy(x => x.Descriptor.StableKey, StringComparer.Ordinal).ToArray();
    }
}

/// <summary>Deliberately bounded, idempotent numerical recording. A cap produces partial coverage instead of overcounting.</summary>
public static class ExternalSdkMetricPolicy
{
    private static string Seen(string metric) => "sdk.seen." + metric.ToLowerInvariant();
    private static string Value(string metric) => "sdk.value." + metric.ToLowerInvariant();

    public static ApiRecordResult Record(FamilyState state, ApiMetricDefinition metric, string? eventId, int tick, double value)
    {
        if (state is null || metric is null || metric.Kind != ApiMetricKind.Event ||
            string.IsNullOrWhiteSpace(eventId) || eventId.Length > 128 || tick < 0 ||
            !double.IsFinite(value) || Math.Abs(value) > 1e12 ||
            metric.EventMode == ApiEventMode.Count && value != 1)
            return ApiRecordResult.InvalidEvent;

        var seen = Seen(metric.MetricName);
        if (state.Peek(seen)?.Overflow == true)
            return ApiRecordResult.CapExceeded;
        // Store only a fixed-length one-way digest. Never persist an external event/pawn/guest identifier.
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(eventId)));
        if (!state.Distinct(seen, digest))
            return state.Peek(seen)?.Overflow == true ? ApiRecordResult.CapExceeded : ApiRecordResult.Duplicate;
        var key = Value(metric.MetricName);
        if (metric.EventMode == ApiEventMode.Count)
            state.Count(key);
        else
            state.Observe(key, value);
        state.Mark(key, tick);
        return ApiRecordResult.Recorded;
    }

    /// <summary>
    /// Game-time fast path: use the prevalidated, frozen registration and stack-only UTF-8/hash buffers.
    /// Only the 64-character hex digest persisted in the bounded distinct set is allocated per attempt.
    /// Do not change the digest algorithm: old checkpoints must continue deduplicating identically.
    /// </summary>
    public static ApiRecordResult Record(FamilyState state, RegisteredSdkMetric? registered, string? eventId, int tick, double value)
    {
        if (state is null || registered is null || registered.Api.Kind != ApiMetricKind.Event ||
            string.IsNullOrWhiteSpace(eventId) || eventId.Length > 128 || tick < 0 ||
            !double.IsFinite(value) || Math.Abs(value) > 1e12 ||
            registered.Api.EventMode == ApiEventMode.Count && value != 1)
            return ApiRecordResult.InvalidEvent;

        var seen = registered.SeenAccumulatorKey;
        if (state.Peek(seen)?.Overflow == true)
            return ApiRecordResult.CapExceeded;

        // The validation above bounds UTF-16 input to 128 chars; the maximum UTF-8 byte count
        // also covers fallback sequences for malformed surrogate pairs.
        Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetMaxByteCount(128)];
        var written = Encoding.UTF8.GetBytes(eventId.AsSpan(), utf8);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(utf8[..written], hash);
        var digest = Convert.ToHexStringLower(hash);
        if (!state.Distinct(seen, digest))
            return state.Peek(seen)?.Overflow == true ? ApiRecordResult.CapExceeded : ApiRecordResult.Duplicate;

        var key = registered.ValueAccumulatorKey;
        if (registered.Api.EventMode == ApiEventMode.Count)
            state.Count(key);
        else
            state.Observe(key, value);
        state.Mark(key, tick);
        return ApiRecordResult.Recorded;
    }

    public static void Build(FamilyOutputContext output, RegisteredSdkMetric metric)
    {
        var d = metric.Descriptor;
        var count = output.State.Peek(Value(d.MetricName));
        var n = count?.N ?? 0;
        var partial = output.State.Peek(Seen(d.MetricName))?.Overflow == true;
        MetricValue value;
        switch (metric.Api.EventMode)
        {
            case ApiEventMode.Count:
                value = output.Value(integer: n, partial: partial);
                break;
            case ApiEventMode.Sum:
                value = output.Value(real: count?.Sum ?? 0, partial: partial);
                break;
            case ApiEventMode.Mean:
                value = n == 0 ? output.Value(coverage: output.Coverage == MetricCoverage.Complete
                        ? MetricCoverage.NotApplicable : MetricCoverage.Unknown, samples: 0)
                    : output.Value(real: count!.Sum!.Value / n, numerator: count.Sum.Value,
                        denominator: n, samples: (int)Math.Min(n, int.MaxValue), partial: partial);
                break;
            default:
                return;
        }
        output.Into[d.StableKey] = MetricEntry.Of(d.Revision, value);
    }

    public static MetricEntry? Snapshot(RegisteredSdkMetric metric)
    {
        var amount = metric.Snapshot?.Invoke();
        if (amount is null)
            return null;
        if (!double.IsFinite(amount.Value) || Math.Abs(amount.Value) > 1e12)
            throw new InvalidOperationException("SDK snapshot returned a non-finite or out-of-range value");
        return MetricEntry.Of(metric.Descriptor.Revision,
            new MetricValue { RealValue = amount.Value, Coverage = MetricCoverage.SnapshotOnly });
    }
}
