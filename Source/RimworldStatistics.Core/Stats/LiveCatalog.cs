namespace RimworldStatistics.Core;

/// <summary>
/// The outputs of the acquisition units of Phases B, C and D2 (<see cref="AcquisitionUnits"/>). They are in the catalog with
/// their stored keys, units, aggregation and translation keys, so a unit is published by marking it verified, nothing else.
/// Order is the display order within each category.
/// </summary>
public static class LiveCatalog
{
    private const MetricUnit Count = MetricUnit.Count;
    private const MetricUnit Days = MetricUnit.Days;
    private const OverallAggregation Sum = OverallAggregation.Sum;
    private const OverallAggregation Max = OverallAggregation.Max;
    private const OverallAggregation Min = OverallAggregation.Min;
    private const OverallAggregation Ratio = OverallAggregation.Ratio;
    private const OverallAggregation Each = OverallAggregation.IndividualOnly;

    /// <summary>An output of a live family (stored in the checkpoint).</summary>
    private static MetricDefinition Live(string key, string ledger, MetricCategory category, MetricUnit unit, OverallAggregation overall, string family,
        string label, string description, bool breakdown = false, MetricUnit? breakdownUnit = null, bool signed = false, string? stored = null,
        string? dlc = null)
        => new()
        {
            Key = key, StoredKey = stored ?? key, LedgerIds = ledger, Category = category, Unit = unit, Provenance = MetricProvenance.LiveAggregate,
            Source = MetricObservationSource.Checkpoint, Overall = overall, Signed = signed, HasBreakdown = breakdown, BreakdownUnit = breakdownUnit,
            RequiredDlc = dlc ?? AcquisitionUnits.Find(family)?.RequiredDlc, Acquisition = family, LabelKey = label, DescriptionKey = description,
        };

    private const string Biotech = AcquisitionUnits.Biotech;
    private const string Royalty = AcquisitionUnits.Royalty;
    private const MetricCategory Combat = MetricCategory.Combat;
    private const MetricCategory Pop = MetricCategory.Population;
    private const MetricCategory Defense = MetricCategory.Defense;
    private const MetricCategory Social = MetricCategory.Social;
    private const MetricCategory Animals = MetricCategory.Animals;
    private const MetricUnit Years = MetricUnit.Years;
    private const MetricUnit Percent = MetricUnit.Percent;

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        // C1 · research
        Live(ResearchFamily.Completed, "Q-001", MetricCategory.Research, Count, Sum, ResearchFamily.Name, "RWS_Metric_ResearchLiveCompleted", "RWS_MetricDesc_ResearchLiveCompleted"),
        Live(ResearchFamily.CompletedTicks, "Q-002", MetricCategory.Research, Count, Each, ResearchFamily.Name, "RWS_Metric_ResearchLiveCompletedTicks", "RWS_MetricDesc_ResearchLiveCompletedTicks",
            breakdown: true, breakdownUnit: Days),
        Live(ResearchFamily.DurationMin, "Q-003", MetricCategory.Research, Days, Min, ResearchFamily.Name, "RWS_Metric_ResearchLiveDurationMin", "RWS_MetricDesc_ResearchLiveDurationMin"),
        Live(ResearchFamily.DurationMax, "Q-004, N3-W-011", MetricCategory.Research, Days, Max, ResearchFamily.Name, "RWS_Metric_ResearchLiveDurationMax", "RWS_MetricDesc_ResearchLiveDurationMax"),
        Live(ResearchFamily.FirstDay, "N5-M-004", MetricCategory.Research, Days, Min, ResearchFamily.Name, "RWS_Metric_ResearchLiveFirstDay", "RWS_MetricDesc_ResearchLiveFirstDay"),

        // C1 · mental
        Live(MentalFamily.Breaks, "P-005", MetricCategory.Mind, Count, Sum, MentalFamily.Name, "RWS_Metric_MentalBreaks", "RWS_MetricDesc_MentalBreaks"),
        Live(MentalFamily.BreaksByDef, "P-006", MetricCategory.Mind, Count, Sum, MentalFamily.Name, "RWS_Metric_MentalBreaksByDef", "RWS_MetricDesc_MentalBreaksByDef", breakdown: true),
        Live(MentalFamily.Inspirations, "P-025", MetricCategory.Mind, Count, Sum, MentalFamily.Name, "RWS_Metric_Inspirations", "RWS_MetricDesc_Inspirations"),
        Live(MentalFamily.InspirationsByDef, "P-025", MetricCategory.Mind, Count, Sum, MentalFamily.Name, "RWS_Metric_InspirationsByDef", "RWS_MetricDesc_InspirationsByDef", breakdown: true),

        // C2 · health_death
        Live(HealthDeathFamily.HostileKills, "B-001", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_HostileKills", "RWS_MetricDesc_HostileKills"),
        Live(HealthDeathFamily.KillsPerPawnMax, "B-022", Combat, Count, Max, HealthDeathFamily.Name, "RWS_Metric_KillsPerPawnMax", "RWS_MetricDesc_KillsPerPawnMax", breakdown: true),
        Live(HealthDeathFamily.KillsByVictimKind, "N1-B-006", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_KillsByVictimKind", "RWS_MetricDesc_KillsByVictimKind", breakdown: true),
        Live(HealthDeathFamily.RangedKills, "N5-B-001", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_RangedKills", "RWS_MetricDesc_RangedKills"),
        Live(HealthDeathFamily.MeleeKills, "N5-B-002", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_MeleeKills", "RWS_MetricDesc_MeleeKills"),
        Live(HealthDeathFamily.UnarmedKills, "N5-B-003", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_UnarmedKills", "RWS_MetricDesc_UnarmedKills"),
        Live(HealthDeathFamily.ExplosiveKills, "N5-B-004", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_ExplosiveKills", "RWS_MetricDesc_ExplosiveKills"),
        Live(HealthDeathFamily.EnvironmentKills, "N5-B-005", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_EnvironmentKills", "RWS_MetricDesc_EnvironmentKills"),
        Live(HealthDeathFamily.TurretKills, "N2-C-005", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_TurretKills", "RWS_MetricDesc_TurretKills"),
        Live(HealthDeathFamily.TrapKills, "N1-B-004", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_TrapKills", "RWS_MetricDesc_TrapKills"),
        Live(HealthDeathFamily.TameAnimalKills, "N2-C-006", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_TameAnimalKills", "RWS_MetricDesc_TameAnimalKills"),
        Live(HealthDeathFamily.PlayerMechKills, "N5-B-009", Combat, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_PlayerMechKills", "RWS_MetricDesc_PlayerMechKills", dlc: Biotech),
        Live(HealthDeathFamily.DeathCauses, "P-004", Pop, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_DeathCauses", "RWS_MetricDesc_DeathCauses", breakdown: true),
        Live(HealthDeathFamily.DeathsByRaid, "B-021", Pop, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_DeathsByRaid", "RWS_MetricDesc_DeathsByRaid", breakdown: true),
        Live(HealthDeathFamily.DiseaseDeaths, "N3-H-003", Pop, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_DiseaseDeaths", "RWS_MetricDesc_DiseaseDeaths"),
        Live(HealthDeathFamily.DiseaseDeathRate, "N1-C-009", Pop, Percent, Ratio, HealthDeathFamily.Name, "RWS_Metric_DiseaseDeathRate", "RWS_MetricDesc_DiseaseDeathRate"),
        Live(HealthDeathFamily.LongestDeathGap, "B-020", Pop, Days, Max, HealthDeathFamily.Name, "RWS_Metric_LongestDeathGap", "RWS_MetricDesc_LongestDeathGap"),
        Live(HealthDeathFamily.FirstDeathDay, "N5-M-003", Pop, Days, Min, HealthDeathFamily.Name, "RWS_Metric_FirstDeathDay", "RWS_MetricDesc_FirstDeathDay"),
        Live(HealthDeathFamily.OldestDeathAge, "N1-C-002, P-010", Pop, Years, Max, HealthDeathFamily.Name, "RWS_Metric_OldestDeathAge", "RWS_MetricDesc_OldestDeathAge"),
        Live(HealthDeathFamily.CaravanMemberDeaths, "N3-X-006", Pop, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_CaravanMemberDeaths", "RWS_MetricDesc_CaravanMemberDeaths"),
        Live(HealthDeathFamily.ColonyAnimalDeaths, "A-003", Animals, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_ColonyAnimalDeaths", "RWS_MetricDesc_ColonyAnimalDeaths"),
        Live(HealthDeathFamily.ColonyAnimalRaidDeaths, "N2-C-009", Animals, Count, Sum, HealthDeathFamily.Name, "RWS_Metric_ColonyAnimalRaidDeaths", "RWS_MetricDesc_ColonyAnimalRaidDeaths"),

        // C2 · down
        Live(DownFamily.ColonistsDowned, "B-013", Combat, Count, Sum, DownFamily.Name, "RWS_Metric_ColonistsDowned", "RWS_MetricDesc_ColonistsDowned"),
        Live(DownFamily.EnemiesDowned, "B-014, B-002", Combat, Count, Sum, DownFamily.Name, "RWS_Metric_EnemiesDowned", "RWS_MetricDesc_EnemiesDowned"),
        Live(DownFamily.ToxicDowns, "N4-C-011", Combat, Count, Sum, DownFamily.Name, "RWS_Metric_ToxicDowns", "RWS_MetricDesc_ToxicDowns", dlc: Biotech),
        Live(DownFamily.SocialFightDowns, "N4-P-009", Combat, Count, Sum, DownFamily.Name, "RWS_Metric_SocialFightDowns", "RWS_MetricDesc_SocialFightDowns"),

        // C2 · raid
        Live(RaidFamily.ByArrivalMode, "B-005", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_RaidsByArrivalMode", "RWS_MetricDesc_RaidsByArrivalMode", breakdown: true),
        Live(RaidFamily.ByFaction, "N3-X-011", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_RaidsByFaction", "RWS_MetricDesc_RaidsByFaction", breakdown: true),
        Live(RaidFamily.PawnsMax, "B-004", Defense, Count, Max, RaidFamily.Name, "RWS_Metric_RaidPawnsMax", "RWS_MetricDesc_RaidPawnsMax"),
        Live(RaidFamily.PointsMax, "N1-B-007", Defense, Count, Max, RaidFamily.Name, "RWS_Metric_RaidPointsMax", "RWS_MetricDesc_RaidPointsMax"),
        Live(RaidFamily.KillsMax, "B-016", Defense, Count, Max, RaidFamily.Name, "RWS_Metric_RaidKillsMax", "RWS_MetricDesc_RaidKillsMax"),
        Live(RaidFamily.Sieges, "B-026", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_RaidSieges", "RWS_MetricDesc_RaidSieges"),
        Live(RaidFamily.Breaches, "B-027", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_RaidBreaches", "RWS_MetricDesc_RaidBreaches"),
        Live(RaidFamily.Drops, "B-028", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_RaidDrops", "RWS_MetricDesc_RaidDrops"),
        Live(RaidFamily.Infestations, "B-023", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_Infestations", "RWS_MetricDesc_Infestations"),
        Live(RaidFamily.MechClusters, "B-024", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_MechClusters", "RWS_MetricDesc_MechClusters", dlc: Royalty),
        Live(RaidFamily.Manhunters, "B-025", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_ManhunterPacks", "RWS_MetricDesc_ManhunterPacks"),
        Live(RaidFamily.LongestGap, "B-008, N2-C-002", Defense, Days, Max, RaidFamily.Name, "RWS_Metric_RaidLongestGap", "RWS_MetricDesc_RaidLongestGap"),
        Live(RaidFamily.MeanGap, "N2-C-001", Defense, Days, Ratio, RaidFamily.Name, "RWS_Metric_RaidMeanGap", "RWS_MetricDesc_RaidMeanGap"),
        Live(RaidFamily.FirstDay, "N5-M-001", Defense, Days, Min, RaidFamily.Name, "RWS_Metric_RaidFirstDay", "RWS_MetricDesc_RaidFirstDay"),
        Live(RaidFamily.Retreated, "N2-C-011", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_RaidsRetreated", "RWS_MetricDesc_RaidsRetreated"),
        Live(RaidFamily.Wiped, "N2-C-012", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_RaidsWiped", "RWS_MetricDesc_RaidsWiped"),
        Live(RaidFamily.RepelledRate, "N1-B-010", Defense, Percent, Ratio, RaidFamily.Name, "RWS_Metric_RaidRepelledRate", "RWS_MetricDesc_RaidRepelledRate"),
        Live(RaidFamily.RepelledNoDown, "N1-B-009", Defense, Count, Sum, RaidFamily.Name, "RWS_Metric_RaidRepelledNoDown", "RWS_MetricDesc_RaidRepelledNoDown"),
        Live(RaidFamily.DurationMean, "N1-B-008", Defense, Days, Ratio, RaidFamily.Name, "RWS_Metric_RaidDurationMean", "RWS_MetricDesc_RaidDurationMean"),
        Live(RaidFamily.DurationMax, "N2-C-004", Defense, Days, Max, RaidFamily.Name, "RWS_Metric_RaidDurationMax", "RWS_MetricDesc_RaidDurationMax"),
        Live(RaidFamily.DurationMinRepelled, "N5-B-008", Defense, Days, Min, RaidFamily.Name, "RWS_Metric_RaidDurationMinRepelled", "RWS_MetricDesc_RaidDurationMinRepelled"),

        // C2 · membership
        Live(MembershipFamily.FirstJoin, "P-001", Pop, Count, Sum, MembershipFamily.Name, "RWS_Metric_FirstJoins", "RWS_MetricDesc_FirstJoins"),
        Live(MembershipFamily.Routes, "N1-C-003", Pop, Count, Sum, MembershipFamily.Name, "RWS_Metric_JoinRoutes", "RWS_MetricDesc_JoinRoutes", breakdown: true),
        Live(MembershipFamily.PrisonerRecruit, "P-002", Pop, Count, Sum, MembershipFamily.Name, "RWS_Metric_PrisonerRecruits", "RWS_MetricDesc_PrisonerRecruits"),
        Live(MembershipFamily.HumanBirths, "P-003, L-011", Pop, Count, Sum, MembershipFamily.Name, "RWS_Metric_HumanBirths", "RWS_MetricDesc_HumanBirths", dlc: Biotech),
        Live(MembershipFamily.Unique, "P-009", Pop, Count, Sum, MembershipFamily.Name, "RWS_Metric_UniqueMembers", "RWS_MetricDesc_UniqueMembers"),
        Live(MembershipFamily.Rejoin, "N1-C-004", Pop, Count, Sum, MembershipFamily.Name, "RWS_Metric_Rejoins", "RWS_MetricDesc_Rejoins"),
        Live(MembershipFamily.JoinAgeMean, "N4-P-003", Pop, Years, Ratio, MembershipFamily.Name, "RWS_Metric_JoinAgeMean", "RWS_MetricDesc_JoinAgeMean"),
        Live(MembershipFamily.FirstJoinDay, "N5-M-002", Pop, Days, Min, MembershipFamily.Name, "RWS_Metric_FirstJoinDay", "RWS_MetricDesc_FirstJoinDay"),
        Live(MembershipFamily.SlavesFreed, "P-018", Social, Count, Sum, MembershipFamily.Name, "RWS_Metric_SlavesFreed", "RWS_MetricDesc_SlavesFreed"),
        Live(MembershipFamily.RecruitTimeMin, "P-027", Social, Days, Min, MembershipFamily.Name, "RWS_Metric_RecruitTimeMin", "RWS_MetricDesc_RecruitTimeMin"),
        Live(MembershipFamily.RecruitTimeMax, "P-027", Social, Days, Max, MembershipFamily.Name, "RWS_Metric_RecruitTimeMax", "RWS_MetricDesc_RecruitTimeMax"),
        Live(MembershipFamily.Kidnaps, "P-028", Social, Count, Sum, MembershipFamily.Name, "RWS_Metric_Kidnaps", "RWS_MetricDesc_Kidnaps"),
        Live(MembershipFamily.KidnapRescues, "N4-P-005", Social, Count, Sum, MembershipFamily.Name, "RWS_Metric_KidnapRescues", "RWS_MetricDesc_KidnapRescues"),
        Live(MembershipFamily.CaptiveMax, "N4-P-006", Social, Days, Max, MembershipFamily.Name, "RWS_Metric_CaptiveMax", "RWS_MetricDesc_CaptiveMax"),

        // C2 · lifetime
        Live(LifetimeFamily.LifeMean, "P-020", Pop, Years, Ratio, LifetimeFamily.Name, "RWS_Metric_LifeMean", "RWS_MetricDesc_LifeMean"),
        Live(LifetimeFamily.TenureMean, "P-021", Pop, Days, Ratio, LifetimeFamily.Name, "RWS_Metric_TenureMean", "RWS_MetricDesc_TenureMean"),
        Live(LifetimeFamily.FirstSkill20, "P-023", Pop, Count, Sum, LifetimeFamily.Name, "RWS_Metric_FirstSkill20", "RWS_MetricDesc_FirstSkill20"),
        Live(LifetimeFamily.JoinGainPeak, "P-024", Pop, Count, Max, LifetimeFamily.Name, "RWS_Metric_JoinGainPeak", "RWS_MetricDesc_JoinGainPeak"),

        // C2 · social
        Live(SocialFamily.Marriages, "P-012", Social, Count, Sum, SocialFamily.Name, "RWS_Metric_Marriages", "RWS_MetricDesc_Marriages"),
        Live(SocialFamily.Divorces, "P-013", Social, Count, Sum, SocialFamily.Name, "RWS_Metric_Divorces", "RWS_MetricDesc_Divorces"),
        Live(SocialFamily.Lovers, "P-014", Social, Count, Sum, SocialFamily.Name, "RWS_Metric_Lovers", "RWS_MetricDesc_Lovers"),
        Live(SocialFamily.Fights, "P-015", Social, Count, Sum, SocialFamily.Name, "RWS_Metric_SocialFights", "RWS_MetricDesc_SocialFights"),
        Live(SocialFamily.PrisonBreaks, "P-016", Social, Count, Sum, SocialFamily.Name, "RWS_Metric_PrisonBreaks", "RWS_MetricDesc_PrisonBreaks"),
        Live(SocialFamily.PrisonersReleased, "P-017", Social, Count, Sum, SocialFamily.Name, "RWS_Metric_PrisonersReleased", "RWS_MetricDesc_PrisonersReleased"),
        Live(SocialFamily.PrisonersCaptured, "B-003", Social, Count, Sum, SocialFamily.Name, "RWS_Metric_PrisonersCaptured", "RWS_MetricDesc_PrisonersCaptured"),
        Live(SocialFamily.AnimalBonds, "N1-D-009", Animals, Count, Sum, SocialFamily.Name, "RWS_Metric_AnimalBonds", "RWS_MetricDesc_AnimalBonds"),

        // C2 · animals
        Live(AnimalsFamily.Tames, "A-001", Animals, Count, Sum, AnimalsFamily.Name, "RWS_Metric_AnimalsTamed", "RWS_MetricDesc_AnimalsTamed"),
        Live(AnimalsFamily.Births, "A-002", Animals, Count, Sum, AnimalsFamily.Name, "RWS_Metric_AnimalBirths", "RWS_MetricDesc_AnimalBirths"),
        Live(AnimalsFamily.Slaughtered, "A-004", Animals, Count, Sum, AnimalsFamily.Name, "RWS_Metric_AnimalsSlaughtered", "RWS_MetricDesc_AnimalsSlaughtered"),
        Live(AnimalsFamily.Hunted, "N1-D-006", Animals, Count, Sum, AnimalsFamily.Name, "RWS_Metric_AnimalsHunted", "RWS_MetricDesc_AnimalsHunted"),
    };
}
