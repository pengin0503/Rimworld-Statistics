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
    public string GapAccumulatorKey { get; } = "sdk.gap." + Descriptor.MetricName.ToLowerInvariant();
}

/// <summary>
/// SDK registrations, frozen once at startup. An acquisition unit (<c>mod.owner.family</c>) has one hook, one family state
/// and one recording choice, so it belongs to one provider: a built-in integration's units are reserved here, and an SDK
/// metric in such a unit is refused (#314) instead of being accepted into a family whose output never includes it.
/// </summary>
public sealed class ExternalSdkMetricRegistry
{
    private readonly object sync = new();
    private readonly ModMetricProviderRegistry registry =
        new(MetricRegistry.ImplementedCatalog.Select(d => d.Key));
    private readonly Dictionary<string, RegisteredSdkMetric> entries = new(StringComparer.Ordinal);
    private readonly Func<IEnumerable<ModMetricDescriptor>>? builtIn;
    private HashSet<string>? builtInUnits;
    private HashSet<string>? builtInKeys;
    private bool sealedForStartup;
    private System.Collections.Frozen.FrozenDictionary<string, RegisteredSdkMetric>? frozenEntries;

    /// <param name="builtIn">
    /// The descriptors of the host's built-in (non-SDK) integrations, read once at the first registration, whether or not
    /// their MODs are loaded: the units they own are refused to SDK providers.
    /// </param>
    public ExternalSdkMetricRegistry(Func<IEnumerable<ModMetricDescriptor>>? builtIn = null) => this.builtIn = builtIn;

    public ApiRegistrationResult Register(ApiMetricDefinition? api, Func<double?>? snapshot)
        => Register(api, snapshot, out _);

    /// <param name="builtInUnit">
    /// When the metric is refused because a built-in integration owns its acquisition unit, that unit; otherwise null.
    /// </param>
    public ApiRegistrationResult Register(ApiMetricDefinition? api, Func<double?>? snapshot, out string? builtInUnit)
    {
        builtInUnit = null;
        if (api is null || !Enum.IsDefined(api.Kind) || !Enum.IsDefined(api.EventMode)
            || api.Kind == ApiMetricKind.SaveSnapshot != (snapshot is not null)
            || !Version.TryParse(api.ProviderVersion, out _)
            || api.RequiredPackages is null || api.RequiredPackages.Any(x => x is null))
            return ApiRegistrationResult.InvalidDefinition;
        if (!TryParseEnumName<MetricCategory>(api.Category, out var category)
            || !TryParseEnumName<MetricUnit>(api.Unit, out var unit)
            || !TryParseEnumName<OverallAggregation>(api.Overall, out var overall)
            || !TryParseEnumName<ColonySelection>(api.Selection, out var selection))
            return ApiRegistrationResult.InvalidDefinition;
        if (selection == ColonySelection.ObservationMaxByBreakdown
            || api.Kind == ApiMetricKind.Event && ((api.EventMode == ApiEventMode.Mean) != (overall == OverallAggregation.Ratio)
                || selection != ColonySelection.Representative)
            || api.Kind == ApiMetricKind.SaveSnapshot && (overall == OverallAggregation.Ratio || api.EventMode != ApiEventMode.Count)
            || api.HasBreakdown && (api.Kind != ApiMetricKind.Event || api.EventMode == ApiEventMode.Mean)
            || !ModMetricDescriptor.IsValidOwnerDisplayName(api.OwnerDisplayName, api.OwnerPackageId))
            return ApiRegistrationResult.InvalidDefinition;
        var packages = api.RequiredPackages.Select(x => new ModPackageRequirement(x.PackageId, x.MinimumVersion, x.MaximumVersionExclusive)).ToList();
        if (api.MinimumOwnerVersion is not null || api.MaximumOwnerVersionExclusive is not null)
            packages.Insert(0, new ModPackageRequirement(api.OwnerPackageId, api.MinimumOwnerVersion, api.MaximumOwnerVersionExclusive));
        var descriptor = new ModMetricDescriptor
        {
            ProviderId = api.ProviderId, OwnerPackageId = api.OwnerPackageId, OwnerDisplayName = api.OwnerDisplayName,
            FamilyId = api.FamilyId, MetricName = api.MetricName, Revision = api.Revision,
            Source = api.Kind == ApiMetricKind.Event ? MetricObservationSource.Checkpoint : MetricObservationSource.SaveSnapshot,
            Category = category, Unit = unit, Overall = overall, Selection = selection,
            HasBreakdown = api.HasBreakdown,
            RequiredDlc = api.RequiredDlc, RequiredPackages = packages,
            LabelKey = api.LabelKey, DescriptionKey = api.DescriptionKey,
        };
        if (!descriptor.IsValid())
            return ApiRegistrationResult.InvalidDefinition;

        lock (sync)
        {
            if (sealedForStartup)
                return ApiRegistrationResult.Closed;
            LoadBuiltIn();
            if (builtInUnits!.Contains(descriptor.AcquisitionId))
            {
                // Same metric: a duplicate; another metric in that unit: a family the built-in provider already defines.
                builtInUnit = descriptor.AcquisitionId;
                return builtInKeys!.Contains(descriptor.StableKey)
                    ? ApiRegistrationResult.DuplicateMetric : ApiRegistrationResult.ConflictingFamily;
            }
            if (entries.ContainsKey(descriptor.StableKey))
                return ApiRegistrationResult.DuplicateMetric;
            if (entries.Values.Any(x => x.Descriptor.AcquisitionId == descriptor.AcquisitionId &&
                (x.Descriptor.ProviderId != descriptor.ProviderId || x.Descriptor.Revision != descriptor.Revision ||
                 x.Descriptor.Source != descriptor.Source || x.Api.ProviderVersion != api.ProviderVersion ||
                 x.Descriptor.RequiredDlc != descriptor.RequiredDlc ||
                 !x.Descriptor.RequiredPackages.Select(r => r.ToString()).SequenceEqual(
                     descriptor.RequiredPackages.Select(r => r.ToString()), StringComparer.Ordinal))))
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
    }

    private static bool TryParseEnumName<TEnum>(string? text, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        return !string.IsNullOrEmpty(text)
            && char.IsLetter(text[0])
            && Enum.TryParse(text, ignoreCase: false, out value)
            && Enum.IsDefined(value)
            && string.Equals(Enum.GetName(value), text, StringComparison.Ordinal);
    }

    public void ReserveBuiltInUnits(IEnumerable<string> unitIds)
    {
        if (unitIds is null)
            return;
        lock (sync)
        {
            LoadBuiltIn();
            foreach (var unitId in unitIds)
            {
                if (!string.IsNullOrWhiteSpace(unitId))
                    builtInUnits!.Add(unitId.Trim().ToLowerInvariant());
            }
        }
    }

    private void LoadBuiltIn()
    {
        if (builtInUnits is not null)
            return;
        var units = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var descriptor in builtIn?.Invoke() ?? [])
        {
            if (descriptor is null || !descriptor.IsValid())
                continue;
            units.Add(descriptor.AcquisitionId);
            keys.Add(descriptor.StableKey);
        }
        builtInKeys = keys;
        builtInUnits = units;
    }

    public RegisteredSdkMetric? Find(string id)
    {
        if (id is null)
            return null;
        if (Volatile.Read(ref frozenEntries) is { } frozen)
            return frozen.GetValueOrDefault(id);
        lock (sync)
            return entries.TryGetValue(id, out var value) ? value : null;
    }

    public IReadOnlyList<RegisteredSdkMetric> Seal()
    {
        lock (sync)
        {
            sealedForStartup = true;
            Volatile.Write(ref frozenEntries,
                System.Collections.Frozen.FrozenDictionary.ToFrozenDictionary(entries, StringComparer.Ordinal));
            return entries.Values.OrderBy(x => x.Descriptor.StableKey, StringComparer.Ordinal).ToArray();
        }
    }
}

/// <summary>Deliberately bounded, idempotent numerical recording. A cap produces partial coverage instead of overcounting.</summary>
public static class ExternalSdkMetricPolicy
{
    /// <summary>Maximum distinct breakdown bucket keys retained per SDK breakdown metric before failing closed with Partial coverage.</summary>
    public const int BreakdownBucketCap = 64;

    /// <summary>Maximum character length of a breakdown bucket key.</summary>
    public const int MaxBreakdownKeyLength = 96;

    private static string Seen(string metric) => "sdk.seen." + metric.ToLowerInvariant();
    private static string Value(string metric) => "sdk.value." + metric.ToLowerInvariant();

    public static bool IsValidBreakdownKey(string? breakdownKey)
    {
        if (string.IsNullOrWhiteSpace(breakdownKey) || breakdownKey.Length > MaxBreakdownKeyLength || breakdownKey != breakdownKey.Trim())
            return false;
        for (var i = 0; i < breakdownKey.Length; i++)
        {
            if (char.IsControl(breakdownKey[i]))
                return false;
        }
        return true;
    }

    public static ApiRecordResult Record(FamilyState state, ApiMetricDefinition metric, string? eventId, int tick, double value)
    {
        if (state is null || metric is null || metric.Kind != ApiMetricKind.Event || metric.HasBreakdown ||
            string.IsNullOrWhiteSpace(eventId) || eventId.Length > 128 || tick < 0 ||
            !double.IsFinite(value) || Math.Abs(value) > 1e12 ||
            metric.EventMode == ApiEventMode.Count && value != 1)
            return ApiRecordResult.InvalidEvent;

        var seen = Seen(metric.MetricName);
        if (state.Peek(seen)?.Overflow == true)
            return ApiRecordResult.CapExceeded;
        // Store only a fixed-length one-way digest. Never persist an external event/pawn/guest identifier.
        var digest = HashEventId(eventId);
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
        if (state is null || registered is null || registered.Api.Kind != ApiMetricKind.Event || registered.Api.HasBreakdown ||
            string.IsNullOrWhiteSpace(eventId) || eventId.Length > 128 || tick < 0 ||
            !double.IsFinite(value) || Math.Abs(value) > 1e12 ||
            registered.Api.EventMode == ApiEventMode.Count && value != 1)
            return ApiRecordResult.InvalidEvent;

        var seen = registered.SeenAccumulatorKey;
        if (state.Peek(seen)?.Overflow == true)
            return ApiRecordResult.CapExceeded;

        var digest = HashEventId(eventId);
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

    public static ApiRecordResult RecordBreakdown(
        FamilyState state,
        ApiMetricDefinition metric,
        string? eventId,
        string? breakdownKey,
        int tick,
        double value)
    {
        if (state is null || metric is null || metric.Kind != ApiMetricKind.Event || !metric.HasBreakdown
            || metric.EventMode is not (ApiEventMode.Count or ApiEventMode.Sum))
            return ApiRecordResult.InvalidEvent;

        return RecordBreakdownCore(state, Seen(metric.MetricName), Value(metric.MetricName), metric.EventMode, eventId, breakdownKey, tick, value);
    }

    public static ApiRecordResult RecordBreakdown(
        FamilyState state,
        RegisteredSdkMetric? registered,
        string? eventId,
        string? breakdownKey,
        int tick,
        double value)
    {
        if (state is null || registered is null || registered.Api.Kind != ApiMetricKind.Event || !registered.Api.HasBreakdown
            || registered.Api.EventMode is not (ApiEventMode.Count or ApiEventMode.Sum))
            return ApiRecordResult.InvalidEvent;

        return RecordBreakdownCore(
            state,
            registered.SeenAccumulatorKey,
            registered.ValueAccumulatorKey,
            registered.Api.EventMode,
            eventId,
            breakdownKey,
            tick,
            value);
    }

    private static ApiRecordResult RecordBreakdownCore(
        FamilyState state,
        string seenKey,
        string valueKey,
        ApiEventMode mode,
        string? eventId,
        string? breakdownKey,
        int tick,
        double value)
    {
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 128 || !IsValidBreakdownKey(breakdownKey) || tick < 0
            || !double.IsFinite(value) || value < 0 || value > 1e12
            || (mode == ApiEventMode.Count && value != 1))
            return ApiRecordResult.InvalidEvent;

        var seenAcc = state.Peek(seenKey);
        var valAcc = state.Peek(valueKey);
        if (seenAcc?.Overflow == true || valAcc?.Overflow == true)
            return ApiRecordResult.CapExceeded;

        var digest = HashEventId(eventId);
        if (seenAcc?.By?.ContainsKey(digest) == true)
            return ApiRecordResult.Duplicate;

        if (valAcc?.By is { Count: >= BreakdownBucketCap } existingBuckets && !existingBuckets.ContainsKey(breakdownKey!))
        {
            valAcc.Overflow = true;
            return ApiRecordResult.CapExceeded;
        }

        if (!state.Distinct(seenKey, digest))
            return state.Peek(seenKey)?.Overflow == true ? ApiRecordResult.CapExceeded : ApiRecordResult.Duplicate;

        var target = valAcc ?? state.Acc(valueKey);
        target.N = (target.N ?? 0) + 1;
        if (mode != ApiEventMode.Count)
        {
            target.Sum = (target.Sum ?? 0) + value;
            if (target.Min is null || value < target.Min)
                target.Min = value;
            if (target.Max is null || value > target.Max)
                target.Max = value;
        }
        var by = target.By ??= new Dictionary<string, double>(StringComparer.Ordinal);
        var delta = mode == ApiEventMode.Count ? 1.0 : value;
        by[breakdownKey!] = by.GetValueOrDefault(breakdownKey!) + delta;
        target.First ??= tick;
        target.Last = tick;
        return ApiRecordResult.Recorded;
    }

    private static string HashEventId(string eventId)
    {
        // The caller bounds UTF-16 input to 128 chars; GetMaxByteCount(128) covers malformed surrogates as well.
        Span<byte> utf8 = stackalloc byte[Encoding.UTF8.GetMaxByteCount(128)];
        var written = Encoding.UTF8.GetBytes(eventId.AsSpan(), utf8);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(utf8[..written], hash);
        return Convert.ToHexStringLower(hash);
    }

    public static void Build(FamilyOutputContext output, RegisteredSdkMetric metric)
    {
        var d = metric.Descriptor;
        var count = output.State.Peek(metric.ValueAccumulatorKey);
        var n = count?.N ?? 0;
        var partial = output.State.Peek(metric.SeenAccumulatorKey)?.Overflow == true
            || count?.Overflow == true
            || output.State.Peek(metric.GapAccumulatorKey)?.Overflow == true;
        MetricValue value;
        switch (metric.Api.EventMode)
        {
            case ApiEventMode.Count:
                value = output.Value(integer: n, partial: partial);
                break;
            case ApiEventMode.Sum:
            {
                var sum = count?.Sum ?? 0;
                value = double.IsFinite(sum)
                    ? output.Value(real: sum, partial: partial)
                    : output.Value(coverage: MetricCoverage.Unknown, partial: partial);
                break;
            }
            case ApiEventMode.Mean:
            {
                var samples = UtcTime.ClampToInt32(n);
                if (n <= 0)
                    value = output.Value(coverage: output.Coverage == MetricCoverage.Complete
                        ? MetricCoverage.NotApplicable : MetricCoverage.Unknown, samples: 0);
                else if (count?.Sum is not { } sum || !double.IsFinite(sum) || !double.IsFinite(sum / n))
                    value = output.Value(coverage: MetricCoverage.Unknown, samples: samples, partial: partial);
                else
                    value = output.Value(real: sum / n, numerator: sum, denominator: n, samples: samples, partial: partial);
                break;
            }
            default:
                return;
        }

        if (!d.HasBreakdown)
        {
            output.Into[d.StableKey] = MetricEntry.Of(d.Revision, value);
            return;
        }

        var entry = new MetricEntry { Revision = d.Revision };
        entry.Values[MetricEntry.Scalar] = value;
        if (count?.By is { Count: > 0 } by)
        {
            foreach (var (k, v) in by)
            {
                if (!double.IsFinite(v))
                    continue;
                entry.Values[k] = metric.Api.EventMode == ApiEventMode.Count
                    ? output.Value(integer: UtcTime.ClampToInt64(v), partial: partial)
                    : output.Value(real: v, partial: partial);
            }
        }
        output.Into[d.StableKey] = entry;
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
