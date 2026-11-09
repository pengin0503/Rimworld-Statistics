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
    string ProviderVersion = "1.0.0",
    Action? ValidateBinding = null);

/// <summary>Frozen at the startup adapter pass; absent optional mods have no hooks or save-time work.</summary>
internal static class ModProviderAdapters
{
    private sealed record Enabled(ModMetricAdapterRegistration Registration, string? OwnerVersion);
    private static readonly ModProviderExecutionGuard Guard = new();
    private static readonly Lazy<IReadOnlyList<ModMetricAdapterRegistration>> Declarations =
        new(ExternalOnlyAdapters);
    private static Enabled[] active = Array.Empty<Enabled>();
    private static bool initialized;

    /// <summary>Only separately loaded SDK addons can register optional metrics.</summary>
    private static IReadOnlyList<ModMetricAdapterRegistration> ExternalOnlyAdapters()
    {
        try { return ExternalSdkHost.Instance.BuildAdapters(); }
        catch (Exception e)
        {
            Log.Error("[RimworldStatistics] External SDK registration scan failed; optional metrics are off.\n" + e);
            return Array.Empty<ModMetricAdapterRegistration>();
        }
    }

    public static IReadOnlyList<AcquisitionUnit> KnownUnits =>
        Declarations.Value.Where(x => x.Descriptor.IsValid())
            .Select(x => new AcquisitionUnit(x.Descriptor.AcquisitionId,
                x.Descriptor.Source == MetricObservationSource.Checkpoint ? AcquisitionKind.Family : AcquisitionKind.Snapshot,
                AcquisitionStatus.Unverified, x.Descriptor.RequiredDlc))
            .GroupBy(x => x.Id, StringComparer.Ordinal).Select(g => g.First()).ToArray();

    public static IReadOnlyList<ModMetricDescriptor> KnownDescriptors
        => Declarations.Value.Where(x => x.Descriptor.IsValid()).Select(x => x.Descriptor).ToArray();

    public static IReadOnlyList<ModMetricDescriptor> ActiveDescriptors
        => active.Select(x => x.Registration.Descriptor).ToArray();

    public static bool HasSnapshots => active.Any(x => x.Registration.Descriptor.Source == MetricObservationSource.SaveSnapshot);

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
                continue;
            if (descriptor.RequiredDlc is { } dlc && !ModsConfig.IsActive(dlc))
                continue;
            // Fail the unit before installing a patch/reader if this installed MOD's
            // actual method/field/property shape is not the version we audited.
            if (registration.ValidateBinding is not null &&
                !Guard.TryRun(id, registration.ValidateBinding,
                    (unit, e) => MetricGates.Fail(unit, e)))
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
                if (!Guard.TryRun(id, () => registration.InstallEventHook(harmony),
                    (unit, e) => MetricGates.Fail(unit, e)))
                    continue;
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
