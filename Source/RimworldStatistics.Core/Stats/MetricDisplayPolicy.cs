using System.Collections.Frozen;

namespace RimworldStatistics.Core;

/// <summary>Visibility is distinct from the active game-session recording policy.</summary>
public static class MetricDisplayPolicy
{
    /// <summary>
    /// The regular Details window displays every implemented output (including
    /// experimental, unobserved and historical optional-MOD definitions).
    /// This does not enable acquisition units or install any game-side hooks.
    /// </summary>
    public static IReadOnlySet<string> AllDetailsVisibleUnits { get; } =
        AcquisitionUnits.All.Select(u => u.Id)
            .Append(MetricRegistry.OptionalModVisibilityId)
            .ToFrozenSet(StringComparer.Ordinal);

    // An explicit semantic whitelist: neither OverallAggregation.Sum nor a Count unit proves
    // that a value is the cumulative result of successful events. Unknown SDK metrics remain in All.
    private static readonly IReadOnlySet<string> LifetimeKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        // Confirmed saved-game counters; no snapshot inventories, peaks, periods or rates.
        MetricKeys.QuestSuccess, MetricKeys.QuestFailure, MetricKeys.QuestExpired,
        MetricKeys.QuestAccepted, MetricKeys.QuestSuccessByRoot,
        // Game-side cumulative event streams (gated separately from UI visibility).
        ResearchFamily.Completed,
        MentalFamily.Breaks, MentalFamily.Inspirations,
        HealthDeathFamily.HostileKills, HealthDeathFamily.RangedKills, HealthDeathFamily.MeleeKills,
        HealthDeathFamily.UnarmedKills, HealthDeathFamily.ExplosiveKills,
        HealthDeathFamily.EnvironmentKills, HealthDeathFamily.TurretKills,
        HealthDeathFamily.TrapKills, HealthDeathFamily.TameAnimalKills,
        HealthDeathFamily.PlayerMechKills, HealthDeathFamily.DiseaseDeaths,
        HealthDeathFamily.CaravanMemberDeaths, HealthDeathFamily.ColonyAnimalDeaths,
        HealthDeathFamily.ColonyAnimalRaidDeaths,
        DownFamily.ColonistsDowned, DownFamily.EnemiesDowned, DownFamily.ToxicDowns,
        DownFamily.SocialFightDowns,
        RaidFamily.Sieges, RaidFamily.Breaches, RaidFamily.Drops, RaidFamily.Infestations,
        RaidFamily.MechClusters, RaidFamily.Manhunters, RaidFamily.Retreated,
        RaidFamily.Wiped, RaidFamily.RepelledNoDown,
        MembershipFamily.FirstJoin, MembershipFamily.PrisonerRecruit,
        MembershipFamily.HumanBirths, MembershipFamily.Rejoin, MembershipFamily.SlavesFreed,
        MembershipFamily.Kidnaps, MembershipFamily.KidnapRescues,
        LifetimeFamily.FirstSkill20,
        SocialFamily.Marriages, SocialFamily.Divorces, SocialFamily.Lovers,
        SocialFamily.Fights, SocialFamily.PrisonBreaks, SocialFamily.PrisonersReleased,
        SocialFamily.PrisonersCaptured, SocialFamily.AnimalBonds,
        AnimalsFamily.Tames, AnimalsFamily.Births, AnimalsFamily.Slaughtered, AnimalsFamily.Hunted,
        ChoreFamily.FilthRemoved, ChoreFamily.CleaningJobsCompleted, ChoreFamily.FiresExtinguished, ChoreFamily.MealIngestions,
        ChoreFamily.HaulPlacements, ChoreFamily.HauledUnits, ChoreFamily.TreeWoodPlaced,
        WorkDetailsFamily.CraftedByDef, WorkDetailsFamily.MinedByDef,
        WorkFamily.CraftedUnits, WorkFamily.HarvestUnits, WorkFamily.HarvestFailures,
        WorkFamily.Sown_, WorkFamily.ColdLoss, WorkFamily.OtherLoss, WorkFamily.MiningUnits,
        WorkFamily.Constructions, WorkFamily.ConstructionFailures, WorkFamily.Deconstructions,
        WorkFamily.Repairs, WorkFamily.PodsLaunched_,
        FoodFamily.SpoiledUnits, FoodFamily.Nutrition,
        TradeFamily.Deals, TradeFamily.OrbitalDeals, TradeFamily.VisitingCaravanDeals,
        TradeFamily.SalesTotal, TradeFamily.BuysTotal, TradeFamily.SalesUnits, TradeFamily.BuysUnits,
        DiplomacyFamily.GiftGoodwill, DiplomacyFamily.AllyToHostile,
        DiplomacyFamily.Reconciled, DiplomacyFamily.AidRequests, DiplomacyFamily.AidArrivals,
        MedicineFamily.Tends, MedicineFamily.NeutralTends, MedicineFamily.MedicineUsed,
        MedicineFamily.Surgeries, MedicineFamily.Implants, MedicineFamily.Rescues,
        MedicineFamily.HostileRescues, MedicineFamily.AwayRescues,
        MedicineFamily.Resurrections, MedicineFamily.DiseaseOnsets,
        MedicineFamily.InfectionRecoveries, MedicineFamily.FoodPoisonings,
        MedicineFamily.Addictions, MedicineFamily.Overdoses, MedicineFamily.Withdrawals,
        MedicineFamily.PartsLost, MedicineFamily.Scars, MedicineFamily.WildAnimalInjuries,
        DisasterFamily.SolarFlares, DisasterFamily.ColdSnaps, DisasterFamily.HeatWaves,
    };

    /// <summary>A monotone event accumulation along one recorded checkpoint lineage, not
    /// necessarily full-history coverage. Provider metrics require explicit semantic review.</summary>
    public static bool IsLifetimeCumulative(MetricDefinition definition)
        => definition.ProviderId is null && definition.OwnerPackageId is null && LifetimeKeys.Contains(definition.Key);

    // Standalone trade-kind counts are already nested under the trade.count
    // breakdown. Preserve their semantic membership and accessibility in All.
    private static readonly IReadOnlySet<string> LifetimeBreakdownAliases = new HashSet<string>(StringComparer.Ordinal)
    {
        TradeFamily.OrbitalDeals, TradeFamily.VisitingCaravanDeals,
    };

    public static bool IsLifetimePrimaryRow(MetricDefinition definition)
        => IsLifetimeCumulative(definition) && !LifetimeBreakdownAliases.Contains(definition.Key);

    public static IReadOnlyList<MetricDefinition> SelectDefinitions(
        IReadOnlyCollection<MetricDefinition> catalog, IReadOnlySet<string> visibleUnitIds)
        => catalog.Where(d => d.Published || d.Acquisition is { } id && visibleUnitIds.Contains(id)).ToArray();
}
