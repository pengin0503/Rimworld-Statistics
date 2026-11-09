using RimworldStatistics.Core;

namespace RimworldStatistics.VerifyHarness;

/// <summary>
/// Read-only coverage inventory. Observed only means an actual value was found;
/// it does NOT establish event accuracy, correct zeros or performance and must
/// never upgrade an acquisition unit's verification status.
/// </summary>
public enum CatalogAuditState
{
    Observed,
    NotObserved,
    NotRecording,
    DlcUnavailable,
    DerivedOnly,
}

public sealed record CatalogAuditRow(MetricDefinition Definition, CatalogAuditState State)
{
    public bool Verified => false; // Only manually reviewed real-game evidence can change publication status.
}

public static class VerifyCatalogAudit
{
    /// <summary>One row for every registered experimental output, in registry order.</summary>
    public static IReadOnlyList<CatalogAuditRow> Build(
        IReadOnlyList<MetricDefinition> definitions,
        IReadOnlySet<string> recordingUnits,
        IReadOnlySet<string> observedStoredKeys,
        IReadOnlySet<string> unavailableDlc)
        => definitions.Where(d => d.Acquisition is not null).Select(d => new CatalogAuditRow(d,
            d.Overall == OverallAggregation.Ranking ? CatalogAuditState.DerivedOnly :
            d.RequiredDlc is { } dlc && unavailableDlc.Contains(dlc) ? CatalogAuditState.DlcUnavailable :
            !recordingUnits.Contains(d.Acquisition!) ? CatalogAuditState.NotRecording :
            observedStoredKeys.Contains(d.StoredKey) ? CatalogAuditState.Observed :
            CatalogAuditState.NotObserved)).ToArray();
}
