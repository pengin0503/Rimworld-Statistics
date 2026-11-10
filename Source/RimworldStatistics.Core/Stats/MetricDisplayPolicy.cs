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
        ChoreFamily.SlaughteredButchery,
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
        DisasterFamily.VolcanicWinters, DisasterFamily.Eclipses,
        DisasterFamily.CropBlights, DisasterFamily.Meteors, DisasterFamily.ShortCircuits,
        DisasterFamily.Fires, DisasterFamily.BuildingsBurned,
        DisasterFamily.ToxicFalloutDays, DisasterFamily.SolarFlareDays,
        CaravanFamily.Departures, CaravanFamily.Lost_, CaravanFamily.TilesTravelled,
        CaravanFamily.UniqueTiles, CaravanFamily.Ambushes, CaravanFamily.SitesCleared,
        CaravanFamily.SettlementsFounded, CaravanFamily.SettlementsAbandoned,
        CaravanFamily.AncientDangersOpened,
        HealthDeathFamily.KillsByVictimKind, HealthDeathFamily.DeathCauses,
        HealthDeathFamily.DeathsByRaid, RaidFamily.ByArrivalMode, RaidFamily.ByFaction,
        MembershipFamily.Routes, MembershipFamily.Unique,
        WorkFamily.QualityBreakdown, WorkFamily.Legendary, WorkFamily.Masterwork,
        WorkFamily.Sculptures, WorkFamily.MealsByDef, WorkFamily.UniqueCraftedDefs,
        WorkFamily.HarvestByCrop, WorkFamily.SownDefs, WorkFamily.ConstructionMaterials,
        TradeFamily.SoldDefs, TradeFamily.BoughtDefs, TradeFamily.PartnerFactionDefs,
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

    /// <summary>A captured numeric reading, including an observed zero. Missing,
    /// non-applicable and non-finite values never qualify for the optional UI filter.</summary>
    public static bool HasMeasuredNumber(NumericRow row)
        => row.Value.Number is { } n && double.IsFinite(n)
            && row.Value.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable);

    /// <summary>A cross-colony top-category output with no ranked winner is
    /// not a time or wealth threshold that a colony has failed to reach.</summary>
    public static bool IsUnrankedTop(MetricDefinition definition, NumericValue value)
        => CrossColonyCatalog.IsDerived(definition)
            && definition.Key.StartsWith(CrossColonyCatalog.TopPrefix, StringComparison.Ordinal)
            && value.NotReached;

    /// <summary>Return a localization key only for a breakdown of this parent.
    /// Shared tokens such as "other" and "success" are not globally meaningful.</summary>
    public static string? BreakdownLabelKey(MetricDefinition definition, string key)
    {
        if (key == MetricKeys.UnknownBucket)
            return "RWS_Details_UnknownBucket";
        if (definition.Key == MedicineFamily.Surgeries)
            return key switch
            {
                "success" => "RWS_Details_SurgerySuccess",
                "failure" => "RWS_Details_SurgeryFailure",
                _ => null,
            };
        if (definition.Key == TradeFamily.Deals)
            return key switch
            {
                TradeFamily.Orbital => "RWS_Details_TradeKindOrbital",
                TradeFamily.VisitingCaravan => "RWS_Details_TradeKindCaravan",
                TradeFamily.Settlement => "RWS_Details_TradeKindSettlement",
                TradeFamily.OtherTrader => "RWS_Details_TradeKindOther",
                _ => null,
            };
        if (key == MembershipFamily.RouteOther)
            return "RWS_Details_Other";
        if (definition.Key is MetricRankings.ByPermadeathDays or MetricRankings.ByPermadeathEndings)
            return key switch
            {
                MetricRankings.PermadeathOn => "RWS_Details_Permadeath",
                MetricRankings.PermadeathOff => "RWS_Details_NotPermadeath",
                _ => null,
            };
        return null;
    }

    public static IReadOnlyList<MetricDefinition> SelectDefinitions(
        IReadOnlyCollection<MetricDefinition> catalog, IReadOnlySet<string> visibleUnitIds)
        => catalog.Where(d => d.Published || d.Acquisition is { } id && visibleUnitIds.Contains(id)).ToArray();
}
