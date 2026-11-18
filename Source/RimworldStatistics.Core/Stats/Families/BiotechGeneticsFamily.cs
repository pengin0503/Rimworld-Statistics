namespace RimworldStatistics.Core;

/// <summary>
/// Biotech DLC expansion live events (§4.1.5): growth vat completions, gene extractions,
/// xenogerm recombinations, gene implantations, and exact first-defeat ticks for mechanitor bosses.
/// </summary>
public static class BiotechGeneticsFamily
{
    public const string Name = "biotech_genetics";
    public const string GrowthVatCompletions = "biotech.growth_vat.completions";
    public const string GeneExtractions = "biotech.genetics.extractions";
    public const string XenogermsAssembled = "biotech.genetics.xenogerms_assembled";
    public const string GenesImplanted = "biotech.genetics.implanted";
    public const string BossFirstDefeatedTicks = "biotech.bossgroups.first_defeated_ticks";

    private const string VatCount = "growth_vat";
    private const string ExtractCount = "gene_extractions";
    private const string AssembleCount = "xenogerms_assembled";
    private const string ImplantIds = "implant_event_ids";
    private const string ImplantCount = "genes_implanted";
    private const string BossDefeatTicks = "boss_defeat_ticks";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name,
        Revision = 1,
        RequiredDlc = AcquisitionUnits.Biotech,
        Build = Build,
    };

    /// <summary>
    /// The biological age at which a growth vat ejects its occupant on its own (Building_GrowthVat.AgeToEject, 1.7.5011): its
    /// tick finishes the cycle once <c>AgeBiologicalYearsFloat &gt;= 18f</c>.
    /// </summary>
    public const float GrowthVatEjectAge = 18f;

    /// <summary>
    /// Whether a child leaving a growth vat at <paramref name="biologicalAgeYears"/> completed its growth (#391). "Cancel vatgrowth"
    /// ejects a younger child through the same FinishPawn; that ends the cycle early and is not a completion.
    /// </summary>
    public static bool IsGrowthVatCompletionAge(float biologicalAgeYears) => biologicalAgeYears >= GrowthVatEjectAge;

    /// <summary>Records a completed growth vat cycle (living child growth completion or living vat birth).</summary>
    public static void GrowthVatCompleted(FamilyState s, bool playerVat, bool livingResult)
    {
        if (playerVat && livingResult)
            s.Count(VatCount);
    }

    /// <summary>Records a completed gene extraction that produced a genepack on a player gene extractor.</summary>
    public static void GeneExtracted(FamilyState s, bool playerExtractor, bool genepackProduced)
    {
        if (playerExtractor && genepackProduced)
            s.Count(ExtractCount);
    }

    /// <summary>Records a completed xenogerm recombination on a player gene assembler.</summary>
    public static void XenogermAssembled(FamilyState s, bool playerAssembler, bool xenogermProduced)
    {
        if (playerAssembler && xenogermProduced)
            s.Count(AssembleCount);
    }

    /// <summary>Records a completed xenogerm implantation or reimplantation involving a colony pawn.</summary>
    public static void XenogermImplanted(FamilyState s, string eventId, bool colonyAffiliated, bool implanted)
    {
        if (!colonyAffiliated || !implanted || string.IsNullOrWhiteSpace(eventId))
            return;
        if (s.Distinct(ImplantIds, eventId))
            s.Count(ImplantCount);
    }

    /// <summary>Records the exact game tick when a bossgroup was defeated for the first time in this colony.</summary>
    public static void BossFirstDefeated(FamilyState s, string qualifiedBossDef, int tick)
    {
        if (tick < 0 || string.IsNullOrWhiteSpace(qualifiedBossDef))
            return;
        var acc = s.Acc(BossDefeatTicks);
        acc.By ??= new Dictionary<string, double>(StringComparer.Ordinal);
        if (acc.By.ContainsKey(qualifiedBossDef))
            return;
        if (acc.By.Count >= FamilyOps.KeyCap)
        {
            acc.Overflow = true;
            return;
        }
        acc.By[qualifiedBossDef] = tick / (double)StatFormat.TicksPerDay;
        acc.N = (acc.N ?? 0) + 1;
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(VatCount, GrowthVatCompletions);
        c.PutCount(ExtractCount, GeneExtractions);
        c.PutCount(AssembleCount, XenogermsAssembled);
        c.PutCount(ImplantCount, GenesImplanted);

        var s = c.State;
        if (s.Peek(ImplantIds)?.Overflow == true)
            c.MarkPartial(GenesImplanted);

        var bossAcc = s.Peek(BossDefeatTicks);
        var bossValues = new Dictionary<string, MetricValue>(StringComparer.Ordinal);
        if (bossAcc?.By is { } map)
        {
            foreach (var (bossKey, day) in map)
                bossValues[bossKey] = c.Value(real: day, partial: bossAcc.Overflow);
        }
        c.PutBreakdown(
            BossFirstDefeatedTicks,
            bossValues,
            c.Value(integer: bossValues.Count, partial: bossAcc?.Overflow == true));
    }
}
