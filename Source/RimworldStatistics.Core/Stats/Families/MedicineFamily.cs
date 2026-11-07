namespace RimworldStatistics.Core;

/// <summary>A hediff newly added to a free colonist (not merged into an existing one), classified by the adapter.</summary>
public sealed class HediffFacts
{
    public required string Id { get; init; }
    public required string PawnId { get; init; }
    public bool Disease { get; init; }
    public bool Infection { get; init; }
    public bool FoodPoisoning { get; init; }
    public bool Addiction { get; init; }
    public bool Overdose { get; init; }

    /// <summary>A lost part whose parent part is not missing, outside a surgery.</summary>
    public bool MissingPart { get; init; }

    public bool Injury { get; init; }
    public bool WildAnimalAttack { get; init; }
}

/// <summary>
/// Phase C · medicine (ledger P-007, P-008, P-026, M-001…M-007, N1-C-007, N1-C-010, N3-H-001, N3-H-002, N3-H-004, N3-H-007,
/// N3-H-010, N3-H-012, N4-C-010, N4-P-001, N4-P-002, N4-P-007, N4-P-008, N3-X-010; evidence #medicine). Held: N3-H-005
/// (withdrawal is a stage of the addiction hediff, with no confirmed transition notification) and N3-H-011 (entering and leaving
/// a medical bed has no low-cost confirmed notification).
/// </summary>
public static class MedicineFamily
{
    public const string Name = "medicine";

    public const string Rescues = "medicine.rescues";
    public const string Resurrections = "medicine.resurrections";
    public const string Addictions = "medicine.addictions";
    public const string Tends = "medicine.tends";
    public const string Surgeries = "medicine.surgery.count";
    public const string SurgerySuccessRate = "medicine.surgery.success_rate";
    public const string DiseaseOnsets = "medicine.disease.onsets";
    public const string InfectionRecoveries = "medicine.infection.recovery";
    public const string FoodPoisonings = "medicine.food_poisoning";
    public const string PartsLost = "medicine.part_loss";
    public const string Implants = "medicine.implants";
    public const string TendQualityMean = "medicine.tend_quality_mean";
    public const string MedicineUsed = "medicine.consumed";
    public const string DiseaseDurationMean = "medicine.disease.duration_mean";
    public const string InfectionDurationMax = "medicine.infection.duration_max";
    public const string Overdoses = "medicine.overdoses";
    public const string Scars = "medicine.scars";
    public const string BestSurgeon = "medicine.surgery.best_doctor";
    public const string BestTender = "medicine.tends.best_doctor";
    public const string WildAnimalInjuries = "medicine.wild_attack.injuries";
    public const string HostileRescues = "medicine.rescues.hostile";
    public const string NeutralTends = "medicine.tends.neutral";
    public const string ResurrectionsPerPawnMax = "medicine.resurrections.per_pawn_max";
    public const string InjuriesPerPawnMax = "medicine.injuries.per_pawn_max";
    public const string AwayRescues = "medicine.rescues.away";

    public static LiveFamily Family { get; } = new() { Name = Name, Revision = 1, Build = Build };

    private const string TendCount = "tends";
    private const string TendByDoctor = "tends_by_doctor";
    private const string Neutral = "tends_neutral";
    private const string Quality = "tend_quality";
    private const string Used = "medicine_used";
    private const string Surgery_ = "surgeries";
    private const string SurgeryOk = "surgeries_ok";
    private const string SurgeonOk = "surgeries_by_doctor";
    private const string Implant = "implants";
    private const string Rescue = "rescues";
    private const string RescueHostile = "rescues_hostile";
    private const string RescueAway = "rescues_away";
    private const string Raise = "resurrections";
    private const string RaiseByPawn = "resurrections_by_pawn";
    private const string Disease = "disease_onsets";
    private const string Food = "food_poisoning";
    private const string Addiction = "addictions";
    private const string Overdose = "overdoses";
    private const string Missing = "parts_lost";
    private const string Wild = "wild_injuries";
    private const string InjuryByPawn = "injuries_by_pawn";
    private const string Scar_ = "scars";
    private const string Recovered = "infection_recovered";
    private const string DiseaseDuration = "disease_duration";
    private const string InfectionDuration = "infection_duration";
    private const string DiseaseOpen = "disease";       // active: hediff id → onset tick
    private const string InfectionOpen = "infection";

    public static bool IsColonyTend(string? doctorId, bool colonyPatient)
        => doctorId != null || colonyPatient;

    /// <summary>One colony treatment: the qualities applied to each tended hediff, and the medicine it used up.</summary>
    public static void Tended(FamilyState s, string? doctorId, bool colonyPatient, bool neutralPatient,
        IReadOnlyList<double> qualities, int medicineUsed)
    {
        if (!IsColonyTend(doctorId, colonyPatient) || qualities.Count == 0)
            return;
        s.Count(TendCount);
        if (doctorId != null)
            s.CountBy(TendByDoctor, doctorId);
        if (neutralPatient)
            s.Count(Neutral);
        foreach (var q in qualities)
            s.Observe(Quality, q);
        if (medicineUsed > 0)
            s.Count(Used, medicineUsed);
    }

    /// <summary>A surgery whose outcome was decided (Recipe_Surgery.CheckSurgeryFail); an install that succeeded adds an implant.</summary>
    public static void Surgery(FamilyState s, string? surgeonId, bool success, bool implant)
    {
        s.Count(Surgery_);
        if (!success)
            return;
        s.Count(SurgeryOk);
        if (surgeonId != null)
            s.CountBy(SurgeonOk, surgeonId);
        if (implant)
            s.Count(Implant);
    }

    public static void Rescued(FamilyState s, bool hostile, bool away)
    {
        s.Count(Rescue);
        if (hostile)
            s.Count(RescueHostile);
        if (away)
            s.Count(RescueAway);
    }

    public static void Resurrected(FamilyState s, string pawnId)
    {
        s.Count(Raise);
        s.CountBy(RaiseByPawn, pawnId);
    }

    public static void HediffAdded(FamilyState s, HediffFacts h, int tick)
    {
        if (h.Disease)
        {
            s.Count(Disease);
            s.Track(DiseaseOpen, h.Id, tick);
        }
        if (h.Infection)
            s.Track(InfectionOpen, h.Id, tick);
        if (h.FoodPoisoning)
            s.Count(Food);
        if (h.Addiction)
            s.Count(Addiction);
        if (h.Overdose)
            s.Count(Overdose);
        if (h.MissingPart)
            s.Count(Missing);
        if (h.Injury)
        {
            s.CountBy(InjuryByPawn, h.PawnId);
            if (h.WildAnimalAttack)
                s.Count(Wild);
        }
    }

    /// <summary>A tracked hediff resolved by healing or immunity (not by death, amputation or surgery).</summary>
    public static void HediffCured(FamilyState s, string id, int tick)
    {
        if (s.Untrack(DiseaseOpen, id) is { } d && tick >= d)
            s.Observe(DiseaseDuration, tick - d);
        if (s.Untrack(InfectionOpen, id) is { } i && tick >= i)
        {
            s.Count(Recovered);
            s.Observe(InfectionDuration, tick - i);
        }
    }

    /// <summary>A tracked hediff was removed some other way: its interval is dropped.</summary>
    public static void HediffGone(FamilyState s, string id)
    {
        s.Untrack(DiseaseOpen, id);
        s.Untrack(InfectionOpen, id);
    }

    public static void Scar(FamilyState s) => s.Count(Scar_);

    private static void Build(FamilyOutputContext c)
    {
        const double Days = 1.0 / StatFormat.TicksPerDay;
        c.PutCount(TendCount, Tends);
        c.PutTop(TendByDoctor, BestTender);
        c.PutCount(Neutral, NeutralTends);
        c.PutMean(Quality, TendQualityMean);
        c.PutCount(Used, MedicineUsed);
        var n = c.State.Peek(Surgery_)?.N ?? 0;
        var ok = c.State.Peek(SurgeryOk)?.N ?? 0;
        c.Put(Surgeries, c.Value(integer: n));
        c.Put(SurgerySuccessRate, n == 0 ? c.Value(numerator: 0, denominator: 0) : c.Value(real: ok / (double)n, numerator: ok, denominator: n));
        c.PutTop(SurgeonOk, BestSurgeon);
        c.PutCount(Implant, Implants);
        c.PutCount(Rescue, Rescues);
        c.PutCount(RescueHostile, HostileRescues);
        c.PutCount(RescueAway, AwayRescues);
        c.PutCount(Raise, Resurrections);
        c.PutTop(RaiseByPawn, ResurrectionsPerPawnMax);
        c.PutCount(Disease, DiseaseOnsets);
        c.PutCount(Food, FoodPoisonings);
        c.PutCount(Addiction, Addictions);
        c.PutCount(Overdose, Overdoses);
        c.PutCount(Missing, PartsLost);
        c.PutCount(Wild, WildAnimalInjuries);
        c.PutTop(InjuryByPawn, InjuriesPerPawnMax);
        c.PutCount(Scar_, Scars);
        c.PutCount(Recovered, InfectionRecoveries);
        c.PutMean(DiseaseDuration, DiseaseDurationMean, Days);
        c.PutExtreme(InfectionDuration, InfectionDurationMax, max: true, Days);
        // A hediff dropped at a tracker's cap has no duration sample, and an infection dropped there no recovery.
        if (c.State.TrackerOverflowed(DiseaseOpen))
            c.MarkPartial(DiseaseDurationMean);
        if (c.State.TrackerOverflowed(InfectionOpen))
            c.MarkPartial(InfectionRecoveries, InfectionDurationMax);
    }
}
