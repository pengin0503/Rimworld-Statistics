using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>Live session status, distinct from evidence stored in historical profiles.</summary>
internal static class UnitRecordingStates
{
    public static UnitRecordingState Get(string acquisitionId)
    {
        var unit = AcquisitionUnits.Find(acquisitionId)
            ?? ModProviderAdapters.KnownUnits.FirstOrDefault(u => u.Id == acquisitionId);
        var provider = ModProviderAdapters.FirstDescriptorFor(acquisitionId);
        var dlc = provider?.RequiredDlc ?? unit?.RequiredDlc;
        if (dlc is not null && !ModsConfig.IsActive(dlc))
            return UnitRecordingState.DlcMissing;
        var owner = provider?.OwnerPackageId ?? BuiltInModMetricCatalog.Definitions
            .FirstOrDefault(d => d.Acquisition == acquisitionId)?.OwnerPackageId;
        if (owner is not null && !ModsConfig.IsActive(owner))
            return UnitRecordingState.TargetModMissing;
        if (MetricGates.FailedThisSession(acquisitionId))
            return UnitRecordingState.Failed;
        return MetricGates.IsRecording(acquisitionId)
            ? UnitRecordingState.Recording : UnitRecordingState.NotSelected;
    }
}
