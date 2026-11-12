namespace RimworldStatistics.HospitalityAddon;

/// <summary>
/// Evidence from the visit's actual lord lifecycle. A visit leaving state alone
/// never proves that the visitors physically departed. In-memory evidence is
/// deliberately not reconstructed from old saves.
/// </summary>
internal readonly record struct VisitExitEvidence(
    bool VisitFinished,
    bool SawActualExit,
    bool SentAway,
    bool Hostile,
    bool RecoveredVisitor,
    bool OtherLossOccurred,
    int ViolentLosses,
    int RemainingPawns,
    bool PlayerHome);

internal static class HospitalityDeparturePolicy
{
    internal static bool ShouldRecord(VisitExitEvidence evidence) =>
        evidence.VisitFinished &&
        evidence.SawActualExit &&
        !evidence.SentAway &&
        !evidence.Hostile &&
        !evidence.RecoveredVisitor &&
        !evidence.OtherLossOccurred &&
        evidence.ViolentLosses == 0 &&
        evidence.RemainingPawns == 0 &&
        evidence.PlayerHome;
}
