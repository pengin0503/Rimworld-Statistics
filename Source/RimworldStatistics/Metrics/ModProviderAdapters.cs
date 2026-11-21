using HarmonyLib;
using RimworldStatistics.Core;
using RimworldStatistics.VehicleFramework;
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
    Action? ValidateSnapshot = null);

/// <summary>Installed once for compatible optional mods; recording preference is mutable at runtime.</summary>
internal static class ModProviderAdapters
{
    private sealed record Enabled(ModMetricAdapterRegistration Registration, string? OwnerVersion);
    private static readonly ModProviderExecutionGuard Guard = new();
    private static readonly Lazy<IReadOnlyList<ModMetricAdapterRegistration>> Declarations =
        new(BuildAllAdapters);
    private static Enabled[] active = Array.Empty<Enabled>();
    private static bool initialized;

    // Declarations only: no MOD is probed and nothing is patched until Initialize. The SDK registry reads the same list,
    // so a unit an SDK provider is refused is exactly a unit declared here (#314).
    private static readonly Lazy<IReadOnlyList<ModMetricAdapterRegistration>> BuiltIn = new(() =>
    {
        var result = new List<ModMetricAdapterRegistration>();
        try
        {
            result.AddRange(IntegratedTransitionAdapters.Build());
            result.Add(VehicleOwnershipSnapshot.Build());
        }
        catch (Exception e)
        {
            Log.Error("[RimworldStatistics] Native optional metrics registration failed.\n" + e);
        }
        return result;
    });

    /// <summary>The descriptors of the built-in (non-SDK) integrations, whose acquisition units SDK providers cannot use.</summary>
    internal static IEnumerable<ModMetricDescriptor> BuiltInDescriptors => BuiltIn.Value.Select(x => x.Descriptor);

    /// <summary>Both embedded optional integrations and separately loaded SDK consumers register in the existing host during Mod construction.</summary>
    private static IReadOnlyList<ModMetricAdapterRegistration> BuildAllAdapters()
    {
        var result = new List<ModMetricAdapterRegistration>(BuiltIn.Value);
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
        // Only providers with installed target MODs are considered; user opt-in is a live gate.
        var installed = ModProviderGameDiscovery.ActivePackages();
        MetricGates.ConfigureOptionalUnits(KnownUnits, RimworldStatisticsMod.ActiveAcquisitionSelection);
        var registry = new ModMetricProviderRegistry();
        var enabled = new List<Enabled>();
        var families = new List<LiveFamily>();
        // Group by AcquisitionId so a partial failure in a multi-descriptor unit never leaves a half-installed
        // hook or a dangling descriptor in LiveFamily/snapshot capture.
        foreach (var group in declarations.GroupBy(x => x.Descriptor.AcquisitionId, StringComparer.Ordinal))
        {
            var id = group.Key;
            var regs = group.ToArray();
            var groupOk = true;
            var groupValidated = new List<Enabled>(regs.Length);
            LiveFamily? groupFamily = null;
            Action<Harmony>? groupInstall = null;

            foreach (var registration in regs)
            {
                var descriptor = registration.Descriptor;
                if (!registry.Register(descriptor).Accepted)
                {
                    Log.ErrorOnce("[RimworldStatistics] Duplicate/invalid optional statistics descriptor: " +
                        descriptor.StableKey, ("RWS.provider.invalid." + descriptor.ProviderId).GetHashCode());
                    groupOk = false;
                    break;
                }
                // Hook registration must not depend on a preference that can change
                // in-game. Avoid even probing definitions for an absent target MOD.
                if (!MetricGates.IsInstallEligible(id)
                    || !installed.Any(x => string.Equals(x.PackageId, descriptor.OwnerPackageId,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    groupOk = false;
                    break;
                }
                var presentDefs = descriptor.RequiredDefNames.Count == 0 ? null
                    : ModProviderGameDiscovery.ExistingDefs(descriptor.RequiredDefNames);
                var eligibility = ModProviderDiscovery.Resolve(descriptor, installed, presentDefs);
                if (!eligibility.Available)
                {
                    if (MetricGates.IsSelected(id))
                        Log.WarningOnce($"[RimworldStatistics] Selected optional acquisition '{id}' unavailable: "
                            + $"{eligibility.State} ({eligibility.Detail ?? descriptor.OwnerPackageId}).",
                            ("RWS.unit.incompatible." + id).GetHashCode());
                    groupOk = false;
                    break;
                }
                if (descriptor.RequiredDlc is { } dlc && !ModsConfig.IsActive(dlc))
                {
                    groupOk = false;
                    break;
                }
                var ownerVersion = installed.FirstOrDefault(x =>
                    string.Equals(x.PackageId, descriptor.OwnerPackageId, StringComparison.OrdinalIgnoreCase))?.Version;
                if (descriptor.Source == MetricObservationSource.Checkpoint)
                {
                    if (registration.EventFamily is not { } family || family.Name != id ||
                        family.Revision != descriptor.Revision || registration.InstallEventHook is null)
                    {
                        MetricGates.Fail(id, new InvalidOperationException("Optional event family lacks a complete hook/decoder"));
                        groupOk = false;
                        break;
                    }
                    groupFamily ??= family;
                    groupInstall ??= registration.InstallEventHook;
                }
                else if (registration.CaptureSnapshot is null)
                {
                    MetricGates.Fail(id, new InvalidOperationException("Optional snapshot has no reader"));
                    groupOk = false;
                    break;
                }
                if (registration.ValidateSnapshot is { } validate &&
                    !Guard.TryRun(id, validate, (unit, e) => MetricGates.Fail(unit, e)))
                {
                    groupOk = false;
                    break;
                }
                groupValidated.Add(new Enabled(registration, ownerVersion));
            }

            if (!groupOk)
                continue;
            if (groupFamily is not null && groupInstall is not null)
            {
                if (!Guard.TryRun(id, () => groupInstall(harmony), (unit, e) => MetricGates.Fail(unit, e)))
                    continue;
                families.Add(groupFamily);
            }
            MetricGates.MarkApplied(id);
            enabled.AddRange(groupValidated);
        }
        LiveFamilies.SetOptionalFamilies(families);
        active = enabled.ToArray();
        activeDescriptors = active.Select(x =>
        {
            var d = x.Registration.Descriptor;
            var resolvedTitle = ModMetricDescriptor.SanitizeOwnerDisplayName(
                UI.MetricText.ActiveModTitle(d.OwnerPackageId),
                d.OwnerPackageId) ?? d.OwnerDisplayName;
            return resolvedTitle is not null && !string.Equals(d.OwnerDisplayName, resolvedTitle, StringComparison.Ordinal)
                ? d with { OwnerDisplayName = resolvedTitle }
                : d;
        }).ToArray();
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

    /// <summary>Called only after a committed game save; every optional unit writes all its detached scalars atomically or none.</summary>
    public static void CaptureSuccessfulSave(MetricObservation observation)
    {
        var currentAcquisition = string.Empty;
        var staged = new List<(ModMetricDescriptor Descriptor, string? OwnerVersion, string ProviderVersion, MetricEntry? Entry)>();
        var groupFailed = false;

        void CommitStaged()
        {
            if (groupFailed || staged.Count == 0)
            {
                staged.Clear();
                return;
            }
            foreach (var (d, ownerVersion, providerVersion, entry) in staged)
            {
                if (entry is null)
                    continue;
                observation.Metrics[d.StableKey] = entry;
                observation.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(StringComparer.Ordinal);
                observation.EvidenceByAcquisition[d.AcquisitionId] = MetricEvidence.Experimental;
                observation.ProviderCaptures ??= new Dictionary<string, ModProviderCapture>(StringComparer.Ordinal);
                observation.ProviderCaptures[d.ProviderId] = new ModProviderCapture(d.OwnerPackageId, ownerVersion, providerVersion);
            }
            staged.Clear();
        }

        foreach (var enabled in active)
        {
            var reg = enabled.Registration;
            var descriptor = reg.Descriptor;
            if (descriptor.Source != MetricObservationSource.SaveSnapshot ||
                !MetricGates.IsRecording(descriptor.AcquisitionId) || reg.CaptureSnapshot is null)
                continue;
            if (!string.Equals(currentAcquisition, descriptor.AcquisitionId, StringComparison.Ordinal))
            {
                CommitStaged();
                currentAcquisition = descriptor.AcquisitionId;
                groupFailed = false;
            }
            if (groupFailed)
                continue;
            if (!Guard.TryRun(descriptor.AcquisitionId, () =>
            {
                var entry = reg.CaptureSnapshot();
                if (entry is null)
                {
                    staged.Add((descriptor, enabled.OwnerVersion, reg.ProviderVersion, null));
                    return; // Missing is unknown, never a fabricated zero.
                }
                if (entry.IsQuarantined || entry.Revision != descriptor.Revision)
                    throw new InvalidOperationException("Optional snapshot returned an unreadable or wrong-revision metric");
                staged.Add((descriptor, enabled.OwnerVersion, reg.ProviderVersion, entry));
            }, (unit, e) => LiveMetrics.Fail(unit, e)))
            {
                groupFailed = true;
                staged.Clear();
            }
        }
        CommitStaged();
    }
}
