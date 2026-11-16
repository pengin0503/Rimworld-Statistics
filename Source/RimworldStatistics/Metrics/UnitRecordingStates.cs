using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>Live session status, distinct from evidence stored in historical profiles.</summary>
internal static class UnitRecordingStates
{
    private static readonly IReadOnlyDictionary<string, string> BuiltInOwners = BuiltInModMetricCatalog.Definitions
        .Where(d => !string.IsNullOrEmpty(d.Acquisition) && !string.IsNullOrEmpty(d.OwnerPackageId))
        .GroupBy(d => d.Acquisition!, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First().OwnerPackageId!, StringComparer.Ordinal);

    public static string? OwnerPackageId(string acquisitionId)
        => ModProviderAdapters.FirstDescriptorFor(acquisitionId)?.OwnerPackageId
            ?? BuiltInOwners.GetValueOrDefault(acquisitionId);

    public static UnitRecordingState Get(string acquisitionId)
    {
        var unit = AcquisitionUnits.Find(acquisitionId)
            ?? ModProviderAdapters.KnownUnits.FirstOrDefault(u => u.Id == acquisitionId);
        var provider = ModProviderAdapters.FirstDescriptorFor(acquisitionId);
        var dlc = provider?.RequiredDlc ?? unit?.RequiredDlc;
        if (dlc is not null && !ModsConfig.IsActive(dlc))
            return UnitRecordingState.DlcMissing;
        var owner = OwnerPackageId(acquisitionId);
        if (owner is not null && !ModsConfig.IsActive(owner))
            return UnitRecordingState.TargetModMissing;
        if (acquisitionId == ModdedIncidentFamily.Name && !MetricGates.HasModdedIncidentDefs())
            return UnitRecordingState.TargetModMissing;
        if (acquisitionId == OutpostSnapshot.Unit && !MetricGates.HasOutpostWorldObjectDefs())
            return UnitRecordingState.TargetModMissing;
        if (acquisitionId is DbhStatistics.FixturesUnit or DbhStatistics.ActivitiesUnit
            && MetricGates.IsExternalDbhAddonActive())
            return UnitRecordingState.NotSelected;
        if (MetricGates.FailedThisSession(acquisitionId))
            return UnitRecordingState.Failed;
        if (MetricGates.IsRecording(acquisitionId))
            return UnitRecordingState.Recording;
        return MetricGates.IsSelected(acquisitionId)
            ? UnitRecordingState.TargetIncompatible : UnitRecordingState.NotSelected;
    }
}
