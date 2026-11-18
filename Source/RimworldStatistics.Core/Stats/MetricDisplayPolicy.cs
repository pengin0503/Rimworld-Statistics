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
        RaidFamily.MechClusters, RaidFamily.Manhunters, RaidFamily.Retreated, RaidFamily.RetreatedByFaction,
        RaidFamily.Wiped, RaidFamily.WipedByFaction, RaidFamily.RepelledNoDown,
        MembershipFamily.FirstJoin, MembershipFamily.PrisonerRecruit,
        MembershipFamily.HumanBirths, MembershipFamily.HumanBirthsBySpecies, MembershipFamily.HumanBirthsByXenotype,
        MembershipFamily.SlavesFreed,
        MembershipFamily.Kidnaps, MembershipFamily.KidnapRescues,
        LifetimeFamily.FirstSkill20,
        SocialFamily.Marriages, SocialFamily.Divorces, SocialFamily.Lovers,
        SocialFamily.Fights, SocialFamily.PrisonBreaks, SocialFamily.PrisonersReleased,
        SocialFamily.PrisonersCaptured, SocialFamily.AnimalBonds,
        AnimalsFamily.Tames, AnimalsFamily.TamesBySpecies, AnimalsFamily.Births, AnimalsFamily.BirthsBySpecies,
        AnimalsFamily.Slaughtered, AnimalsFamily.SlaughteredBySpecies, AnimalsFamily.Hunted, AnimalsFamily.HuntedBySpecies,
        ChoreFamily.FilthRemoved, ChoreFamily.CleaningJobsCompleted, ChoreFamily.FiresExtinguished, ChoreFamily.MealIngestions,
        ChoreFamily.HaulPlacements, ChoreFamily.HauledUnits, ChoreFamily.TreeWoodPlaced,
        ChoreFamily.SlaughteredButchery,
        WorkDetailsFamily.CraftedByDef, WorkDetailsFamily.MinedByDef,
        WorkFamily.CraftedUnits, WorkFamily.HarvestUnits, WorkFamily.HarvestFailures,
        WorkFamily.Sown_, WorkFamily.MiningUnits,
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
        ModdedIncidentFamily.ByDef,
        FishingFamily.SuccessfulActions, FishingFamily.AcceptedUnitsByDef,
        CaravanFamily.Departures, CaravanFamily.Lost_, CaravanFamily.TilesTravelled,
        CaravanFamily.UniqueTiles, CaravanFamily.Ambushes, CaravanFamily.SitesCleared,
        CaravanFamily.SettlementsFounded, CaravanFamily.SettlementsAbandoned,
        CaravanFamily.AncientDangersOpened,
        HealthDeathFamily.KillsByVictimKind, HealthDeathFamily.DeathCauses,
        HealthDeathFamily.DeathsByRaid, RaidFamily.ByArrivalMode, RaidFamily.ByFaction,
        MembershipFamily.Routes, MembershipFamily.JoinsBySpecies, HealthDeathFamily.DeathsBySpecies,
        MembershipFamily.JoinsByXenotype, HealthDeathFamily.DeathsByXenotype,
        WorkFamily.QualityBreakdown, WorkFamily.Legendary, WorkFamily.Masterwork,
        WorkFamily.Sculptures, WorkFamily.MealsByDef, WorkFamily.UniqueCraftedDefs,
        WorkFamily.HarvestByCrop, WorkFamily.SownDefs, WorkFamily.ConstructionMaterials,
        TradeFamily.SoldDefs, TradeFamily.BoughtDefs, TradeFamily.PartnerFactionDefs,
        // DLC event streams (#331): each counts confirmed events, or a distinct set that only grows, along the lineage.
        MentalFamily.BreaksByDef, MentalFamily.InspirationsByDef,
        RoyaltyFamily.TitleGrants, RoyaltyFamily.PermitUses, RoyaltyFamily.PermitUsesByDef,
        RoyaltyFamily.Psycasts, RoyaltyFamily.PsycastsByDef, RoyaltyFamily.Honor,
        RoyaltyLinkingFamily.AnimaTreeLinks,
        IdeologyFamily.Conversions, IdeologyFamily.RitualResults, IdeologyFamily.RoleAppointments,
        IdeologyExpansionFamily.PrisonersEnslaved, IdeologyExpansionFamily.BiosculpterCompletions,
        OdysseyFamily.Flights, OdysseyFamily.Distance, OdysseyFamily.UniqueTiles, OdysseyFamily.NewMapLandings,
        OdysseyVoyageFamily.ChemfuelConsumed, OdysseyVoyageFamily.OrbitArrivals, OdysseyVoyageFamily.VisitedLandmarks,
        BiotechFamily.MechsCreated, BiotechFamily.BirthMothers, BiotechFamily.ChildStages, BiotechFamily.MechRepairs,
        BiotechFamily.WasteCreated, BiotechFamily.WasteDisposed, BiotechFamily.PollutionCleaned,
        BiotechGeneticsFamily.GrowthVatCompletions, BiotechGeneticsFamily.GeneExtractions,
        BiotechGeneticsFamily.XenogermsAssembled, BiotechGeneticsFamily.GenesImplanted,
        AnomalyFamily.PsychicRituals, AnomalyFamily.PsychicRitualsByDef, AnomalyFamily.EntityEscapes,
        AnomalyFamily.EntitiesContained, AnomalyFamily.PitGatesCollapsed,
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
        => row.Value.Coverage is not (MetricCoverage.Unknown or MetricCoverage.NotApplicable)
            && (row.Value.Number is { } n && double.IsFinite(n)
                // A parent without an additive scalar is measured when one of its breakdowns is (#329).
                || row.Value.BreakdownOnly && row.Breakdowns.Any(HasMeasuredNumber));

    /// <summary>The measured "min" and "max" breakdowns of a breakdown-only range output, for its parent row (#329).</summary>
    public static (double Min, double Max)? BreakdownRange(NumericRow row)
    {
        if (!row.Value.BreakdownOnly)
            return null;
        double? min = null, max = null;
        foreach (var b in row.Breakdowns)
        {
            if (!HasMeasuredNumber(b))
                continue;
            if (b.Breakdown == "min") min = b.Value.Number;
            else if (b.Breakdown == "max") max = b.Value.Number;
        }
        return min is { } lo && max is { } hi ? (lo, hi) : null;
    }

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
        if (key == MetricKeys.CustomXenotypeBucket)
            return "RWS_Details_CustomXenotype";
        if (definition.Key == ColonyStartSnapshot.WorldTemperature)
            return key switch
            {
                "VeryCold" => "RWS_WorldTemp_VeryCold",
                "Cold" => "RWS_WorldTemp_Cold",
                "LittleBitColder" => "RWS_WorldTemp_LittleBitColder",
                "Normal" => "RWS_WorldTemp_Normal",
                "LittleBitWarmer" => "RWS_WorldTemp_LittleBitWarmer",
                "Hot" => "RWS_WorldTemp_Hot",
                "VeryHot" => "RWS_WorldTemp_VeryHot",
                _ => null,
            };
        if (definition.Key == ColonyStartSnapshot.WorldRainfall)
            return key switch
            {
                "AlmostNone" => "RWS_WorldRain_AlmostNone",
                "Little" => "RWS_WorldRain_Little",
                "LittleBitLess" => "RWS_WorldRain_LittleBitLess",
                "Normal" => "RWS_WorldRain_Normal",
                "LittleBitMore" => "RWS_WorldRain_LittleBitMore",
                "High" => "RWS_WorldRain_High",
                "VeryHigh" => "RWS_WorldRain_VeryHigh",
                _ => null,
            };
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
