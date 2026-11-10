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
/// Phase C · medicine (ledger P-007, P-008, P-026, M-001…M-007, N1-C-007, N1-C-010, N3-H-001, N3-H-002, N3-H-004, N3-H-005,
/// N3-H-007, N3-H-010, N3-H-011, N3-H-012, N4-C-010, N4-P-001, N4-P-002, N4-P-007, N4-P-008, N3-X-010; evidence #medicine).
/// <para>
/// N3-H-005: in 1.7.4985 withdrawal is stage 1 of <c>Hediff_Addiction</c>, derived from its chemical need's category; the need's
/// level setter calls <c>Hediff_Addiction.Notify_NeedCategoryChanged</c> only when the category actually changes, so a call that
/// finds the need in withdrawal is one onset. The tracker only removes a repeated notification of the same onset.
/// </para>
/// <para>
/// N3-H-011: the only code that sets <c>PawnPosture.LayingInBed</c> is the init of the <c>Toils_LayDown.LayDown</c> toil, and
/// that toil's finish action runs when the pawn stops lying there. A stay is a free colonist lying in a medical bed from the
/// first such init to the finish after which the pawn does not lie down in the same bed again within
/// <see cref="StayMergeTicks"/> (a job switch while staying in bed, e.g. to watch television, starts the next lying toil in the
/// same tick). A stay that ends in death, or whose start was not seen, gives no sample.
/// </para>
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
    public const string Withdrawals = "medicine.withdrawal";
    public const string HospitalStayMean = "medicine.hospital.stay_mean";

    /// <summary>Lying toils in the same medical bed this close together (ticks) are one stay.</summary>
    public const int StayMergeTicks = 60;

    // Revision 2 added N3-H-005 and N3-H-011: a revision-1 checkpoint did not measure them and is never continued.
    // An interruption settles the stays whose pawn got up long enough before the game's latest tick; open stays are dropped.
    public static LiveFamily Family { get; } = new()
    {
        Name = Name, Revision = 2, Build = Build, Interrupted = (s, _, latestTick) => SettleStays(s, latestTick),
    };

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
    private const string Withdrawal = "withdrawals";
    private const string InWithdrawal = "withdrawal";  // active: addiction hediff id → onset tick
    private const string Stay = "hospital_stay";       // samples: completed stays (ticks)
    private const string StayOpen = "stay";            // active: pawn id → tick the stay began
    private const string StayBed = "stay_bed";         // active: pawn id → the bed's thing number
    private const string StayLeft = "stay_left";       // active: pawn id → tick the pawn stopped lying there (not settled yet)

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
        s.Untrack(InWithdrawal, id);
    }

    /// <summary>A tracked hediff was removed some other way: its interval is dropped.</summary>
    public static void HediffGone(FamilyState s, string id)
    {
        s.Untrack(DiseaseOpen, id);
        s.Untrack(InfectionOpen, id);
        s.Untrack(InWithdrawal, id);
    }

    /// <summary>
    /// A free colonist's vanilla addiction saw its chemical need change category (Hediff_Addiction.Notify_NeedCategoryChanged).
    /// Entering withdrawal is one onset; the same withdrawal notified again is not counted twice.
    /// </summary>
    public static void AddictionNeedChanged(FamilyState s, string hediffId, bool withdrawal, int tick)
    {
        if (!withdrawal)
        {
            s.Untrack(InWithdrawal, hediffId);
            return;
        }
        if (s.Tracked(InWithdrawal, hediffId) is not null)
            return;
        // Past the cap the onset still counts (the notification fires only on a real change); only the repeat guard is lost.
        s.Track(InWithdrawal, hediffId, tick);
        s.Count(Withdrawal);
    }

    /// <summary>
    /// A free colonist began lying in a medical bed (the init of a Toils_LayDown.LayDown toil). Lying down again in the same bed
    /// within <see cref="StayMergeTicks"/> of getting up continues the stay.
    /// </summary>
    public static void BedEntered(FamilyState s, string pawnId, long bedId, int tick)
    {
        SettleStays(s, tick);
        if (s.Tracked(StayOpen, pawnId) is { } start)
        {
            var left = s.Tracked(StayLeft, pawnId);
            if (s.Tracked(StayBed, pawnId) == bedId)
            {
                s.Untrack(StayLeft, pawnId);   // a job switch in the same bed: still the same stay
                return;
            }
            if (left is { } l)
                Complete(s, pawnId, start, l);   // moved to another bed at once: the first stay ended when the pawn got up
            else
                DropStay(s, pawnId);             // lying in another bed without having left this one: its end is unknown
        }
        if (s.Track(StayOpen, pawnId, tick))
            s.Track(StayBed, pawnId, bedId);
    }

    /// <summary>
    /// A lying toil of a pawn whose stay is tracked finished (the toil's finish action). The stay ends there unless the pawn lies
    /// down again in the same bed soon; one that ends in death is dropped.
    /// </summary>
    public static void BedLeft(FamilyState s, string pawnId, bool died, int tick)
    {
        if (s.Tracked(StayOpen, pawnId) is null)
            return;
        if (died)
        {
            DropStay(s, pawnId);
            return;
        }
        if (s.Tracked(StayLeft, pawnId) is null)
            s.Track(StayLeft, pawnId, tick);
    }

    /// <summary>Read only (the verification harness): whether a stay of the pawn is open, and whether the pawn has got up from it.</summary>
    public static (bool Open, bool Left) StayState(FamilyState s, string pawnId)
        => (s.Tracked(StayOpen, pawnId) is not null, s.Tracked(StayLeft, pawnId) is not null);

    /// <summary>Stays whose pawn got up more than <see cref="StayMergeTicks"/> before <paramref name="tick"/> are complete.</summary>
    internal static void SettleStays(FamilyState s, int tick)
    {
        if (!s.Active.TryGetValue(StayLeft, out var left) || left.Count == 0)
            return;
        foreach (var (pawnId, leftTick) in left.Where(kv => tick - kv.Value > StayMergeTicks).ToList())
            if (s.Tracked(StayOpen, pawnId) is { } start)
                Complete(s, pawnId, start, leftTick);
            else
                s.Untrack(StayLeft, pawnId);
    }

    private static void Complete(FamilyState s, string pawnId, long start, long left)
    {
        if (left >= start)
            s.Observe(Stay, left - start);
        DropStay(s, pawnId);
    }

    private static void DropStay(FamilyState s, string pawnId)
    {
        s.Untrack(StayOpen, pawnId);
        s.Untrack(StayBed, pawnId);
        s.Untrack(StayLeft, pawnId);
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
        // Keep the original scalar as partial when an old or damaged checkpoint
        // is inconsistent; never propagate an impossible ratio into totals.
        var validSurgeryCounts = n >= 0 && ok >= 0 && ok <= n;
        if (!validSurgeryCounts)
        {
            c.Put(Surgeries, c.Value(integer: n, partial: true));
            c.Put(SurgerySuccessRate, c.Value(coverage: MetricCoverage.Unknown));
        }
        else
        {
            c.PutBreakdown(Surgeries, new Dictionary<string, MetricValue>(StringComparer.Ordinal)
            {
                ["success"] = c.Value(integer: ok),
                ["failure"] = c.Value(integer: n - ok),
            }, c.Value(integer: n));
            c.Put(SurgerySuccessRate, n == 0 ? c.Value(numerator: 0, denominator: 0)
                : c.Value(real: ok / (double)n, numerator: ok, denominator: n));
        }
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
        c.PutCount(Withdrawal, Withdrawals);
        PutStays(c, Days);
        // A hediff dropped at a tracker's cap has no duration sample, and an infection dropped there no recovery.
        if (c.State.TrackerOverflowed(DiseaseOpen))
            c.MarkPartial(DiseaseDurationMean);
        if (c.State.TrackerOverflowed(InfectionOpen))
            c.MarkPartial(InfectionRecoveries, InfectionDurationMax);
        if (c.State.TrackerOverflowed(InWithdrawal))
            c.MarkPartial(Withdrawals);
        if (c.State.TrackerOverflowed(StayOpen) || c.State.TrackerOverflowed(StayBed) || c.State.TrackerOverflowed(StayLeft))
            c.MarkPartial(HospitalStayMean);
    }

    /// <summary>
    /// The mean stay: the completed samples, plus the stays already settled at the end of the measured range but not yet by an event
    /// (read only; the next event settles them for good, so none is counted twice).
    /// </summary>
    private static void PutStays(FamilyOutputContext c, double days)
    {
        var a = c.State.Peek(Stay);
        double sum = a?.Sum ?? 0;
        long n = a?.N ?? 0;
        if (c.State.Active.TryGetValue(StayLeft, out var left))
            foreach (var (pawnId, leftTick) in left)
                if (c.State.ThroughTick - leftTick > StayMergeTicks && c.State.Tracked(StayOpen, pawnId) is { } start && leftTick >= start)
                {
                    sum += leftTick - start;
                    n++;
                }
        c.Put(HospitalStayMean, n == 0
            ? c.Value(coverage: MetricCoverage.NotApplicable, numerator: 0, denominator: 0, samples: 0)
            : c.Value(real: sum * days / n, numerator: sum * days, denominator: n, samples: (int)Math.Min(n, int.MaxValue)));
    }
}
