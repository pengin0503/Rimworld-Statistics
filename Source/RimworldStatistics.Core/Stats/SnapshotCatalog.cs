namespace RimworldStatistics.Core;

/// <summary>Phase B snapshots are independent from event checkpoints and remain hidden until verified.</summary>
public static class SnapshotCatalog
{
    private static MetricDefinition Snap(string key, string ledger, MetricUnit metricUnit, OverallAggregation aggregation,
        string unit, string label, string description, MetricCategory category = MetricCategory.Research, bool breakdown = false, ColonySelection selection = ColonySelection.Representative, string? dlc = null)
        => new()
        {
            Key = key, StoredKey = key, LedgerIds = ledger, Category = category, Unit = metricUnit,
            Provenance = MetricProvenance.SaveSnapshot, Source = MetricObservationSource.SaveSnapshot,
            Overall = aggregation, Acquisition = unit, HasBreakdown = breakdown, Selection = selection, RequiredDlc = dlc,
            LabelKey = label, DescriptionKey = description,
        };

    public static IReadOnlyList<MetricDefinition> Definitions { get; } = new[]
    {
        Snap(ResearchStateSnapshot.Finished, "S-012", MetricUnit.Count, OverallAggregation.Sum, "research_state", "RWS_Metric_ResearchSnapshotFinished", "RWS_MetricDesc_ResearchSnapshotFinished"),
        Snap(ResearchStateSnapshot.FinishedRate, "S-012", MetricUnit.Percent, OverallAggregation.Ratio, "research_state", "RWS_Metric_ResearchSnapshotFinishedRate", "RWS_MetricDesc_ResearchSnapshotFinishedRate"),
        Snap(ResearchStateSnapshot.TechPeak, "S-013", MetricUnit.Count, OverallAggregation.Max, "research_state", "RWS_Metric_ResearchSnapshotTechPeak", "RWS_MetricDesc_ResearchSnapshotTechPeak", breakdown: true),
        Snap(FactionStateSnapshot.Relations, "S-014", MetricUnit.Count, OverallAggregation.Sum, "factions", "RWS_Metric_FactionSnapshotRelations", "RWS_MetricDesc_FactionSnapshotRelations", category: MetricCategory.Economy, breakdown: true),
        Snap(FactionStateSnapshot.GoodwillRange, "S-015", MetricUnit.Count, OverallAggregation.IndividualOnly, "factions", "RWS_Metric_FactionSnapshotGoodwillRange", "RWS_MetricDesc_FactionSnapshotGoodwillRange", category: MetricCategory.Economy, breakdown: true),
        Snap(RosterSnapshot.Free, "S-005", MetricUnit.Count, OverallAggregation.Sum, "roster", "RWS_Metric_RosterFree", "RWS_MetricDesc_RosterFree", category: MetricCategory.Population),
        Snap(RosterSnapshot.Prisoners, "S-006", MetricUnit.Count, OverallAggregation.Sum, "roster", "RWS_Metric_RosterPrisoners", "RWS_MetricDesc_RosterPrisoners", category: MetricCategory.Population),
        Snap(RosterSnapshot.Animals, "S-007", MetricUnit.Count, OverallAggregation.Sum, "roster", "RWS_Metric_RosterAnimals", "RWS_MetricDesc_RosterAnimals", category: MetricCategory.Animals),
        Snap(RosterSnapshot.Mechs, "S-008", MetricUnit.Count, OverallAggregation.Sum, "roster", "RWS_Metric_RosterMechs", "RWS_MetricDesc_RosterMechs", category: MetricCategory.Population),
        Snap(RosterSnapshot.AugmentedPeak, "S-021", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterAugmentedPeak", "RWS_MetricDesc_RosterAugmentedPeak", category: MetricCategory.Population, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.Skill20PawnsPeak, "S-022", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterSkill20PawnsPeak", "RWS_MetricDesc_RosterSkill20PawnsPeak", category: MetricCategory.Population, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.Skill20PerPawnPeak, "S-023", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterSkill20PerPawnPeak", "RWS_MetricDesc_RosterSkill20PerPawnPeak", category: MetricCategory.Population, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.Oldest, "N1-C-001", MetricUnit.Years, OverallAggregation.Max, "roster", "RWS_Metric_RosterOldest", "RWS_MetricDesc_RosterOldest", category: MetricCategory.Population, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.AnimalDefsPeak, "N1-D-008", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterAnimalDefsPeak", "RWS_MetricDesc_RosterAnimalDefsPeak", category: MetricCategory.Animals, selection: ColonySelection.ObservationMax),
        Snap(RosterSnapshot.Passions, "N3-W-010", MetricUnit.Count, OverallAggregation.IndividualOnly, "roster", "RWS_Metric_RosterPassions", "RWS_MetricDesc_RosterPassions", category: MetricCategory.Population, breakdown: true),
        Snap(RosterSnapshot.AgeMean, "N4-P-011", MetricUnit.Years, OverallAggregation.Ratio, "roster", "RWS_Metric_RosterAgeMean", "RWS_MetricDesc_RosterAgeMean", category: MetricCategory.Population),
        Snap(RosterSnapshot.InjuredPeak, "P-019", MetricUnit.Count, OverallAggregation.Max, "roster", "RWS_Metric_RosterInjuredPeak", "RWS_MetricDesc_RosterInjuredPeak", category: MetricCategory.Medicine, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.HomeMapsPeak, "S-016", MetricUnit.Count, OverallAggregation.Max, "facilities", "RWS_Metric_FacilitiesHomeMaps", "RWS_MetricDesc_FacilitiesHomeMaps", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.MedicalBedsPeak, "S-024", MetricUnit.Count, OverallAggregation.Max, "facilities", "RWS_Metric_FacilitiesMedicalBeds", "RWS_MetricDesc_FacilitiesMedicalBeds", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.TurretsPeak, "S-025", MetricUnit.Count, OverallAggregation.Max, "facilities", "RWS_Metric_FacilitiesTurrets", "RWS_MetricDesc_FacilitiesTurrets", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.GrowingCellsPeak, "N1-A-001", MetricUnit.Count, OverallAggregation.Max, "facilities", "RWS_Metric_FacilitiesGrowingCells", "RWS_MetricDesc_FacilitiesGrowingCells", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.RoomsPeak, "N1-A-003", MetricUnit.Count, OverallAggregation.Max, "facilities", "RWS_Metric_FacilitiesRooms", "RWS_MetricDesc_FacilitiesRooms", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.GenerationPeak, "N1-D-001", MetricUnit.Watts, OverallAggregation.Max, "facilities", "RWS_Metric_FacilitiesGeneration", "RWS_MetricDesc_FacilitiesGeneration", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.BatteryPeak, "N1-D-002", MetricUnit.WattDays, OverallAggregation.Max, "facilities", "RWS_Metric_FacilitiesBattery", "RWS_MetricDesc_FacilitiesBattery", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.RunningGenerators, "N1-D-004", MetricUnit.Count, OverallAggregation.Sum, "facilities", "RWS_Metric_FacilitiesRunningGenerators", "RWS_MetricDesc_FacilitiesRunningGenerators", category: MetricCategory.Facilities, breakdown: true, selection: ColonySelection.ObservationMax),
        Snap(FacilitiesSnapshot.OutdoorTempMin, "N4-C-001", MetricUnit.Celsius, OverallAggregation.Min, "facilities", "RWS_Metric_FacilitiesOutdoorTempMin", "RWS_MetricDesc_FacilitiesOutdoorTempMin", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMin),
        Snap(FacilitiesSnapshot.OutdoorTempMax, "N4-C-002", MetricUnit.Celsius, OverallAggregation.Max, "facilities", "RWS_Metric_FacilitiesOutdoorTempMax", "RWS_MetricDesc_FacilitiesOutdoorTempMax", category: MetricCategory.Facilities, selection: ColonySelection.ObservationMax),
        Snap(StockSnapshot.FoodLatest, "S-009", MetricUnit.Nutrition, OverallAggregation.Sum, "stock", "RWS_Metric_StockFoodLatest", "RWS_MetricDesc_StockFoodLatest", category: MetricCategory.Economy),
        Snap(StockSnapshot.SilverLatest, "S-010", MetricUnit.Silver, OverallAggregation.Sum, "stock", "RWS_Metric_StockSilverLatest", "RWS_MetricDesc_StockSilverLatest", category: MetricCategory.Economy),
        Snap(StockSnapshot.MaterialsLatest, "S-011", MetricUnit.Count, OverallAggregation.Sum, "stock", "RWS_Metric_StockMaterialsLatest", "RWS_MetricDesc_StockMaterialsLatest", category: MetricCategory.Economy, breakdown: true),
        Snap(StockSnapshot.WeaponsPeak, "S-017", MetricUnit.Count, OverallAggregation.Max, "stock", "RWS_Metric_StockWeaponsPeak", "RWS_MetricDesc_StockWeaponsPeak", category: MetricCategory.Economy, selection: ColonySelection.ObservationMax),
        Snap(StockSnapshot.WeaponDefsPeak, "S-018", MetricUnit.Count, OverallAggregation.Max, "stock", "RWS_Metric_StockWeaponDefsPeak", "RWS_MetricDesc_StockWeaponDefsPeak", category: MetricCategory.Economy, selection: ColonySelection.ObservationMax),
        Snap(StockSnapshot.LegendaryWeaponsPeak, "S-019", MetricUnit.Count, OverallAggregation.Max, "stock", "RWS_Metric_StockLegendaryWeaponsPeak", "RWS_MetricDesc_StockLegendaryWeaponsPeak", category: MetricCategory.Economy, selection: ColonySelection.ObservationMax),
        Snap(StockSnapshot.PersonaWeaponsPeak, "S-020", MetricUnit.Count, OverallAggregation.Max, "stock", "RWS_Metric_StockPersonaWeaponsPeak", "RWS_MetricDesc_StockPersonaWeaponsPeak", category: MetricCategory.Economy, selection: ColonySelection.ObservationMax, dlc: AcquisitionUnits.Royalty),
        Snap(StockSnapshot.MedicinePeak, "N1-A-004", MetricUnit.Count, OverallAggregation.Max, "stock", "RWS_Metric_StockMedicinePeak", "RWS_MetricDesc_StockMedicinePeak", category: MetricCategory.Economy, selection: ColonySelection.ObservationMax),
        Snap(StockSnapshot.FoodMin, "N1-A-005", MetricUnit.Nutrition, OverallAggregation.Min, "stock", "RWS_Metric_StockFoodMin", "RWS_MetricDesc_StockFoodMin", category: MetricCategory.Economy, selection: ColonySelection.ObservationMin),
    };
}
