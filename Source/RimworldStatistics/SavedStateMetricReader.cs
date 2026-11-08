using RimWorld;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics;

/// <summary>
/// Game-thread, save-success-only Phase B acquisition. Each reader returns detached primitive metrics.
/// A failure quarantines only the corresponding unit, not the successful game save or other statistics.
/// </summary>
internal static class SavedStateMetricReader
{
    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal) { "research_state", "factions", "roster", "facilities", "stock", "colony_start", "royalty_state", "royalty_quests", "biotech_state" };

    internal static bool Supports(string id) => Supported.Contains(id);

    public static MetricObservation? Read(ColonyIdentity identity, string gameVersion, DateTime utc)
    {
        var units = Metrics.MetricGates.RecordingSnapshots();
        if (units.Count == 0 && !Metrics.ModProviderAdapters.HasSnapshots)
            return null;
        var observation = new MetricObservation
        {
            Source = MetricObservationSource.SaveSnapshot,
            FormatVersion = 1,
            Key = Guid.NewGuid().ToString("N"),
            Identity = identity.Clone(),
            GameVersion = gameVersion,
            SaveTick = Find.TickManager.TicksGame,
            ObservedUtc = utc,
            ActiveDlcIds = Metrics.LiveMetrics.ActiveDlcIds(),
        };
        foreach (var unit in units)
        {
            try
            {
                if (unit == "research_state")
                    foreach (var (key, value) in ReadResearch())
                        observation.Metrics[key] = value;
                else if (unit == "factions")
                    foreach (var (key, value) in ReadFactions())
                        observation.Metrics[key] = value;
                else if (unit == "roster")
                    foreach (var (key, value) in ReadRoster())
                        observation.Metrics[key] = value;
                else if (unit == "facilities")
                    foreach (var (key, value) in ReadFacilities())
                        observation.Metrics[key] = value;
                else if (unit == "stock")
                    foreach (var (key, value) in ReadStock())
                        observation.Metrics[key] = value;
                else if (unit == "colony_start")
                    foreach (var (key, value) in ReadColonyStart())
                        observation.Metrics[key] = value;
                else if (unit == "royalty_state")
                    foreach (var (key, value) in ReadRoyaltyState())
                        observation.Metrics[key] = value;
                else if (unit == "royalty_quests")
                    foreach (var (key, value) in ReadRoyaltyQuests())
                        observation.Metrics[key] = value;
                else if (unit == "biotech_state")
                    foreach (var (key, value) in ReadBiotechState())
                        observation.Metrics[key] = value;
                observation.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(StringComparer.Ordinal);
                observation.EvidenceByAcquisition[unit] = Metrics.MetricGates.IsExperimental(unit)
                    ? MetricEvidence.Experimental : MetricEvidence.Verified;
            }
            catch (Exception e)
            {
                Metrics.MetricGates.Fail(unit, e);
            }
        }
        Metrics.ModProviderAdapters.CaptureSuccessfulSave(observation);
        return observation.Metrics.Count > 0 ? observation : null;
    }

    /// <summary>Biome of the original game starting tile; relocation never changes the category.</summary>
    private static Dictionary<string, MetricEntry> ReadColonyStart()
    {
        var startingTile = Find.GameInfo.startingTile;
        if (!startingTile.Valid)
            return ColonyStartSnapshot.Build(null);
        // PlanetTile is stable across saving and reloading; unlike any currently
        // loaded player's home map, this is the game's original start location.
        var grid = Find.World?.grid;
        var biomeDef = grid is not null && grid.InBounds(startingTile)
            ? grid[startingTile]?.PrimaryBiome?.defName : null;
        return ColonyStartSnapshot.Build(biomeDef);
    }

    private static Dictionary<string, MetricEntry> ReadResearch()
    {
        var facts = new List<ResearchStateFact>();
        var partial = false;
        foreach (var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
        {
            if (project is null || string.IsNullOrEmpty(project.defName))
            {
                partial = true;
                continue;
            }
            try
            {
                facts.Add(new ResearchStateFact(project.defName, project.IsFinished, (int)project.techLevel));
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        return ResearchStateSnapshot.Build(facts, partial);
    }
    private static Dictionary<string, MetricEntry> ReadFactions()
    {
        var player = Faction.OfPlayerSilentFail;
        if (player is null)
            throw new InvalidOperationException("Player faction is not initialized");
        var facts = new List<FactionStateFact>();
        var partial = false;
        foreach (var f in Find.FactionManager.AllFactions)
        {
            if (f is null || f == player)
                continue;
            try
            {
                var active = f.defeated == false && f.hidden == false && f.temporary == false && f.deactivated == false;
                if (!active)
                    continue;
                var relation = player.RelationKindWith(f);
                var goodwill = f.def.permanentEnemy ? (int?)null : player.GoodwillWith(f);
                facts.Add(new FactionStateFact(f.GetUniqueLoadID(), true,
                    relation == FactionRelationKind.Ally, relation == FactionRelationKind.Hostile, goodwill));
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        return FactionStateSnapshot.Build(facts, partial);
    }
    private static Dictionary<string, MetricEntry> ReadRoster()
    {
        var facts = new List<RosterPawnFact>();
        var partial = false;
        foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive)
        {
            if (pawn is null || pawn.Dead)
                continue;
            try
            {
                var owned = pawn.Faction == Faction.OfPlayer;
                var kind = owned && pawn.IsFreeNonSlaveColonist ? RosterKind.FreeColonist
                    : pawn.IsPrisonerOfColony ? RosterKind.Prisoner
                    : owned && pawn.RaceProps.Animal ? RosterKind.ColonyAnimal
                    : owned && pawn.RaceProps.IsMechanoid ? RosterKind.ColonyMech
                    : RosterKind.Other;
                if (kind == RosterKind.Other)
                    continue;
                var skills = kind == RosterKind.FreeColonist && pawn.skills?.skills is { } list
                    ? list.Where(x => x?.def != null && !x.TotallyDisabled)
                        .Select(x => new RosterSkillFact(x.def.defName, x.levelInt, x.passion.ToString())).ToArray()
                    : Array.Empty<RosterSkillFact>();
                var hediffs = pawn.health?.hediffSet?.hediffs;
                facts.Add(new RosterPawnFact
                {
                    Id = pawn.GetUniqueLoadID(), Kind = kind, RaceDef = pawn.def.defName,
                    AgeYears = kind == RosterKind.FreeColonist ? pawn.ageTracker?.AgeBiologicalYearsFloat : null,
                    Augmented = hediffs?.Any(h => h is Hediff_AddedPart) == true,
                    Injured = hediffs?.Any(h => h is Hediff_Injury) == true,
                    Skills = skills,
                });
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        return RosterSnapshot.Build(facts, partial);
    }

    private static Dictionary<string, MetricEntry> ReadFacilities()
    {
        var maps = new List<FacilityMapFact>();
        var partial = false;
        foreach (var map in Find.Maps)
        {
            if (!map.IsPlayerHome)
                continue;
            try
            {
                var player = Faction.OfPlayerSilentFail;
                var beds = 0;
                var turrets = 0;
                double generation = 0, battery = 0;
                var running = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var thing in map.listerThings.AllThings)
                {
                    if (thing.Faction != player)
                        continue;
                    if (thing is Building_Bed bed && bed.Medical) beds++;
                    if (thing is Building_Turret) turrets++;
                    if (thing is not ThingWithComps compThing) continue;
                    var plant = compThing.TryGetComp<CompPowerPlant>();
                    if (plant != null)
                    {
                        var rated = Math.Max(0, -plant.Props.PowerConsumption);
                        generation += rated;
                        if (plant.PowerOutput > 0)
                            running[thing.def.defName] = running.GetValueOrDefault(thing.def.defName) + 1;
                    }
                    var store = compThing.TryGetComp<CompPowerBattery>();
                    if (store != null)
                        battery += store.Props.storedEnergyMax;
                }
                var growingCells = map.zoneManager.AllZones.OfType<Zone_Growing>()
                    .SelectMany(z => z.Cells).Distinct().Count();
                // Regions are the game's already-deduplicated spatial partition. Enumerating
                // them avoids invoking RegionAndRoomQuery.RoomAt for every single map cell.
                // Rebuild-capable AllRegions also observes room changes after construction.
                var rooms = new HashSet<Room>();
                foreach (var region in map.regionGrid.AllRegions)
                {
                    var room = region.Room;
                    if (room != null && !room.PsychologicallyOutdoors)
                        rooms.Add(room);
                }
                var indoorRooms = rooms.Count;
                maps.Add(new FacilityMapFact
                {
                    Id = map.uniqueID.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    MedicalBeds = beds, Turrets = turrets, GrowingCells = growingCells, IndoorRooms = indoorRooms,
                    GenerationWatts = generation, BatteryWattDays = battery,
                    RunningGenerators = running,
                    OutdoorTempC = map.mapTemperature.OutdoorTemp,
                });
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        return FacilitiesSnapshot.Build(maps, partial);
    }

    private static Dictionary<string, MetricEntry> ReadStock()
    {
        var items = new List<StockItemFact>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var partial = false;
        var player = Faction.OfPlayerSilentFail;
        if (player is null)
            throw new InvalidOperationException("No player faction exists");
        void Gather(Thing item)
        {
            if (item is null || item.Destroyed || item is Corpse || item is Pawn)
                return;
            try
            {
                if (item.Faction is { } owner && owner != player)
                    return;
                if (item.IsForbidden(player))
                    return;
                if (item is MinifiedThing min && min.InnerThing is { } inner)
                {
                    Gather(inner);
                    return;
                }
                var id = item.GetUniqueLoadID();
                if (string.IsNullOrEmpty(id) || !visited.Add(id))
                    return;
                double nutrition = 0;
                if (item.def.ingestible != null)
                {
                    var value = item.GetStatValue(StatDefOf.Nutrition);
                    if (float.IsFinite(value) && value > 0)
                        nutrition = value;
                }
                var isWeapon = item.def.IsWeapon;
                var quality = item.TryGetComp<CompQuality>();
                var persona = item.TryGetComp<CompBladelinkWeapon>() != null;
                items.Add(new StockItemFact(id, item.def.defName, item.stackCount, nutrition,
                    isWeapon, item.def.IsMedicine, quality?.Quality == QualityCategory.Legendary, persona));
                if (item is IThingHolder holder)
                {
                    var direct = holder.GetDirectlyHeldThings();
                    if (direct != null)
                        foreach (var contained in direct)
                            Gather(contained);
                }
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        foreach (var map in Find.Maps)
        {
            if (!map.IsPlayerHome)
                continue;
            foreach (var thing in map.listerThings.AllThings)
                Gather(thing);
        }
        // Phase B stock intentionally excludes caravans, transport pods and non-home maps.
        // A player pawn must actually be on a loaded home map for their possessions to count.
        foreach (var map in Find.Maps.Where(m => m.IsPlayerHome))
            foreach (var pawn in map.mapPawns.AllPawns)
            {
                if (pawn is null || pawn.Dead || !pawn.Spawned || pawn.Faction != player)
                    continue;
                if (pawn.equipment != null)
                    foreach (var item in pawn.equipment.AllEquipmentListForReading)
                        Gather(item);
                if (pawn.apparel != null)
                    foreach (var item in pawn.apparel.WornApparel)
                        Gather(item);
                if (pawn.inventory?.innerContainer is { } inventory)
                    foreach (var item in inventory)
                        Gather(item);
            }
        return StockSnapshot.Build(items, partial);
    }

    /// <summary>
    /// L-003: accepted, successfully ended retained quests that the Quests tab shows as related to the Empire
    /// (MainTabWindow_Quests.DoFactionInfo, 1.7.4985): a non-hidden, non-player involved faction of the Empire's def, unless the
    /// root hides the involved factions. Hidden quests are not listed by the game and are not counted.
    /// </summary>
    private static Dictionary<string, MetricEntry> ReadRoyaltyQuests()
    {
        if (!ModsConfig.RoyaltyActive)
            throw new InvalidOperationException("Royalty DLC is disabled");
        var empire = FactionDefOf.Empire ?? throw new InvalidOperationException("the Empire faction def is not loaded");
        var quests = Find.QuestManager?.QuestsListForReading ?? throw new InvalidOperationException("no quest manager");
        var count = 0;
        var partial = false;
        foreach (var quest in quests)
        {
            if (quest is null)
            {
                partial = true;
                continue;
            }
            try
            {
                if (quest.hidden || !quest.EverAccepted || quest.State != QuestState.EndedSuccess || quest.root is null || quest.root.hideInvolvedFactionsInfo)
                    continue;
                if (quest.InvolvedFactions.Any(f => f != null && !f.Hidden && !f.IsPlayer && f.def == empire))
                    count++;
            }
            catch (Exception)
            {
                partial = true;   // this quest is left out; the count is a lower bound
            }
        }
        return RoyaltyQuestSnapshot.Build(count, partial);
    }

    private static Dictionary<string, MetricEntry> ReadRoyaltyState()
    {
        if (!ModsConfig.RoyaltyActive)
            throw new InvalidOperationException("Royalty DLC is disabled");
        var levels = new List<int>();
        var partial = false;
        foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep)
        {
            if (pawn is null || pawn.Dead || !pawn.IsFreeNonSlaveColonist)
                continue;
            try { levels.Add(pawn.GetPsylinkLevel()); }
            catch (Exception) { partial = true; }
        }
        return RoyaltyStateSnapshot.Build(levels, partial);
    }

    private static Dictionary<string, MetricEntry> ReadBiotechState()
    {
        if (!ModsConfig.BiotechActive)
            throw new InvalidOperationException("Biotech DLC is disabled");
        var polluted = new List<int>();
        var bandwidth = new List<int>();
        var partial = false;
        foreach (var map in Find.Maps)
        {
            if (!map.IsPlayerHome)
                continue;
            try
            {
                // Vanilla PollutionGrid exposes the aggregate as a public property.
                // Do not iterate every map cell on every successful save.
                // An invalid aggregate is a failed unit reading, not a fabricated zero.
                var count = map.pollutionGrid.TotalPollution;
                if (count < 0 || count > map.cellIndices.NumGridCells)
                    throw new InvalidOperationException("PollutionGrid.TotalPollution was outside the map cell count.");
                polluted.Add(count);
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        foreach (var pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists_NoCryptosleep)
        {
            if (pawn is null || pawn.Dead || !pawn.IsFreeNonSlaveColonist)
                continue;
            try
            {
                if (pawn.mechanitor is { } tracker)
                    bandwidth.Add(tracker.UsedBandwidthFromSubjects);
            }
            catch (Exception)
            {
                partial = true;
            }
        }
        return BiotechStateSnapshot.Build(polluted, bandwidth, partial);
    }

}

