namespace RimworldStatistics.Stage2ModOutcomes;

internal static class HospitalStage2Rules
{
    internal const string OwnerPackageId = "Adamas.Hospital";

    internal static bool IsEligibleCuredPatientBefore(
        bool isPlayerHomeMap,
        bool isAlive,
        bool isDowned,
        bool wasDismissed,
        bool hasPendingSurgeryBill,
        bool hasHediffsNeedingTend,
        bool shouldSeekMedicalRest,
        int pendingBillStackCount)
        => isPlayerHomeMap
           && isAlive
           && !isDowned
           && !wasDismissed
           && !hasPendingSurgeryBill
           && !hasHediffsNeedingTend
           && !shouldSeekMedicalRest
           && pendingBillStackCount == 0;

    internal static bool ShouldRecordCuredDischarge(bool eligibleBefore, bool isAliveAfter, bool dismissedAfter)
        => eligibleBefore && isAliveAfter && dismissedAfter;

    internal static long AcceptedTreatmentSilver(int placedCount, bool isSilverDef, bool isPlayerHomeMap, bool activeDischargeScope)
        => placedCount > 0 && isSilverDef && isPlayerHomeMap && activeDischargeScope
            ? placedCount
            : 0;
}
