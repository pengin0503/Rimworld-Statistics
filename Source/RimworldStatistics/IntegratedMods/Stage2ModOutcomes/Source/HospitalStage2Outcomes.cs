using System.Collections;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using RimworldStatistics.NewModOutcomes;
using RimworldStatistics.Patches.Metrics;
using Verse;

namespace RimworldStatistics.Stage2ModOutcomes;

/// <summary>
/// Stage 2 metrics for Hospital (Adamas.Hospital, 1.7/Assemblies/Hospital.dll):
/// 1. Cured patient discharges: observes HospitalMapComponent.PatientLeaves(Pawn)
///    only when the patient was registered, non-dismissed, mobile, had no pending
///    surgery or medical rest/tend needs (excluding deaths, surgery failures,
///    refusals, manual dismissals, and uncured danger evacuations), and completed
///    RemoveFromPatientList (Dismissed: false -> true).
/// 2. Treatment silver received: transpiles the single GenPlace.TryPlaceThing call
///    inside HospitalMapComponent.PatientLeaves(Pawn) to record the exact quantity
///    of ThingDefOf.Silver actually placed onto a player home map.
/// </summary>
internal static class HospitalStage2Outcomes
{
    private const string AssemblyName = "Hospital";
    private const string DischargesUnit = "mod.adamas.hospital.patient_discharges";
    private const string SilverUnit = "mod.adamas.hospital.treatment_silver";

    private static FieldInfo patientsField = null!;
    private static FieldInfo dismissedField = null!;
    private static FieldInfo pendingSurgeryField = null!;
    [ThreadStatic] private static Map? activeSilverMap;

    private sealed record DischargeAttempt(Pawn Patient, object Data);

    internal static IEnumerable<ModMetricAdapterRegistration> Build()
    {
        yield return OutcomeMetric.Count(
            HospitalStage2Rules.OwnerPackageId,
            "patient_discharges",
            "RWS_HospitalCuredDischarges",
            "RWS_HospitalCuredDischargesDesc",
            MetricCategory.Medicine,
            InstallDischarges);

        yield return OutcomeMetric.Count(
            HospitalStage2Rules.OwnerPackageId,
            "treatment_silver",
            "RWS_HospitalTreatmentSilver",
            "RWS_HospitalTreatmentSilverDesc",
            MetricCategory.Economy,
            InstallTreatmentSilver,
            unit: MetricUnit.Silver);
    }

    private static MethodInfo ResolvePatientLeaves()
    {
        var component = OutcomeMetric.Bind("Hospital.HospitalMapComponent", AssemblyName);
        var data = OutcomeMetric.Bind("Hospital.PatientData", AssemblyName);
        patientsField = AccessTools.DeclaredField(component, "Patients") is { } pf &&
            pf.FieldType == typeof(Dictionary<,>).MakeGenericType(typeof(Pawn), data)
            ? pf
            : throw new InvalidOperationException("Hospital Patients field signature changed");
        dismissedField = AccessTools.DeclaredField(data, "Dismissed") is { } df && df.FieldType == typeof(bool)
            ? df
            : throw new InvalidOperationException("Hospital PatientData.Dismissed field signature changed");
        pendingSurgeryField = AccessTools.DeclaredField(data, "HasPendingSurgeryBill") is { } sf && sf.FieldType == typeof(bool)
            ? sf
            : throw new InvalidOperationException("Hospital PatientData.HasPendingSurgeryBill field signature changed");
        return OutcomeMetric.Method(component, "PatientLeaves", typeof(void), typeof(Pawn));
    }

    private static void InstallDischarges(Harmony harmony)
    {
        var leaves = ResolvePatientLeaves();
        OutcomeMetric.Patch(harmony, leaves, typeof(HospitalStage2Outcomes), nameof(BeforeDischarge), nameof(AfterDischarge));
    }

    private static void BeforeDischarge(MapComponent __instance, Pawn? __0, out DischargeAttempt? __state)
    {
        __state = null;
        if (!OutcomeMetric.Observing(DischargesUnit) ||
            __instance?.map is not { IsPlayerHome: true } ||
            __0 is not { Dead: false, Downed: false, health: { } health } pawn)
            return;

        try
        {
            if (patientsField.GetValue(__instance) is not IDictionary registry ||
                !registry.Contains(pawn) ||
                registry[pawn] is not { } data ||
                dismissedField.GetValue(data) is not bool wasDismissed ||
                pendingSurgeryField.GetValue(data) is not bool hasPendingSurgery)
                return;

            var billCount = health.surgeryBills?.Count ?? 0;
            if (HospitalStage2Rules.IsEligibleCuredPatientBefore(
                    isPlayerHomeMap: true,
                    isAlive: true,
                    isDowned: false,
                    wasDismissed: wasDismissed,
                    hasPendingSurgeryBill: hasPendingSurgery,
                    hasHediffsNeedingTend: health.HasHediffsNeedingTend(false),
                    shouldSeekMedicalRest: HealthAIUtility.ShouldSeekMedicalRest(pawn),
                    pendingBillStackCount: billCount))
            {
                __state = new DischargeAttempt(pawn, data);
            }
        }
        catch (Exception error)
        {
            __state = null;
            LiveMetrics.Fail(DischargesUnit, error);
        }
    }

    private static void AfterDischarge(bool __runOriginal, DischargeAttempt? __state)
    {
        if (!__runOriginal || __state is null || !OutcomeMetric.Observing(DischargesUnit))
            return;

        try
        {
            if (dismissedField.GetValue(__state.Data) is bool dismissedAfter &&
                HospitalStage2Rules.ShouldRecordCuredDischarge(
                    eligibleBefore: true,
                    isAliveAfter: !__state.Patient.Dead,
                    dismissedAfter: dismissedAfter))
            {
                OutcomeMetric.Record(DischargesUnit);
            }
        }
        catch (Exception error)
        {
            LiveMetrics.Fail(DischargesUnit, error);
        }
    }

    private static void InstallTreatmentSilver(Harmony harmony)
    {
        var leaves = ResolvePatientLeaves();
        var prefix = AccessTools.DeclaredMethod(typeof(HospitalStage2Outcomes), nameof(BeginSilverScope));
        var finalizer = AccessTools.DeclaredMethod(typeof(HospitalStage2Outcomes), nameof(EndSilverScope));
        var transpiler = AccessTools.DeclaredMethod(typeof(HospitalStage2Outcomes), nameof(TranspileSilverPlacement));
        try
        {
            harmony.Patch(
                leaves,
                prefix: new HarmonyMethod(prefix),
                finalizer: new HarmonyMethod(finalizer),
                transpiler: new HarmonyMethod(transpiler));
        }
        catch
        {
            harmony.Unpatch(leaves, prefix);
            harmony.Unpatch(leaves, finalizer);
            harmony.Unpatch(leaves, transpiler);
            throw;
        }
    }

    private static void BeginSilverScope(MapComponent __instance, out Map? __state)
    {
        __state = activeSilverMap;
        if (OutcomeMetric.Observing(SilverUnit) && __instance?.map is { IsPlayerHome: true } map)
            activeSilverMap = map;
        else
            activeSilverMap = null;
    }

    private static Exception? EndSilverScope(Exception? __exception, Map? __state)
    {
        activeSilverMap = __state;
        return __exception;
    }

    private static IEnumerable<CodeInstruction> TranspileSilverPlacement(IEnumerable<CodeInstruction> instructions)
    {
        var place8 = AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>), typeof(Rot4?), typeof(int)]);
        var place6 = AccessTools.DeclaredMethod(typeof(GenPlace), nameof(GenPlace.TryPlaceThing),
            [typeof(Thing), typeof(IntVec3), typeof(Map), typeof(ThingPlaceMode),
             typeof(Action<Thing, int>), typeof(Predicate<IntVec3>)]);

        var body = instructions.ToList();
        var call8 = place8 is null ? null : body.SingleOrDefault(i => i.Calls(place8));
        if (call8 is not null)
        {
            call8.opcode = OpCodes.Call;
            call8.operand = AccessTools.DeclaredMethod(typeof(HospitalStage2Outcomes), nameof(PlaceSilver8));
            return body;
        }

        var call6 = place6 is null ? null : body.SingleOrDefault(i => i.Calls(place6));
        if (call6 is not null)
        {
            call6.opcode = OpCodes.Call;
            call6.operand = AccessTools.DeclaredMethod(typeof(HospitalStage2Outcomes), nameof(PlaceSilver6));
            return body;
        }

        throw new InvalidOperationException("Hospital PatientLeaves silver placement call signature changed");
    }

    private static bool PlaceSilver8(
        Thing thing,
        IntVec3 center,
        Map map,
        ThingPlaceMode mode,
        Action<Thing, int>? placedAction,
        Predicate<IntVec3>? nearPlaceValidator,
        Rot4? rot,
        int squareRadius)
        => GenPlace.TryPlaceThing(
            thing,
            center,
            map,
            mode,
            WrapSilverCallback(map, placedAction),
            nearPlaceValidator,
            rot,
            squareRadius);

    private static bool PlaceSilver6(
        Thing thing,
        IntVec3 center,
        Map map,
        ThingPlaceMode mode,
        Action<Thing, int>? placedAction,
        Predicate<IntVec3>? nearPlaceValidator)
        => GenPlace.TryPlaceThing(
            thing,
            center,
            map,
            mode,
            WrapSilverCallback(map, placedAction),
            nearPlaceValidator);

    private static Action<Thing, int>? WrapSilverCallback(Map targetMap, Action<Thing, int>? original)
    {
        var scopeMap = activeSilverMap;
        if (scopeMap is null || !ReferenceEquals(scopeMap, targetMap) || !OutcomeMetric.Observing(SilverUnit))
            return original;

        return (placedThing, placedCount) =>
        {
            original?.Invoke(placedThing, placedCount);
            try
            {
                var accepted = HospitalStage2Rules.AcceptedTreatmentSilver(
                    placedCount,
                    isSilverDef: placedThing?.def == ThingDefOf.Silver,
                    isPlayerHomeMap: scopeMap.IsPlayerHome,
                    activeDischargeScope: ReferenceEquals(activeSilverMap, scopeMap) && OutcomeMetric.Observing(SilverUnit));
                if (accepted > 0)
                    OutcomeMetric.Record(SilverUnit, amount: accepted);
            }
            catch (Exception error)
            {
                LiveMetrics.Fail(SilverUnit, error);
            }
        };
    }
}
