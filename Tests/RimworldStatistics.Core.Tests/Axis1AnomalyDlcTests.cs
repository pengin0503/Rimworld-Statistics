using System.Text;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class Axis1AnomalyDlcTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnomalyStateSnapshot_RespectsPlaystyleApplicabilityAndNeverFabricatesZero()
    {
        var standard = AnomalyStateSnapshot.Build(
            discoveredEntityDefs: 7,
            monolithHighestLevel: 2,
            heldEntities: 3);
        Assert.Equal(MetricCoverage.SnapshotOnly, standard[AnomalyStateSnapshot.DiscoveredEntities].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(7L, standard[AnomalyStateSnapshot.DiscoveredEntities].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, standard[AnomalyStateSnapshot.MonolithHighestLevel].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(3L, standard[AnomalyStateSnapshot.HeldEntitiesPeak].Values[MetricEntry.Scalar].IntegerValue);

        // Ambient Horror: study/containment enabled (0 held is valid), monolith disabled (null -> NotApplicable).
        var ambientHorror = AnomalyStateSnapshot.Build(
            discoveredEntityDefs: 4,
            monolithHighestLevel: null,
            heldEntities: 0);
        Assert.Equal(MetricCoverage.SnapshotOnly, ambientHorror[AnomalyStateSnapshot.DiscoveredEntities].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(MetricCoverage.NotApplicable, ambientHorror[AnomalyStateSnapshot.MonolithHighestLevel].Values[MetricEntry.Scalar].Coverage);
        Assert.Null(ambientHorror[AnomalyStateSnapshot.MonolithHighestLevel].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(0L, ambientHorror[AnomalyStateSnapshot.HeldEntitiesPeak].Values[MetricEntry.Scalar].IntegerValue);

        // Negative input weakens to Partial + Unknown rather than storing negative counts.
        var corrupted = AnomalyStateSnapshot.Build(-1, 1, 2);
        Assert.Equal(MetricCoverage.Unknown, corrupted[AnomalyStateSnapshot.DiscoveredEntities].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(MetricCoverage.Partial, corrupted[AnomalyStateSnapshot.MonolithHighestLevel].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, corrupted[AnomalyStateSnapshot.MonolithHighestLevel].Values[MetricEntry.Scalar].IntegerValue);
    }

    [Fact]
    public void AnomalyFamily_TracksPlayerPsychicRitualsEscapesContainmentBaselineAndPitGates()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { AnomalyFamily.Family.Spec }, 0);
        var s = acc.Family(AnomalyFamily.Name)!;

        // Baseline pre-existing held entity so moving platforms does not count as a new capture.
        AnomalyFamily.BaselineHeldEntities(s, new[] { "Thing_Shambler1" });
        AnomalyFamily.EntityContained(s, "Thing_Shambler1", playerPlatform: true, livingEntityOnPlatform: true);
        AnomalyFamily.EntityContained(s, "Thing_Revenant2", playerPlatform: true, livingEntityOnPlatform: true);
        AnomalyFamily.EntityContained(s, "Thing_Revenant2", playerPlatform: true, livingEntityOnPlatform: true); // transfer ignored
        AnomalyFamily.EntityContained(s, "Thing_Hostile3", playerPlatform: false, livingEntityOnPlatform: true);

        // Psychic rituals: vanilla + modded package-qualified defs; enemy cultist rituals ignored.
        AnomalyFamily.PsychicRitualCompleted(s, "Lord_1/0/1000", "ludeon.rimworld.anomaly/VoidProvocation", playerRitual: true, completed: true);
        AnomalyFamily.PsychicRitualCompleted(s, "Lord_1/0/1000", "ludeon.rimworld.anomaly/VoidProvocation", playerRitual: true, completed: true); // duplicate ignored
        AnomalyFamily.PsychicRitualCompleted(s, "Lord_2/0/2000", "author.anomalymod/ChronoChant", playerRitual: true, completed: true);
        AnomalyFamily.PsychicRitualCompleted(s, "Lord_3/0/3000", "ludeon.rimworld.anomaly/PsychicSkipAbduction", playerRitual: false, completed: true);
        AnomalyFamily.PsychicRitualCompleted(s, "Lord_4/0/4000", "ludeon.rimworld.anomaly/Chronophagy", playerRitual: true, completed: false);

        // Entity escapes and pit gate collapses.
        AnomalyFamily.EntityEscaped(s, "Thing_Shambler1", playerPlatform: true, escapedFromPlatform: true);
        AnomalyFamily.EntityEscaped(s, "Thing_Revenant2", playerPlatform: true, escapedFromPlatform: true);
        AnomalyFamily.EntityEscaped(s, "Thing_Other", playerPlatform: false, escapedFromPlatform: true);

        AnomalyFamily.PitGateCollapsed(s, "Thing_PitGate9", confirmedCollapse: true);
        AnomalyFamily.PitGateCollapsed(s, "Thing_PitGate9", confirmedCollapse: true);
        AnomalyFamily.PitGateCollapsed(s, "Thing_PitGate10", confirmedCollapse: false);

        var draft = acc.PrepareSave(5000, T0, "cp-anom-1")!;
        var parsed = CheckpointJson.Parse(draft.Json).Payload!;
        var obs = CheckpointMetrics.ToObservation(
            parsed, draft.Json,
            new ColonyIdentity { SeedString = "anom", StartingTile = "10,0", FirstWealthBits = 1 },
            "1.7.5011 rev24",
            new List<string> { AcquisitionUnits.Anomaly });

        Assert.Equal(2L, obs.Metrics[AnomalyFamily.PsychicRituals].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Complete, obs.Metrics[AnomalyFamily.PsychicRituals].Values[MetricEntry.Scalar].Coverage);

        var ritualByDef = obs.Metrics[AnomalyFamily.PsychicRitualsByDef].Values;
        Assert.Equal(2L, ritualByDef[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, ritualByDef["ludeon.rimworld.anomaly/VoidProvocation"].IntegerValue);
        Assert.Equal(1L, ritualByDef["author.anomalymod/ChronoChant"].IntegerValue);
        Assert.False(ritualByDef.ContainsKey("ludeon.rimworld.anomaly/PsychicSkipAbduction"));

        Assert.Equal(2L, obs.Metrics[AnomalyFamily.EntityEscapes].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, obs.Metrics[AnomalyFamily.EntitiesContained].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, obs.Metrics[AnomalyFamily.PitGatesCollapsed].Values[MetricEntry.Scalar].IntegerValue);
    }

    [Fact]
    public void SaveReader_SalvagesAnomalyCodexMonolithAndHoldingPlatformsFrom17Save()
    {
        var root = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
            .MetaMods("1.7.5011 rev24", "ludeon.rimworld", AcquisitionUnits.Anomaly)
            .Scenario("The Anomaly", playerFactionDef: "PlayerColony")
            .Build()));
        var game = root.Root!.Element("game")!;
        var world = game.Element("world")!;
        world.Add(new XElement("factionManager", new XElement("allFactions",
            new XElement("li", new XElement("def", "PlayerColony"), new XElement("loadID", 10)),
            new XElement("li", new XElement("def", "Entities"), new XElement("loadID", 99)))));
        world.Add(new XElement("worldObjects", new XElement("worldObjects",
            new XElement("li", new XElement("def", "Settlement"), new XElement("tile", "50,0"),
                new XElement("ID", 7), new XElement("faction", "Faction_10")))));

        game.Add(new XElement("entityCodex",
            new XElement("discoveredEntities",
                new XElement("li", "Shambler"),
                new XElement("li", "Sightstealer"),
                new XElement("li", "Revenant"),
                new XElement("li", "Shambler"))));

        game.Add(new XElement("components",
            new XElement("li", new XAttribute("Class", "GameComponent_Anomaly"),
                new XElement("levelDef", "Waking"),
                new XElement("highestLevelReached", 2),
                new XElement("monolith", "Thing_VoidMonolith1"))));

        XElement HeldPawn(string id, string? healthState = null)
            => new("li", new XAttribute("Class", "Pawn"),
                new XElement("id", id),
                healthState is null
                    ? new XElement("healthTracker")
                    : new XElement("healthTracker", new XElement("healthState", healthState)));

        game.Add(new XElement("maps",
            new XElement("li",
                new XElement("uniqueID", 0),
                new XElement("mapInfo", new XElement("parent", "WorldObject_7")),
                new XElement("things",
                    // Player platform holding 1 mobile entity
                    new XElement("thing", new XAttribute("Class", "Building_HoldingPlatform"),
                        new XElement("id", "Thing_HoldingPlatform1"),
                        new XElement("faction", "Faction_10"),
                        new XElement("innerContainer",
                            new XElement("innerList", HeldPawn("Thing_Sightstealer1")))),
                    // Player platform holding 1 downed entity
                    new XElement("thing", new XAttribute("Class", "Building_HoldingPlatform"),
                        new XElement("id", "Thing_HoldingPlatform2"),
                        new XElement("faction", "Faction_10"),
                        new XElement("innerContainer",
                            new XElement("innerList", HeldPawn("Thing_Revenant1", "Down")))),
                    // Player platform with a dead corpse (must not count)
                    new XElement("thing", new XAttribute("Class", "Building_HoldingPlatform"),
                        new XElement("id", "Thing_HoldingPlatform3"),
                        new XElement("faction", "Faction_10"),
                        new XElement("innerContainer",
                            new XElement("innerList", HeldPawn("Thing_DeadGhoul", "Dead")))),
                    // Non-player platform (must not count)
                    new XElement("thing", new XAttribute("Class", "Building_HoldingPlatform"),
                        new XElement("id", "Thing_HoldingPlatformHostile"),
                        new XElement("faction", "Faction_99"),
                        new XElement("innerContainer",
                            new XElement("innerList", HeldPawn("Thing_HostileEntity"))))))));

        var bytes = Encoding.UTF8.GetBytes(root.ToString());
        var read = new SaveReader().ReadDetailed(new MemoryStream(bytes), "anomaly.rws", bytes.Length, T0);
        Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
        var m = read.Metrics!;
        Assert.Equal(MetricEvidence.Experimental, m.EvidenceByAcquisition![AnomalyStateSnapshot.Unit]);

        Assert.Equal(3L, m.Metrics[AnomalyStateSnapshot.DiscoveredEntities].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, m.Metrics[AnomalyStateSnapshot.DiscoveredEntities].Values[MetricEntry.Scalar].Coverage);

        Assert.Equal(2L, m.Metrics[AnomalyStateSnapshot.MonolithHighestLevel].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, m.Metrics[AnomalyStateSnapshot.MonolithHighestLevel].Values[MetricEntry.Scalar].Coverage);

        Assert.Equal(2L, m.Metrics[AnomalyStateSnapshot.HeldEntitiesPeak].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, m.Metrics[AnomalyStateSnapshot.HeldEntitiesPeak].Values[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void SaveReader_DoesNotFabricateZeroMonolithOrAnomalyStateWhenInactiveOrAmbientHorror()
    {
        // Ambient Horror save: Anomaly active, monolith == "null" and highestLevelReached omitted.
        var ambientRoot = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
            .MetaMods("1.7.5011 rev24", "ludeon.rimworld", AcquisitionUnits.Anomaly)
            .Scenario("Ambient Horror", playerFactionDef: "PlayerColony")
            .Build()));
        ambientRoot.Root!.Element("game")!.Add(
            new XElement("entityCodex", new XElement("discoveredEntities")),
            new XElement("components",
                new XElement("li", new XAttribute("Class", "GameComponent_Anomaly"),
                    new XElement("monolith", "null"))));

        var ambientBytes = Encoding.UTF8.GetBytes(ambientRoot.ToString());
        var ambientRead = new SaveReader().ReadDetailed(new MemoryStream(ambientBytes), "ambient.rws", ambientBytes.Length, T0);
        Assert.Equal(ObservationStatus.Ok, ambientRead.Observation.Status);
        Assert.False(ambientRead.Metrics!.Metrics.ContainsKey(AnomalyStateSnapshot.MonolithHighestLevel));
        Assert.False(ambientRead.Metrics.Metrics.ContainsKey(AnomalyStateSnapshot.DiscoveredEntities));
        Assert.False(ambientRead.Metrics.Metrics.ContainsKey(AnomalyStateSnapshot.HeldEntitiesPeak));

        // Non-Anomaly historical save: even if stale XML tags exist, SaveReader emits nothing (Unknown, never 0).
        var noAnomalyRoot = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
            .MetaMods("1.7.5011 rev24", "ludeon.rimworld")
            .Build()));
        noAnomalyRoot.Root!.Element("game")!.Add(
            new XElement("entityCodex", new XElement("discoveredEntities", new XElement("li", "Shambler"))),
            new XElement("components",
                new XElement("li", new XAttribute("Class", "GameComponent_Anomaly"),
                    new XElement("highestLevelReached", 2),
                    new XElement("monolith", "Thing_VoidMonolith1"))));
        var noAnomalyBytes = Encoding.UTF8.GetBytes(noAnomalyRoot.ToString());
        var noAnomalyRead = new SaveReader().ReadDetailed(new MemoryStream(noAnomalyBytes), "vanilla.rws", noAnomalyBytes.Length, T0);
        Assert.False(noAnomalyRead.Metrics!.Metrics.ContainsKey(AnomalyStateSnapshot.DiscoveredEntities));
        Assert.False(noAnomalyRead.Metrics.Metrics.ContainsKey(AnomalyStateSnapshot.MonolithHighestLevel));

        // When SaveSnapshot / Checkpoint observations exist with ActiveDlcIds lacking Anomaly, both units are NotApplicable.
        var profile = new Profile
        {
            SaveObservations = { noAnomalyRead.Observation },
            MetricObservations =
            {
                noAnomalyRead.Metrics,
                new MetricObservation
                {
                    Source = MetricObservationSource.SaveSnapshot,
                    Key = "snap-no-anomaly",
                    Identity = noAnomalyRead.Observation.Identity.Clone(),
                    FormatVersion = SaveReader.Version,
                    SaveTick = noAnomalyRead.Observation.Values.TicksGame,
                    ObservedUtc = T0,
                    ActiveDlcIds = new List<string>(),
                    ModSetHash = new string('c', 64),
                },
                new MetricObservation
                {
                    Source = MetricObservationSource.Checkpoint,
                    Key = "cp-no-anomaly",
                    Identity = noAnomalyRead.Observation.Identity.Clone(),
                    FormatVersion = SaveReader.Version,
                    SaveTick = noAnomalyRead.Observation.Values.TicksGame,
                    ObservedUtc = T0,
                    ActiveDlcIds = new List<string>(),
                    ModSetHash = new string('c', 64),
                },
            },
        };
        var colonies = CampaignMatcher.BuildColonies(profile);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        var defs = MetricDisplayPolicy.SelectDefinitions(MetricRegistry.Catalog, MetricDisplayPolicy.AllDetailsVisibleUnits);
        var view = MetricCalculator.BuildView(profile, colonies, stats, defs);
        var colonyRows = view.Colonies.Single().Rows.ToDictionary(r => r.Definition.Key, StringComparer.Ordinal);

        Assert.Equal(MetricCoverage.NotApplicable, colonyRows[AnomalyStateSnapshot.DiscoveredEntities].Value.Coverage);
        Assert.Equal(0, colonyRows[AnomalyStateSnapshot.DiscoveredEntities].Targets);
        Assert.Equal(MetricCoverage.NotApplicable, colonyRows[AnomalyFamily.PsychicRituals].Value.Coverage);
        Assert.Equal(0, colonyRows[AnomalyFamily.PsychicRituals].Targets);
    }
}
