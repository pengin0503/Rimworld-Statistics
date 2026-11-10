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
    private const MetricUnit Silver = MetricUnit.Silver;
    private const MetricCategory Work = MetricCategory.Work;
    private const MetricCategory Economy = MetricCategory.Economy;
    private const MetricCategory Medicine = MetricCategory.Medicine;
    private const MetricCategory Env = MetricCategory.Environment;
    private const MetricCategory Explore = MetricCategory.Exploration;

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

        // C3 · work
        Live(WorkDetailsFamily.CraftedByDef, "P1-W-001", Work, Count, Sum, WorkDetailsFamily.Name, "RWS_Metric_CraftedByDef", "RWS_MetricDesc_CraftedByDef", breakdown: true),
        Live(WorkDetailsFamily.MinedByDef, "P1-W-002", Work, Count, Sum, WorkDetailsFamily.Name, "RWS_Metric_MinedByDef", "RWS_MetricDesc_MinedByDef", breakdown: true),
        Live(WorkFamily.CraftedUnits, "W-003", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_CraftedUnits", "RWS_MetricDesc_CraftedUnits"),
        Live(WorkFamily.QualityBreakdown, "W-006", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_CraftQuality", "RWS_MetricDesc_CraftQuality", breakdown: true),
        Live(WorkFamily.Legendary, "W-010", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_CraftLegendary", "RWS_MetricDesc_CraftLegendary"),
        Live(WorkFamily.Masterwork, "W-011", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_CraftMasterwork", "RWS_MetricDesc_CraftMasterwork"),
        Live(WorkFamily.BestQuality, "W-005", Work, Count, Max, WorkFamily.Name, "RWS_Metric_CraftBestQuality", "RWS_MetricDesc_CraftBestQuality", breakdown: true),
        Live(WorkFamily.BestPrice, "W-012", Work, Silver, Max, WorkFamily.Name, "RWS_Metric_CraftBestPrice", "RWS_MetricDesc_CraftBestPrice"),
        Live(WorkFamily.Sculptures, "W-013", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_Sculptures", "RWS_MetricDesc_Sculptures"),
        Live(WorkFamily.MealsByDef, "W-017, N2-F-012", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_MealsByDef", "RWS_MetricDesc_MealsByDef", breakdown: true),
        Live(WorkFamily.UniqueCraftedDefs, "W-018", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_UniqueCraftedDefs", "RWS_MetricDesc_UniqueCraftedDefs"),
        Live(WorkFamily.DailyPeak, "N1-A-009", Work, Count, Max, WorkFamily.Name, "RWS_Metric_CraftDailyPeak", "RWS_MetricDesc_CraftDailyPeak"),
        Live(WorkFamily.BestCrafter, "N3-W-001", Work, Count, Max, WorkFamily.Name, "RWS_Metric_BestCrafter", "RWS_MetricDesc_BestCrafter", breakdown: true),
        Live(WorkFamily.BestCook, "N3-W-004", Work, Count, Max, WorkFamily.Name, "RWS_Metric_BestCook", "RWS_MetricDesc_BestCook", breakdown: true),
        Live(WorkFamily.HarvestUnits, "W-001", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_HarvestUnits", "RWS_MetricDesc_HarvestUnits"),
        Live(WorkFamily.HarvestByCrop, "N2-F-002", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_HarvestByCrop", "RWS_MetricDesc_HarvestByCrop", breakdown: true),
        Live(WorkFamily.TopCrop, "N2-F-003", Work, Count, Each, WorkFamily.Name, "RWS_Metric_TopCrop", "RWS_MetricDesc_TopCrop", breakdown: true),
        Live(WorkFamily.HarvestYearPeak, "N2-F-011", Work, Count, Max, WorkFamily.Name, "RWS_Metric_HarvestYearPeak", "RWS_MetricDesc_HarvestYearPeak"),
        Live(WorkFamily.BestHarvester, "N3-W-002", Work, Count, Max, WorkFamily.Name, "RWS_Metric_BestHarvester", "RWS_MetricDesc_BestHarvester", breakdown: true),
        Live(WorkFamily.HarvestFailures, "W-015", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_HarvestFailures", "RWS_MetricDesc_HarvestFailures"),
        Live(WorkFamily.Sown_, "W-014", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_CropsSown", "RWS_MetricDesc_CropsSown", breakdown: true),
        Live(WorkFamily.SownDefs, "N2-F-001", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_CropsSownDefs", "RWS_MetricDesc_CropsSownDefs"),
        Live(WorkFamily.ColdLoss, "W-021", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_CropsColdLoss", "RWS_MetricDesc_CropsColdLoss"),
        Live(WorkFamily.OtherLoss, "N2-F-005", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_CropsOtherLoss", "RWS_MetricDesc_CropsOtherLoss"),
        Live(WorkFamily.LossRate, "N2-F-009", Work, Percent, Ratio, WorkFamily.Name, "RWS_Metric_CropsLossRate", "RWS_MetricDesc_CropsLossRate"),
        Live(WorkFamily.MiningUnits, "W-002", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_MiningUnits", "RWS_MetricDesc_MiningUnits"),
        Live(WorkFamily.BestMiner, "N3-W-005", Work, Count, Max, WorkFamily.Name, "RWS_Metric_BestMiner", "RWS_MetricDesc_BestMiner", breakdown: true),
        Live(WorkFamily.Constructions, "W-004", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_Constructions", "RWS_MetricDesc_Constructions"),
        Live(WorkFamily.ConstructionMaterials, "W-007", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_ConstructionMaterials", "RWS_MetricDesc_ConstructionMaterials", breakdown: true),
        Live(WorkFamily.BestBuilder, "N3-W-003", Work, Count, Max, WorkFamily.Name, "RWS_Metric_BestBuilder", "RWS_MetricDesc_BestBuilder", breakdown: true),
        Live(WorkFamily.ConstructionFailures, "W-009", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_ConstructionFailures", "RWS_MetricDesc_ConstructionFailures"),
        Live(WorkFamily.Deconstructions, "W-008", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_Deconstructions", "RWS_MetricDesc_Deconstructions"),
        Live(WorkFamily.Repairs, "N4-R-004", Work, Count, Sum, WorkFamily.Name, "RWS_Metric_Repairs", "RWS_MetricDesc_Repairs"),
        Live(WorkFamily.PodsLaunched_, "W-023", MetricCategory.Exploration, Count, Sum, WorkFamily.Name, "RWS_Metric_PodsLaunched", "RWS_MetricDesc_PodsLaunched"),

        // C3 · food
        Live(FoodFamily.SpoiledUnits, "W-016", Work, Count, Sum, FoodFamily.Name, "RWS_Metric_FoodSpoiled", "RWS_MetricDesc_FoodSpoiled"),
        Live(FoodFamily.Nutrition, "W-019", Work, Count, Sum, FoodFamily.Name, "RWS_Metric_FoodNutrition", "RWS_MetricDesc_FoodNutrition"),
        Live(FoodFamily.NoShortageLongest, "N2-F-010", Work, Days, Max, FoodFamily.Name, "RWS_Metric_FoodNoShortageLongest", "RWS_MetricDesc_FoodNoShortageLongest"),
        Live(FoodFamily.HarvestPerGrowingCell, "N1-A-008", Work, MetricUnit.PerCellDay, Ratio, FoodFamily.Name, "RWS_Metric_FoodHarvestPerCell", "RWS_MetricDesc_FoodHarvestPerCell"),

        // C3 · trade
        Live(TradeFamily.Deals, "T-003", Economy, Count, Sum, TradeFamily.Name, "RWS_Metric_TradeDeals", "RWS_MetricDesc_TradeDeals", breakdown: true),
        Live(TradeFamily.OrbitalDeals, "T-007", Economy, Count, Sum, TradeFamily.Name, "RWS_Metric_TradeOrbital", "RWS_MetricDesc_TradeOrbital"),
        Live(TradeFamily.VisitingCaravanDeals, "T-008", Economy, Count, Sum, TradeFamily.Name, "RWS_Metric_TradeVisitingCaravan", "RWS_MetricDesc_TradeVisitingCaravan"),
        Live(TradeFamily.SalesTotal, "T-001", Economy, Silver, Sum, TradeFamily.Name, "RWS_Metric_TradeSalesTotal", "RWS_MetricDesc_TradeSalesTotal"),
        Live(TradeFamily.BuysTotal, "T-002", Economy, Silver, Sum, TradeFamily.Name, "RWS_Metric_TradeBuysTotal", "RWS_MetricDesc_TradeBuysTotal"),
        Live(TradeFamily.SalesPeak, "T-004", Economy, Silver, Max, TradeFamily.Name, "RWS_Metric_TradeSalesPeak", "RWS_MetricDesc_TradeSalesPeak"),
        Live(TradeFamily.BuysPeak, "T-005", Economy, Silver, Max, TradeFamily.Name, "RWS_Metric_TradeBuysPeak", "RWS_MetricDesc_TradeBuysPeak"),
        Live(TradeFamily.SalesPeakUnit, "N2-E-004", Economy, Silver, Max, TradeFamily.Name, "RWS_Metric_TradeSalesPeakUnit", "RWS_MetricDesc_TradeSalesPeakUnit"),
        Live(TradeFamily.SalesUnits, "N4-R-001", Economy, Count, Sum, TradeFamily.Name, "RWS_Metric_TradeSalesUnits", "RWS_MetricDesc_TradeSalesUnits", breakdown: true),
        Live(TradeFamily.BuysUnits, "N4-R-002", Economy, Count, Sum, TradeFamily.Name, "RWS_Metric_TradeBuysUnits", "RWS_MetricDesc_TradeBuysUnits", breakdown: true),
        Live(TradeFamily.TopSold, "T-006", Economy, Count, Each, TradeFamily.Name, "RWS_Metric_TradeTopSold", "RWS_MetricDesc_TradeTopSold", breakdown: true),
        Live(TradeFamily.TopBought, "N4-R-003", Economy, Count, Each, TradeFamily.Name, "RWS_Metric_TradeTopBought", "RWS_MetricDesc_TradeTopBought", breakdown: true),
        Live(TradeFamily.SoldDefs, "N2-E-010", Economy, Count, Sum, TradeFamily.Name, "RWS_Metric_TradeSoldDefs", "RWS_MetricDesc_TradeSoldDefs"),
        Live(TradeFamily.BoughtDefs, "N2-E-011", Economy, Count, Sum, TradeFamily.Name, "RWS_Metric_TradeBoughtDefs", "RWS_MetricDesc_TradeBoughtDefs"),
        Live(TradeFamily.PartnerFactionDefs, "N2-E-009", Economy, Count, Sum, TradeFamily.Name, "RWS_Metric_TradePartnerFactionDefs", "RWS_MetricDesc_TradePartnerFactionDefs", breakdown: true),

        // C3 · diplomacy
        Live(DiplomacyFamily.GiftGoodwill, "T-009", Economy, Count, Sum, DiplomacyFamily.Name, "RWS_Metric_GiftGoodwill", "RWS_MetricDesc_GiftGoodwill"),
        Live(DiplomacyFamily.AllyToHostile, "T-010", Economy, Count, Sum, DiplomacyFamily.Name, "RWS_Metric_AllyToHostile", "RWS_MetricDesc_AllyToHostile"),
        Live(DiplomacyFamily.Reconciled, "T-011", Economy, Count, Sum, DiplomacyFamily.Name, "RWS_Metric_Reconciled", "RWS_MetricDesc_Reconciled"),
        Live(DiplomacyFamily.AidRequests, "N2-E-007", Economy, Count, Sum, DiplomacyFamily.Name, "RWS_Metric_AidRequests", "RWS_MetricDesc_AidRequests"),
        Live(DiplomacyFamily.AidArrivals, "N2-E-008", Economy, Count, Sum, DiplomacyFamily.Name, "RWS_Metric_AidArrivals", "RWS_MetricDesc_AidArrivals"),

        // C4 · medicine
        Live(MedicineFamily.Tends, "M-001", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Tends", "RWS_MetricDesc_Tends"),
        Live(MedicineFamily.TendQualityMean, "N1-C-007", Medicine, Percent, Ratio, MedicineFamily.Name, "RWS_Metric_TendQualityMean", "RWS_MetricDesc_TendQualityMean"),
        Live(MedicineFamily.BestTender, "N3-H-012", Medicine, Count, Max, MedicineFamily.Name, "RWS_Metric_BestTender", "RWS_MetricDesc_BestTender", breakdown: true),
        Live(MedicineFamily.NeutralTends, "N4-P-002", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_NeutralTends", "RWS_MetricDesc_NeutralTends"),
        Live(MedicineFamily.MedicineUsed, "N1-C-010", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_MedicineUsed", "RWS_MetricDesc_MedicineUsed"),
        Live(MedicineFamily.Surgeries, "M-002", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Surgeries", "RWS_MetricDesc_Surgeries", breakdown: true),
        Live(MedicineFamily.SurgerySuccessRate, "M-002", Medicine, Percent, Ratio, MedicineFamily.Name, "RWS_Metric_SurgerySuccessRate", "RWS_MetricDesc_SurgerySuccessRate"),
        Live(MedicineFamily.BestSurgeon, "N3-H-010", Medicine, Count, Max, MedicineFamily.Name, "RWS_Metric_BestSurgeon", "RWS_MetricDesc_BestSurgeon", breakdown: true),
        Live(MedicineFamily.Implants, "M-007", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Implants", "RWS_MetricDesc_Implants"),
        Live(MedicineFamily.Rescues, "P-007", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Rescues", "RWS_MetricDesc_Rescues"),
        Live(MedicineFamily.HostileRescues, "N4-P-001", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_HostileRescues", "RWS_MetricDesc_HostileRescues"),
        Live(MedicineFamily.AwayRescues, "N3-X-010", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_AwayRescues", "RWS_MetricDesc_AwayRescues"),
        Live(MedicineFamily.Resurrections, "P-008", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Resurrections", "RWS_MetricDesc_Resurrections"),
        Live(MedicineFamily.ResurrectionsPerPawnMax, "N4-P-007", Medicine, Count, Max, MedicineFamily.Name, "RWS_Metric_ResurrectionsPerPawnMax", "RWS_MetricDesc_ResurrectionsPerPawnMax", breakdown: true),
        Live(MedicineFamily.DiseaseOnsets, "M-003", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_DiseaseOnsets", "RWS_MetricDesc_DiseaseOnsets"),
        Live(MedicineFamily.DiseaseDurationMean, "N3-H-001", Medicine, Days, Ratio, MedicineFamily.Name, "RWS_Metric_DiseaseDurationMean", "RWS_MetricDesc_DiseaseDurationMean"),
        Live(MedicineFamily.InfectionRecoveries, "M-004", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_InfectionRecoveries", "RWS_MetricDesc_InfectionRecoveries"),
        Live(MedicineFamily.InfectionDurationMax, "N3-H-002", Medicine, Days, Max, MedicineFamily.Name, "RWS_Metric_InfectionDurationMax", "RWS_MetricDesc_InfectionDurationMax"),
        Live(MedicineFamily.FoodPoisonings, "M-005", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_FoodPoisonings", "RWS_MetricDesc_FoodPoisonings"),
        Live(MedicineFamily.Addictions, "P-026", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Addictions", "RWS_MetricDesc_Addictions"),
        Live(MedicineFamily.Overdoses, "N3-H-004", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Overdoses", "RWS_MetricDesc_Overdoses"),
        Live(MedicineFamily.Withdrawals, "N3-H-005", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Withdrawals", "RWS_MetricDesc_Withdrawals"),
        Live(MedicineFamily.HospitalStayMean, "N3-H-011", Medicine, Days, Ratio, MedicineFamily.Name, "RWS_Metric_HospitalStayMean", "RWS_MetricDesc_HospitalStayMean"),
        Live(MedicineFamily.PartsLost, "M-006", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_PartsLost", "RWS_MetricDesc_PartsLost"),
        Live(MedicineFamily.Scars, "N3-H-007", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_Scars", "RWS_MetricDesc_Scars"),
        Live(MedicineFamily.InjuriesPerPawnMax, "N4-P-008", Medicine, Count, Max, MedicineFamily.Name, "RWS_Metric_InjuriesPerPawnMax", "RWS_MetricDesc_InjuriesPerPawnMax", breakdown: true),
        Live(MedicineFamily.WildAnimalInjuries, "N4-C-010", Medicine, Count, Sum, MedicineFamily.Name, "RWS_Metric_WildAnimalInjuries", "RWS_MetricDesc_WildAnimalInjuries"),

        // C4 · disaster
        Live(DisasterFamily.ToxicFalloutDays, "D-001", Env, Days, Sum, DisasterFamily.Name, "RWS_Metric_ToxicFalloutDays", "RWS_MetricDesc_ToxicFalloutDays"),
        Live(DisasterFamily.SolarFlares, "D-002", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_SolarFlares", "RWS_MetricDesc_SolarFlares"),
        Live(DisasterFamily.SolarFlareDays, "N4-C-012", Env, Days, Sum, DisasterFamily.Name, "RWS_Metric_SolarFlareDays", "RWS_MetricDesc_SolarFlareDays"),
        Live(DisasterFamily.ColdSnaps, "D-003", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_ColdSnaps", "RWS_MetricDesc_ColdSnaps"),
        Live(DisasterFamily.HeatWaves, "D-004", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_HeatWaves", "RWS_MetricDesc_HeatWaves"),
        Live(DisasterFamily.VolcanicWinters, "D-005", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_VolcanicWinters", "RWS_MetricDesc_VolcanicWinters"),
        Live(DisasterFamily.Eclipses, "N4-C-005", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_Eclipses", "RWS_MetricDesc_Eclipses"),
        Live(DisasterFamily.CropBlights, "D-006", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_CropBlights", "RWS_MetricDesc_CropBlights"),
        Live(DisasterFamily.Meteors, "N4-C-006", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_Meteors", "RWS_MetricDesc_Meteors"),
        Live(DisasterFamily.ShortCircuits, "N4-R-010", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_ShortCircuits", "RWS_MetricDesc_ShortCircuits"),
        Live(DisasterFamily.Fires, "D-007", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_Fires", "RWS_MetricDesc_Fires"),
        Live(DisasterFamily.BuildingsBurned, "D-008", Env, Count, Sum, DisasterFamily.Name, "RWS_Metric_BuildingsBurned", "RWS_MetricDesc_BuildingsBurned"),

        // C4 · caravan
        Live(CaravanFamily.Departures, "Q-008", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_CaravanDepartures", "RWS_MetricDesc_CaravanDepartures"),
        Live(CaravanFamily.Lost_, "Q-009", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_CaravansLost", "RWS_MetricDesc_CaravansLost"),
        Live(CaravanFamily.TilesTravelled, "Q-010", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_CaravanTiles", "RWS_MetricDesc_CaravanTiles"),
        Live(CaravanFamily.UniqueTiles, "Q-011", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_CaravanUniqueTiles", "RWS_MetricDesc_CaravanUniqueTiles"),
        Live(CaravanFamily.LongestExpedition, "N2-E-001", Explore, Days, Max, CaravanFamily.Name, "RWS_Metric_CaravanLongestExpedition", "RWS_MetricDesc_CaravanLongestExpedition"),
        Live(CaravanFamily.LongestDistance, "N3-X-007", Explore, Count, Max, CaravanFamily.Name, "RWS_Metric_CaravanLongestDistance", "RWS_MetricDesc_CaravanLongestDistance"),
        Live(CaravanFamily.ConcurrentMax, "N4-R-011", Explore, Count, Max, CaravanFamily.Name, "RWS_Metric_CaravanConcurrentMax", "RWS_MetricDesc_CaravanConcurrentMax"),
        Live(CaravanFamily.Ambushes, "N2-E-003", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_CaravanAmbushes", "RWS_MetricDesc_CaravanAmbushes"),
        Live(CaravanFamily.SitesCleared, "Q-012", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_SitesCleared", "RWS_MetricDesc_SitesCleared"),
        Live(CaravanFamily.SettlementsFounded, "N3-X-008", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_SettlementsFounded", "RWS_MetricDesc_SettlementsFounded"),
        Live(CaravanFamily.SettlementsAbandoned, "Q-013", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_SettlementsAbandoned", "RWS_MetricDesc_SettlementsAbandoned"),
        Live(CaravanFamily.AncientDangersOpened, "Q-014", Explore, Count, Sum, CaravanFamily.Name, "RWS_Metric_AncientDangersOpened", "RWS_MetricDesc_AncientDangersOpened"),
        // D2 · royalty (only audited title and honor state transitions)
        Live(RoyaltyFamily.PermitUses, "L-002", MetricCategory.Royalty, Count, Sum, RoyaltyFamily.Name, "RWS_Metric_RoyaltyPermitUses", "RWS_MetricDesc_RoyaltyPermitUses"),
        Live(RoyaltyFamily.Psycasts, "L-005", MetricCategory.Royalty, Count, Sum, RoyaltyFamily.Name, "RWS_Metric_RoyaltyPsycasts", "RWS_MetricDesc_RoyaltyPsycasts"),
        Live(RoyaltyFamily.TitleGrants, "L-001", MetricCategory.Royalty, Count, Sum, RoyaltyFamily.Name, "RWS_Metric_RoyaltyTitleGrants", "RWS_MetricDesc_RoyaltyTitleGrants"),
        Live(RoyaltyFamily.HighestTitle, "L-004", MetricCategory.Royalty, Count, Max, RoyaltyFamily.Name, "RWS_Metric_RoyaltyHighestTitle", "RWS_MetricDesc_RoyaltyHighestTitle", breakdown: true),
        Live(RoyaltyFamily.Honor, "N1-E-006", MetricCategory.Royalty, Count, Sum, RoyaltyFamily.Name, "RWS_Metric_RoyaltyHonor", "RWS_MetricDesc_RoyaltyHonor", breakdown: true),
        // D2 · ideology: actual existing-ideology changes on the player side
        Live(IdeologyFamily.Conversions, "L-007", MetricCategory.Ideology, Count, Sum, IdeologyFamily.Name, "RWS_Metric_IdeologyConversions", "RWS_MetricDesc_IdeologyConversions"),
        Live(IdeologyFamily.RitualResults, "L-008", MetricCategory.Ideology, Count, Sum, IdeologyFamily.Name, "RWS_Metric_IdeologyRitualResults", "RWS_MetricDesc_IdeologyRitualResults", breakdown: true),
        Live(IdeologyFamily.RitualSuccessRate, "L-009", MetricCategory.Ideology, Percent, Ratio, IdeologyFamily.Name, "RWS_Metric_IdeologyRitualRate", "RWS_MetricDesc_IdeologyRitualRate"),
        Live(IdeologyFamily.ConversionDaysMean, "L-010", MetricCategory.Ideology, Days, Ratio, IdeologyFamily.Name, "RWS_Metric_IdeologyConversionDays", "RWS_MetricDesc_IdeologyConversionDays"),
        Live(IdeologyFamily.RoleAppointments, "N1-E-007", MetricCategory.Ideology, Count, Sum, IdeologyFamily.Name, "RWS_Metric_IdeologyAppointments", "RWS_MetricDesc_IdeologyAppointments"),
        // D2 · odyssey: Core completion accounting, waiting for audited landing hook and stable flight identity
        Live(OdysseyFamily.Flights, "L-022", MetricCategory.Odyssey, Count, Sum, OdysseyFamily.Name, "RWS_Metric_OdysseyFlights", "RWS_MetricDesc_OdysseyFlights"),
        Live(OdysseyFamily.Distance, "L-023", MetricCategory.Odyssey, Count, Sum, OdysseyFamily.Name, "RWS_Metric_OdysseyDistance", "RWS_MetricDesc_OdysseyDistance"),
        Live(OdysseyFamily.UniqueTiles, "L-024", MetricCategory.Odyssey, Count, Sum, OdysseyFamily.Name, "RWS_Metric_OdysseyUniqueTiles", "RWS_MetricDesc_OdysseyUniqueTiles"),
        Live(OdysseyFamily.NewMapLandings, "L-026", MetricCategory.Odyssey, Count, Sum, OdysseyFamily.Name, "RWS_Metric_OdysseyNewMaps", "RWS_MetricDesc_OdysseyNewMaps"),
        Live(BiotechFamily.MechsCreated, "L-012", MetricCategory.Biotech, Count, Sum, BiotechFamily.Name, "RWS_Metric_BiotechMechsCreated", "RWS_MetricDesc_BiotechMechsCreated"),
        Live(BiotechFamily.BirthMothers, "L-014", MetricCategory.Biotech, Count, Sum, BiotechFamily.Name, "RWS_Metric_BiotechBirthMothers", "RWS_MetricDesc_BiotechBirthMothers"),
        Live(BiotechFamily.ChildStages, "L-015", MetricCategory.Biotech, Count, Sum, BiotechFamily.Name, "RWS_Metric_BiotechChildStages", "RWS_MetricDesc_BiotechChildStages", breakdown: true),
        Live(BiotechFamily.MechRepairs, "L-016", MetricCategory.Biotech, Count, Sum, BiotechFamily.Name, "RWS_Metric_BiotechMechRepairs", "RWS_MetricDesc_BiotechMechRepairs"),
        Live(BiotechFamily.WasteCreated, "N1-E-001", MetricCategory.Biotech, Count, Sum, BiotechFamily.Name, "RWS_Metric_BiotechWasteCreated", "RWS_MetricDesc_BiotechWasteCreated"),
        Live(BiotechFamily.WasteDisposed, "N1-E-002", MetricCategory.Biotech, Count, Sum, BiotechFamily.Name, "RWS_Metric_BiotechWasteDisposed", "RWS_MetricDesc_BiotechWasteDisposed"),
        Live(BiotechFamily.PollutionCleaned, "N1-E-003", MetricCategory.Biotech, Count, Sum, BiotechFamily.Name, "RWS_Metric_BiotechPollutionCleaned", "RWS_MetricDesc_BiotechPollutionCleaned"),
    };
}
