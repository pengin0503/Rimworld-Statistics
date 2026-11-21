using HarmonyLib;
using RimWorld;
using RimworldStatistics.Core;
using RimworldStatistics.Metrics;
using Verse;
using Verse.AI;

namespace RimworldStatistics.Patches.Metrics;

/// <summary>
/// Medicine (evidence #medicine). Tends: the hediffs whose tend duration comp changed in one DoTend are the applied ones, each
/// with its own applied quality; medicine used is the stack difference. Surgeries: Recipe_Surgery.CheckSurgeryFail decides the
/// outcome; Bill_Medical.Notify_IterationCompleted is the surgery scope (parts removed or hediffs taken there are not losses
/// or cures).
/// </summary>
internal static class MedicalProbe
{
    [ThreadStatic] public static int SurgeryDepth;

    public static bool CanBeTrackedHediff(Hediff hediff)
        => hediff?.def != null
            && (hediff.def.makesSickThought || hediff.def == HediffDefOf.WoundInfection || hediff is Hediff_Addiction);
}

[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
public static class DoTendMetricPatch
{
    public sealed class Before
    {
        public readonly Dictionary<HediffComp_TendDuration, (int Ticks, float Quality)> Comps = new();
        public string? DoctorId;
        public bool ColonyPatient;
        public bool NeutralPatient;
        public int Medicine;
    }

    public static void Prefix(Pawn doctor, Pawn patient, Medicine medicine, out Before? __state)
    {
        __state = null;
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (patient?.health?.hediffSet == null)
                return;
            var doctorId = doctor != null && GameFacts.IsPlayer(doctor) ? GameFacts.Id(doctor) : null;
            var playerPatient = GameFacts.IsPlayer(patient);
            var colonistPatient = GameFacts.IsFreeColonistSafe(patient);
            var colonyPatient = playerPatient || colonistPatient || GameFacts.IsPrisonerOfColonySafe(patient);
            if (!MedicineFamily.IsColonyTend(doctorId, colonyPatient) || !LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;

            var b = new Before
            {
                DoctorId = doctorId,
                ColonyPatient = colonyPatient,
                NeutralPatient = !colonistPatient && !playerPatient && patient.Faction != null
                    && GameFacts.PlayerFaction is { } pf && patient.RaceProps.Humanlike && !patient.HostileTo(pf),
                Medicine = medicine is { Destroyed: false } ? medicine.stackCount : 0,
            };
            foreach (var h in patient.health.hediffSet.hediffs)
                if (h is HediffWithComps hc && hc.TryGetComp<HediffComp_TendDuration>() is { } td)
                    b.Comps[td] = (td.tendTicksLeft, td.tendQuality);
            __state = b;
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }

    public static void Postfix(Pawn patient, Medicine medicine, Before? __state)
    {
        if (__state == null || !LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (patient?.health?.hediffSet == null)
                return;
            var qualities = new List<double>();
            foreach (var h in patient.health.hediffSet.hediffs)
                if (h is HediffWithComps hc && hc.TryGetComp<HediffComp_TendDuration>() is { } td
                    && (!__state.Comps.TryGetValue(td, out var before) || before.Ticks != td.tendTicksLeft || before.Quality != td.tendQuality) && td.IsTended)
                    qualities.Add(td.tendQuality);
            var used = __state.Medicine - (medicine is { Destroyed: false } ? medicine.stackCount : 0);
            GameFacts.Record(MedicineFamily.Name, s =>
                MedicineFamily.Tended(s, __state.DoctorId, __state.ColonyPatient, __state.NeutralPatient, qualities, Math.Max(0, used)));
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }
}

[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(Bill_Medical), nameof(Bill_Medical.Notify_IterationCompleted))]
public static class SurgeryScopeMetricPatch
{
    public static void Prefix() => MedicalProbe.SurgeryDepth++;

    public static Exception? Finalizer(Exception? __exception)
    {
        MedicalProbe.SurgeryDepth--;
        return __exception;
    }
}

[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(Recipe_Surgery), "CheckSurgeryFail")]
public static class SurgeryOutcomeMetricPatch
{
    public static void Postfix(Recipe_Surgery __instance, bool __result, Pawn surgeon, Pawn patient)
    {
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (surgeon == null || patient == null || !GameFacts.IsPlayer(surgeon) || !LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;
            var id = GameFacts.Id(surgeon);
            var implant = __instance is Recipe_InstallArtificialBodyPart or Recipe_InstallImplant;
            GameFacts.Record(MedicineFamily.Name, s => MedicineFamily.Surgery(s, id, !__result, implant));
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }
}

[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(RestUtility), nameof(RestUtility.TuckIntoBed))]
public static class RescueMetricPatch
{
    public static void Prefix(Pawn taker, Pawn takee, bool rescued, out bool __state)
    {
        __state = false;
        if (!rescued || !LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (takee == null || taker == null || !GameFacts.IsPlayer(taker))
                return;
            __state = GameFacts.IsHostileToPlayer(takee);
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }

    public static void Postfix(Building_Bed bed, Pawn taker, Pawn takee, bool rescued, bool __state)
    {
        if (!rescued || !LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (bed == null || takee == null || taker == null || !GameFacts.IsPlayer(taker)
                || !LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;
            if (takee.CurrentBed() != bed)
                return;
            var away = bed.Map is not { IsPlayerHome: true };
            GameFacts.Record(MedicineFamily.Name, s => MedicineFamily.Rescued(s, __state, away));
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }
}

[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(ResurrectionUtility), nameof(ResurrectionUtility.TryResurrect))]
public static class ResurrectMetricPatch
{
    public static void Prefix(Pawn pawn, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try { __state = pawn != null && pawn.Dead && GameFacts.IsFreeColonistSafe(pawn); }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }

    public static void Postfix(bool __result, Pawn pawn, bool __state)
    {
        if (!__state || !__result || !LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (pawn == null || pawn.Dead || !LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;
            var id = GameFacts.Id(pawn);
            GameFacts.Record(MedicineFamily.Name, s => MedicineFamily.Resurrected(s, id));
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }
}

/// <summary>New hediffs of free colonists (not merged into an existing one; prior presence of the def is checked for onsets).</summary>
[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.AddHediff),
    new[] { typeof(Hediff), typeof(BodyPartRecord), typeof(DamageInfo?), typeof(DamageWorker.DamageResult) })]
public static class AddHediffMetricPatch
{
    public static void Prefix(Pawn ___pawn, Hediff hediff, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (___pawn == null || hediff?.def == null || !GameFacts.IsFreeColonistSafe(___pawn) || ___pawn.Dead)
                return;
            __state = ___pawn.health.hediffSet.HasHediff(hediff.def);
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }

    public static void Postfix(Pawn ___pawn, Hediff hediff, BodyPartRecord part, DamageInfo? dinfo, bool __state)
    {
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (___pawn == null || hediff?.def == null || !GameFacts.IsFreeColonistSafe(___pawn) || ___pawn.Dead
                || !LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;
            var set = ___pawn.health.hediffSet;
            if (!set.hediffs.Contains(hediff))
                return;
            var attacker = dinfo?.Instigator as Pawn;
            var facts = new HediffFacts
            {
                Id = hediff.GetUniqueLoadID(),
                PawnId = GameFacts.Id(___pawn),
                Disease = hediff.def.makesSickThought && !__state,
                Infection = hediff.def == HediffDefOf.WoundInfection,
                FoodPoisoning = hediff.def == HediffDefOf.FoodPoisoning && !__state,
                Addiction = hediff is Hediff_Addiction && !__state,
                Overdose = hediff.def == HediffDefOf.DrugOverdose && !__state,
                MissingPart = hediff is Hediff_MissingPart && part != null && MedicalProbe.SurgeryDepth == 0 && (part.parent == null || !set.PartIsMissing(part.parent)),
                Injury = hediff is Hediff_Injury,
                WildAnimalAttack = hediff is Hediff_Injury && attacker != null && attacker.RaceProps.Animal && attacker.Faction == null,
            };
            var tick = GameFacts.Tick;
            GameFacts.Record(MedicineFamily.Name, s => MedicineFamily.HediffAdded(s, facts, tick));
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }
}

/// <summary>A tracked hediff removed: a cure when it resolved by itself on a living pawn with the part intact, outside surgery.</summary>
[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
public static class RemoveHediffMetricPatch
{
    public static void Prefix(Pawn ___pawn, Hediff hediff)
    {
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (___pawn == null || hediff == null || !MedicalProbe.CanBeTrackedHediff(hediff))
                return;
            if (!UnityData.IsInMainThread && !___pawn.Spawned && !GameFacts.IsPlayerFaction(___pawn.Faction) && !GameFacts.IsPrisonerOfColonySafe(___pawn))
                return;
            if (!LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;
            var id = hediff.GetUniqueLoadID();
            var cured = !___pawn.Dead && MedicalProbe.SurgeryDepth == 0 && hediff.ShouldRemove
                && (hediff.Part == null || !___pawn.health.hediffSet.PartIsMissing(hediff.Part));
            var tick = GameFacts.Tick;
            GameFacts.Record(MedicineFamily.Name, s =>
            {
                if (cured)
                    MedicineFamily.HediffCured(s, id, tick);
                else
                    MedicineFamily.HediffGone(s, id);
            });
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }
}

[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(HediffComp_GetsPermanent), nameof(HediffComp_GetsPermanent.PreFinalizeInjury))]
public static class ScarOnInjuryMetricPatch
{
    public static void Prefix(HediffComp_GetsPermanent __instance, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try { __state = __instance is { IsPermanent: true }; }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }

    public static void Postfix(HediffComp_GetsPermanent __instance, bool __state)
        => ScarProbe.Check(__instance, __state);
}

[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(HediffComp_GetsPermanent), nameof(HediffComp_GetsPermanent.CompPostInjuryHeal))]
public static class ScarOnHealMetricPatch
{
    public static void Prefix(HediffComp_GetsPermanent __instance, out bool __state)
    {
        __state = false;
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try { __state = __instance is { IsPermanent: true }; }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }

    public static void Postfix(HediffComp_GetsPermanent __instance, bool __state)
        => ScarProbe.Check(__instance, __state);
}

internal static class ScarProbe
{
    public static void Check(HediffComp_GetsPermanent comp, bool wasPermanent)
    {
        if (wasPermanent || !LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (comp is { IsPermanent: true } && GameFacts.IsFreeColonistSafe(comp.Pawn))
                GameFacts.Record(MedicineFamily.Name, MedicineFamily.Scar);
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }
}

/// <summary>
/// N3-H-005 (1.7.4985): <c>Need_Chemical.CurLevel</c>'s setter calls <c>CategoryChanged</c> → this method only when the need's
/// category changed, so finding the need in withdrawal here is an onset. Called rarely (category changes of addicted pawns).
/// </summary>
[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(Hediff_Addiction), nameof(Hediff_Addiction.Notify_NeedCategoryChanged))]
public static class WithdrawalMetricPatch
{
    public static void Postfix(Hediff_Addiction __instance)
    {
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            var pawn = __instance?.pawn;
            if (pawn == null || __instance?.Need is not { } need)
                return;
            var withdrawal = need.CurCategory == DrugDesireCategory.Withdrawal;
            // An onset counts for a living free colonist only; leaving withdrawal is passed on for anyone, so a pawn that was
            // a prisoner or away when it recovered is not left marked as still in withdrawal.
            if (withdrawal && (pawn.Dead || !GameFacts.IsFreeColonistSafe(pawn)))
                return;
            if (!withdrawal && !UnityData.IsInMainThread && !pawn.Spawned && !GameFacts.IsPlayerFaction(pawn.Faction) && !GameFacts.IsPrisonerOfColonySafe(pawn))
                return;
            if (!LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;
            var id = __instance.GetUniqueLoadID();
            var tick = GameFacts.Tick;
            GameFacts.Record(MedicineFamily.Name, s => MedicineFamily.AddictionNeedChanged(s, id, withdrawal, tick));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(MedicineFamily.Name, e);
        }
    }
}

/// <summary>
/// N3-H-011 (1.7.4985): <c>Toils_LayDown.LayDown</c> builds every toil that lays a pawn in a bed (its init is the only code that
/// sets <c>PawnPosture.LayingInBed</c>); its finish actions run when the pawn stops lying there (next toil, job end, death,
/// despawn). The factory's postfix appends to that toil's own init and adds a finish action, once per toil built (at a job's
/// start or a load), so nothing runs per tick. Deathrest is not a medical stay.
/// </summary>
[MetricAdapter(MedicineFamily.Name)]
[HarmonyPatch(typeof(Toils_LayDown), nameof(Toils_LayDown.LayDown))]
public static class HospitalBedMetricPatch
{
    public static void Postfix(Toil __result, TargetIndex bedOrRestSpotIndex, bool hasBed, bool deathrest)
    {
        if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
            return;
        try
        {
            if (__result == null || !hasBed || deathrest)
                return;
            var toil = __result;
            var init = toil.initAction;
            toil.initAction = () =>
            {
                init?.Invoke();   // the game's own init first; its exceptions stay the game's
                HospitalProbe.Entered(toil, bedOrRestSpotIndex);
            };
            toil.AddFinishAction(() => HospitalProbe.Left(toil));
        }
        catch (Exception e) { LiveMetrics.Fail(MedicineFamily.Name, e); }
    }
}

internal static class HospitalProbe
{
    public static void Entered(Toil toil, TargetIndex bedIndex)
    {
        try
        {
            if (!LiveMetrics.IsRecordingUnit(MedicineFamily.Name))
                return;
            var pawn = toil.actor;
            if (pawn == null || pawn.Dead || !GameFacts.IsFreeColonistSafe(pawn) || !LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;
            // The game's init ends the job instead when the pawn is not in the bed; then it is not lying there.
            if (pawn.jobs?.posture != PawnPosture.LayingInBed || pawn.CurJob?.GetTarget(bedIndex).Thing is not Building_Bed { Medical: true } bed
                || pawn.CurrentBed() != bed)
                return;
            var id = GameFacts.Id(pawn);
            long bedId = bed.thingIDNumber;
            var tick = GameFacts.Tick;
            GameFacts.Record(MedicineFamily.Name, s => MedicineFamily.BedEntered(s, id, bedId, tick));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(MedicineFamily.Name, e);
        }
    }

    public static void Left(Toil toil)
    {
        try
        {
            var pawn = toil.actor;
            if (pawn == null || !LiveMetrics.CanCaptureEvent(MedicineFamily.Name))
                return;
            var id = GameFacts.Id(pawn);
            var died = pawn.Dead;
            var tick = GameFacts.Tick;
            GameFacts.Record(MedicineFamily.Name, s => MedicineFamily.BedLeft(s, id, died, tick));
        }
        catch (Exception e)
        {
            LiveMetrics.Fail(MedicineFamily.Name, e);
        }
    }
}
