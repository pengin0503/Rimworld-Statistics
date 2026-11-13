using HarmonyLib;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>
/// Internal adapter registration. External SDK providers are mapped to the same
/// proven checkpoint and snapshot pipeline; no additional persistence is introduced.
/// </summary>
internal sealed record ModMetricAdapterRegistration(
    ModMetricDescriptor Descriptor,
    LiveFamily? EventFamily = null,
    Action<Harmony>? InstallEventHook = null,
    Func<MetricEntry?>? CaptureSnapshot = null,
    string ProviderVersion = "1.0.0");

/// <summary>Frozen at the startup adapter pass; absent optional mods have no hooks or save-time work.</summary>
internal static class ModProviderAdapters
{
    private sealed record Enabled(ModMetricAdapterRegistration Registration, string? OwnerVersion);
    private static readonly ModProviderExecutionGuard Guard = new();
    private static readonly Lazy<IReadOnlyList<ModMetricAdapterRegistration>> Declarations =
        new(BuildAllAdapters);
    private static Enabled[] active = Array.Empty<Enabled>();
    private static bool initialized;

    /// <summary>Both embedded optional integrations and separately loaded SDK consumers register in the existing host during Mod construction.</summary>
    private static IReadOnlyList<ModMetricAdapterRegistration> BuildAllAdapters()
    {
        var result = new List<ModMetricAdapterRegistration>();
        try { result.AddRange(IntegratedTransitionAdapters.Build()); }
        catch (Exception e)
        {
            Log.Error("[RimworldStatistics] Native optional metrics registration failed.\n" + e);
        }
        try { result.AddRange(ExternalSdkHost.Instance.BuildAdapters()); }
        catch (Exception e)
        {
            Log.Error("[RimworldStatistics] External SDK registration scan failed.\n" + e);
        }
        return result;
    }

    private static readonly Lazy<IReadOnlyList<AcquisitionUnit>> KnownUnitList = new(() =>
        Declarations.Value.Where(x => x.Descriptor.IsValid())
            .Select(x => new AcquisitionUnit(x.Descriptor.AcquisitionId,
                x.Descriptor.Source == MetricObservationSource.Checkpoint ? AcquisitionKind.Family : AcquisitionKind.Snapshot,
                AcquisitionStatus.Unverified, x.Descriptor.RequiredDlc))
            .GroupBy(x => x.Id, StringComparer.Ordinal).Select(g => g.First()).ToArray());
    private static readonly Lazy<IReadOnlyList<ModMetricDescriptor>> KnownDescriptorList = new(() =>
        Declarations.Value.Where(x => x.Descriptor.IsValid()).Select(x => x.Descriptor).ToArray());
    private static readonly Lazy<IReadOnlyDictionary<string, ModMetricDescriptor>> FirstByAcquisition = new(() =>
    {
        var map = new Dictionary<string, ModMetricDescriptor>(StringComparer.Ordinal);
        foreach (var d in KnownDescriptorList.Value)
            map.TryAdd(d.AcquisitionId, d);
        return map;
    });
    private static IReadOnlyList<ModMetricDescriptor> activeDescriptors = Array.Empty<ModMetricDescriptor>();
    private static bool hasSnapshots;

    public static IReadOnlyList<AcquisitionUnit> KnownUnits => KnownUnitList.Value;
    public static IReadOnlyList<ModMetricDescriptor> KnownDescriptors => KnownDescriptorList.Value;
    public static ModMetricDescriptor? FirstDescriptorFor(string acquisitionId) => FirstByAcquisition.Value.GetValueOrDefault(acquisitionId);
    public static IReadOnlyList<ModMetricDescriptor> ActiveDescriptors => activeDescriptors;
    public static bool HasSnapshots => hasSnapshots;

    public static void Initialize(Harmony harmony)
    {
        if (initialized)
            return;
        initialized = true;
        var declarations = Declarations.Value;
        // Every external addon is opt-in; no third-party collector is built into the host.
        var installed = ModProviderGameDiscovery.ActivePackages();
        MetricGates.ConfigureOptionalUnits(KnownUnits, RimworldStatisticsMod.ActiveAcquisitionSelection);
        var registry = new ModMetricProviderRegistry();
        var enabled = new List<Enabled>();
        var families = new List<LiveFamily>();
        // A family with several output descriptors owns one hook and state.
        var installedEventFamilies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var registration in declarations)
        {
            var descriptor = registration.Descriptor;
            if (!registry.Register(descriptor).Accepted)
            {
                Log.ErrorOnce("[RimworldStatistics] Duplicate/invalid optional statistics descriptor: " +
                    descriptor.StableKey, ("RWS.provider.invalid." + descriptor.ProviderId).GetHashCode());
                continue;
            }
            var id = descriptor.AcquisitionId;
            if (!MetricGates.IsOpen(id))
                continue;
            var presentDefs = descriptor.RequiredDefNames.Count == 0 ? null
                : ModProviderGameDiscovery.ExistingDefs(descriptor.RequiredDefNames);
            var eligibility = ModProviderDiscovery.Resolve(descriptor, installed, presentDefs);
            if (!eligibility.Available)
            {
                Log.WarningOnce($"[RimworldStatistics] Selected optional acquisition '{id}' unavailable: "
                    + $"{eligibility.State} ({eligibility.Detail ?? descriptor.OwnerPackageId}).",
                    ("RWS.provider.unavailable." + id).GetHashCode());
                continue;
            }
            if (descriptor.RequiredDlc is { } dlc && !ModsConfig.IsActive(dlc))
                continue;
            var ownerVersion = installed.FirstOrDefault(x =>
                string.Equals(x.PackageId, descriptor.OwnerPackageId, StringComparison.OrdinalIgnoreCase))?.Version;
            if (descriptor.Source == MetricObservationSource.Checkpoint)
            {
                if (registration.EventFamily is not { } family || family.Name != id ||
                    family.Revision != descriptor.Revision || registration.InstallEventHook is null)
                {
                    MetricGates.Fail(id, new InvalidOperationException("Optional event family lacks a complete hook/decoder"));
                    continue;
                }
                if (!installedEventFamilies.Contains(id))
                {
                    if (!Guard.TryRun(id, () => registration.InstallEventHook(harmony),
                        (unit, e) => MetricGates.Fail(unit, e)))
                        continue;
                    installedEventFamilies.Add(id);
                }
                if (!families.Any(x => x.Name == family.Name))
                    families.Add(family);
            }
            else if (registration.CaptureSnapshot is null)
            {
                MetricGates.Fail(id, new InvalidOperationException("Optional snapshot has no reader"));
                continue;
            }
            MetricGates.MarkApplied(id);
            enabled.Add(new Enabled(registration, ownerVersion));
        }
        LiveFamilies.SetOptionalFamilies(families);
        active = enabled.ToArray();
        activeDescriptors = active.Select(x => x.Registration.Descriptor).ToArray();
        hasSnapshots = active.Any(x => x.Registration.Descriptor.Source == MetricObservationSource.SaveSnapshot);
    }

    /// <summary>Ensure a resumed event family is attributed without rewriting earlier version histories.</summary>
    public static void AttachEventOrigins(LiveMetricAccumulator accumulator)
    {
        foreach (var enabled in active)
        {
            if (enabled.Registration.Descriptor.Source != MetricObservationSource.Checkpoint)
                continue;
            var descriptor = enabled.Registration.Descriptor;
            var state = accumulator.Family(descriptor.AcquisitionId);
            if (state is null)
                continue;
            var current = new ModProviderCapture(descriptor.OwnerPackageId, enabled.OwnerVersion,
                enabled.Registration.ProviderVersion);
            state.ProviderId ??= descriptor.ProviderId;
            state.ProviderCapture = state.ProviderCapture is { } previous
                ? ModProviderCapture.Combine(previous, current) : current;
        }
    }

    /// <summary>Called only after a committed game save; every optional unit writes one detached scalar or nothing.</summary>
    public static void CaptureSuccessfulSave(MetricObservation observation)
    {
        foreach (var enabled in active)
        {
            var reg = enabled.Registration;
            var descriptor = reg.Descriptor;
            if (descriptor.Source != MetricObservationSource.SaveSnapshot ||
                !MetricGates.IsRecording(descriptor.AcquisitionId) || reg.CaptureSnapshot is null)
                continue;
            Guard.TryRun(descriptor.AcquisitionId, () =>
            {
                var entry = reg.CaptureSnapshot();
                if (entry is null)
                    return; // Missing is unknown, never a fabricated zero.
                if (entry.IsQuarantined || entry.Revision != descriptor.Revision)
                    throw new InvalidOperationException("Optional snapshot returned an unreadable or wrong-revision metric");
                observation.Metrics[descriptor.StableKey] = entry;
                observation.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(StringComparer.Ordinal);
                observation.EvidenceByAcquisition[descriptor.AcquisitionId] = MetricEvidence.Experimental;
                observation.ProviderCaptures ??= new Dictionary<string, ModProviderCapture>(StringComparer.Ordinal);
                observation.ProviderCaptures[descriptor.ProviderId] = new ModProviderCapture(
                    descriptor.OwnerPackageId, enabled.OwnerVersion, reg.ProviderVersion);
            }, (unit, e) => LiveMetrics.Fail(unit, e));
        }
    }
}
