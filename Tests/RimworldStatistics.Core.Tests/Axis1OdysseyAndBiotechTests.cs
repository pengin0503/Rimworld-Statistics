using System.Text;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class Axis1OdysseyAndBiotechTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OdysseyStateSnapshot_HandlesLandedAndMidFlightGravshipsAndDeduplicatesUniqueWeapons()
    {
        var landed = OdysseyStateSnapshot.Build(
            substructureCells: 184,
            thrusters: 6,
            uniqueWeapons:
            [
                new UniqueWeaponFact("Thing_Gun_ChargeRifle1", ["ludeon.rimworld.odyssey/AimAssistance", "ludeon.rimworld.odyssey/HighCapacity"]),
                new UniqueWeaponFact("Thing_Gun_ChargeRifle1", ["ludeon.rimworld.odyssey/AimAssistance"]), // duplicate ID weakens to Partial & does not double-count
                new UniqueWeaponFact("Thing_MeleeWeapon_MonoSword2", ["ludeon.rimworld.odyssey/Lightweight"]),
            ]);

        Assert.Equal(MetricCoverage.SnapshotOnly, landed[OdysseyStateSnapshot.SubstructureCellsPeak].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(184L, landed[OdysseyStateSnapshot.SubstructureCellsPeak].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(6L, landed[OdysseyStateSnapshot.ThrustersPeak].Values[MetricEntry.Scalar].IntegerValue);

        // Duplicate weapon ID weakened weapon & trait coverage to Partial while counting only 2 distinct weapons.
        Assert.Equal(MetricCoverage.Partial, landed[OdysseyStateSnapshot.UniqueWeaponsPeak].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(2L, landed[OdysseyStateSnapshot.UniqueWeaponsPeak].Values[MetricEntry.Scalar].IntegerValue);

        var byTrait = landed[OdysseyStateSnapshot.UniqueWeaponsByTrait].Values;
        Assert.Equal(MetricCoverage.Partial, byTrait[MetricEntry.Scalar].Coverage);
        Assert.Equal(3L, byTrait[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, byTrait["ludeon.rimworld.odyssey/AimAssistance"].IntegerValue);
        Assert.Equal(1L, byTrait["ludeon.rimworld.odyssey/HighCapacity"].IntegerValue);
        Assert.Equal(1L, byTrait["ludeon.rimworld.odyssey/Lightweight"].IntegerValue);

        // Mid-flight / no landed gravship: null substructure & thrusters become NotApplicable (never fabricated 0).
        var midFlight = OdysseyStateSnapshot.Build(
            substructureCells: null,
            thrusters: null,
            uniqueWeapons: [new UniqueWeaponFact("Thing_Gun_UniqueRevolver9", ["ludeon.rimworld.odyssey/AimAssistance"])]);

        Assert.Equal(MetricCoverage.NotApplicable, midFlight[OdysseyStateSnapshot.SubstructureCellsPeak].Values[MetricEntry.Scalar].Coverage);
        Assert.Null(midFlight[OdysseyStateSnapshot.SubstructureCellsPeak].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.NotApplicable, midFlight[OdysseyStateSnapshot.ThrustersPeak].Values[MetricEntry.Scalar].Coverage);
        Assert.Null(midFlight[OdysseyStateSnapshot.ThrustersPeak].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.SnapshotOnly, midFlight[OdysseyStateSnapshot.UniqueWeaponsPeak].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, midFlight[OdysseyStateSnapshot.UniqueWeaponsPeak].Values[MetricEntry.Scalar].IntegerValue);

        // Unknown trait bucket weakens trait breakdown without corrupting weapon count.
        var unknownTrait = OdysseyStateSnapshot.Build(
            substructureCells: 40,
            thrusters: 2,
            uniqueWeapons: [new UniqueWeaponFact("Thing_ModdedUnique1", [""])]);
        Assert.Equal(MetricCoverage.SnapshotOnly, unknownTrait[OdysseyStateSnapshot.UniqueWeaponsPeak].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(MetricCoverage.Partial, unknownTrait[OdysseyStateSnapshot.UniqueWeaponsByTrait].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, unknownTrait[OdysseyStateSnapshot.UniqueWeaponsByTrait].Values[MetricKeys.UnknownBucket].IntegerValue);
    }

    [Fact]
    public void OdysseyVoyageFamily_TracksFuelConsumptionOrbitArrivalsAndDistinctLandmarkTiles()
    {
        var acc = LiveMetricAccumulator.StartNew([OdysseyVoyageFamily.Family.Spec], 0);
        var s = acc.Family(OdysseyVoyageFamily.Name)!;

        OdysseyVoyageFamily.FuelConsumed(s, 450.5, playerShip: true);
        OdysseyVoyageFamily.FuelConsumed(s, 149.5, playerShip: true);
        OdysseyVoyageFamily.FuelConsumed(s, 999.0, playerShip: false);
        OdysseyVoyageFamily.FuelConsumed(s, 0.0, playerShip: true);

        OdysseyVoyageFamily.OrbitArrived(s, "gravship/0/12000/10,1", playerArrival: true, isOrbitLayer: true);
        OdysseyVoyageFamily.OrbitArrived(s, "gravship/0/12000/10,1", playerArrival: true, isOrbitLayer: true); // duplicate ignored
        OdysseyVoyageFamily.OrbitArrived(s, "pod/7/18000/22,1", playerArrival: true, isOrbitLayer: true);
        OdysseyVoyageFamily.OrbitArrived(s, "gravship/0/24000/50,0", playerArrival: true, isOrbitLayer: false);
        OdysseyVoyageFamily.OrbitArrived(s, "pod/8/25000/22,1", playerArrival: false, isOrbitLayer: true);

        OdysseyVoyageFamily.LandmarkTileVisited(s, "100,0", "ludeon.rimworld.odyssey/AncientLaunchSite", playerVisited: true);
        OdysseyVoyageFamily.LandmarkTileVisited(s, "100,0", "ludeon.rimworld.odyssey/AncientLaunchSite", playerVisited: true); // revisit same tile ignored
        OdysseyVoyageFamily.LandmarkTileVisited(s, "205,0", "ludeon.rimworld.odyssey/AncientLaunchSite", playerVisited: true);
        OdysseyVoyageFamily.LandmarkTileVisited(s, "310,1", "ludeon.rimworld.odyssey/OrbitalWreck", playerVisited: true);
        OdysseyVoyageFamily.LandmarkTileVisited(s, "400,0", "ludeon.rimworld.odyssey/LavaField", playerVisited: false);

        var draft = acc.PrepareSave(30000, T0, "cp-odyssey-1")!;
        var parsed = CheckpointJson.Parse(draft.Json).Payload!;
        var obs = CheckpointMetrics.ToObservation(
            parsed, draft.Json,
            new ColonyIdentity { SeedString = "odyssey", StartingTile = "10,0", FirstWealthBits = 1 },
            "1.7.5011 rev24",
            [AcquisitionUnits.Odyssey]);

        Assert.Equal(600.0, obs.Metrics[OdysseyVoyageFamily.ChemfuelConsumed].Values[MetricEntry.Scalar].RealValue!.Value, 3);
        Assert.Equal(MetricCoverage.Complete, obs.Metrics[OdysseyVoyageFamily.ChemfuelConsumed].Values[MetricEntry.Scalar].Coverage);

        Assert.Equal(2L, obs.Metrics[OdysseyVoyageFamily.OrbitArrivals].Values[MetricEntry.Scalar].IntegerValue);

        var landmarks = obs.Metrics[OdysseyVoyageFamily.VisitedLandmarks].Values;
        Assert.Equal(3L, landmarks[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, landmarks["ludeon.rimworld.odyssey/AncientLaunchSite"].IntegerValue);
        Assert.Equal(1L, landmarks["ludeon.rimworld.odyssey/OrbitalWreck"].IntegerValue);
        Assert.False(landmarks.ContainsKey("ludeon.rimworld.odyssey/LavaField"));
    }

    [Fact]
    public void BiotechGeneticsFamily_TracksVatsGeneticsImplantationsAndExactFirstBossDefeats()
    {
        var acc = LiveMetricAccumulator.StartNew([BiotechGeneticsFamily.Family.Spec], 0);
        var s = acc.Family(BiotechGeneticsFamily.Name)!;

        BiotechGeneticsFamily.GrowthVatCompleted(s, playerVat: true, livingResult: true);
        BiotechGeneticsFamily.GrowthVatCompleted(s, playerVat: true, livingResult: true);
        BiotechGeneticsFamily.GrowthVatCompleted(s, playerVat: true, livingResult: false); // stillborn excluded
        BiotechGeneticsFamily.GrowthVatCompleted(s, playerVat: false, livingResult: true);

        BiotechGeneticsFamily.GeneExtracted(s, playerExtractor: true, genepackProduced: true);
        BiotechGeneticsFamily.GeneExtracted(s, playerExtractor: true, genepackProduced: false);

        BiotechGeneticsFamily.XenogermAssembled(s, playerAssembler: true, xenogermProduced: true);
        BiotechGeneticsFamily.XenogermAssembled(s, playerAssembler: false, xenogermProduced: true);

        BiotechGeneticsFamily.XenogermImplanted(s, "item/Thing_Human1/Thing_Xenogerm5/6000", colonyAffiliated: true, implanted: true);
        BiotechGeneticsFamily.XenogermImplanted(s, "item/Thing_Human1/Thing_Xenogerm5/6000", colonyAffiliated: true, implanted: true); // duplicate ignored
        BiotechGeneticsFamily.XenogermImplanted(s, "reimplant/Thing_Human2/Thing_Human3/9000", colonyAffiliated: true, implanted: true);
        BiotechGeneticsFamily.XenogermImplanted(s, "reimplant/Thing_Hostile8/Thing_Hostile9/9500", colonyAffiliated: false, implanted: true);

        BiotechGeneticsFamily.BossFirstDefeated(s, HistoricalBiotechBossReader.Diabolus, 120_000); // Day 2.0
        BiotechGeneticsFamily.BossFirstDefeated(s, HistoricalBiotechBossReader.Diabolus, 240_000); // second defeat ignored (keeps first)
        BiotechGeneticsFamily.BossFirstDefeated(s, HistoricalBiotechBossReader.Warqueen, 300_000); // Day 5.0

        var draft = acc.PrepareSave(360_000, T0, "cp-biotech-1")!;
        var parsed = CheckpointJson.Parse(draft.Json).Payload!;
        var obs = CheckpointMetrics.ToObservation(
            parsed, draft.Json,
            new ColonyIdentity { SeedString = "biotech", StartingTile = "12,0", FirstWealthBits = 2 },
            "1.7.5011 rev24",
            [AcquisitionUnits.Biotech]);

        Assert.Equal(2L, obs.Metrics[BiotechGeneticsFamily.GrowthVatCompletions].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, obs.Metrics[BiotechGeneticsFamily.GeneExtractions].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, obs.Metrics[BiotechGeneticsFamily.XenogermsAssembled].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, obs.Metrics[BiotechGeneticsFamily.GenesImplanted].Values[MetricEntry.Scalar].IntegerValue);

        var bossTicks = obs.Metrics[BiotechGeneticsFamily.BossFirstDefeatedTicks].Values;
        Assert.Equal(2L, bossTicks[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2.0, bossTicks[HistoricalBiotechBossReader.Diabolus].RealValue!.Value, 5);
        Assert.Equal(5.0, bossTicks[HistoricalBiotechBossReader.Warqueen].RealValue!.Value, 5);
    }

    [Fact]
    public void SaveReader_SalvagesBiotechBossgroupsFrom17SaveAndChronicleBuildsExactOrIntervalMilestonesPerBranch()
    {
        static (SaveObservation Obs, MetricObservation Metrics) ReadBiotechSave(
            string fileName,
            int ticksGame,
            DateTime lastWriteUtc,
            string[] killedBosses,
            (string Key, int Count)[] calledCounts,
            bool biotechActive = true)
        {
            var mods = biotechActive
                ? new[] { "ludeon.rimworld", AcquisitionUnits.Biotech }
                : new[] { "ludeon.rimworld" };
            var root = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
                .MetaMods("1.7.5011 rev24", mods)
                .Tick(ticksGame)
                .Build()));
            var game = root.Root!.Element("game")!;
            var comps = game.Element("components") ?? new XElement("components");
            if (comps.Parent is null)
                game.Add(comps);
            comps.Add(
                new XElement("li", new XAttribute("Class", "RimworldStatistics.GameComponent_RimworldStatistics"),
                    new XElement("campaignId", "camp-bossgroup-1")),
                new XElement("li", new XAttribute("Class", "GameComponent_Bossgroup"),
                    new XElement("killedBosses", killedBosses.Select(b => new XElement("li", b))),
                    new XElement("timesCalledBossgroups",
                        new XElement("keys", calledCounts.Select(c => new XElement("li", c.Key))),
                        new XElement("values", calledCounts.Select(c => new XElement("li", c.Count))))));

            var bytes = Encoding.UTF8.GetBytes(root.ToString());
            var read = new SaveReader().ReadDetailed(new MemoryStream(bytes), fileName, bytes.Length, lastWriteUtc);
            Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
            return (read.Observation, read.Metrics!);
        }

        // 1. Non-Biotech save never fabricates 0 even if stale XML exists.
        var vanilla = ReadBiotechSave("vanilla.rws", 60_000, T0, ["Diabolus"], [("Diabolus", 1)], biotechActive: false);
        Assert.False(vanilla.Metrics.Metrics.ContainsKey(BiotechStateSnapshot.BossgroupsSummoned));
        Assert.False(vanilla.Metrics.Metrics.ContainsKey(BiotechStateSnapshot.BossesDefeated));

        // 2. Historical Biotech saves: early save (no bosses defeated yet) -> later save (Diabolus summoned 2x & defeated, modded 0-count gizmo entry ignored).
        var save1 = ReadBiotechSave(
            "save1.rws",
            ticksGame: 60_000,
            lastWriteUtc: T0,
            killedBosses: [],
            calledCounts: [("Diabolus", 0), ("ModdedZeroGizmoBoss", 0)]);
        var save2 = ReadBiotechSave(
            "save2.rws",
            ticksGame: 180_000,
            lastWriteUtc: T0.AddHours(1),
            killedBosses: ["Diabolus"],
            calledCounts: [("Diabolus", 2), ("Warqueen", 1)]);

        Assert.Equal(MetricEvidence.Experimental, save2.Metrics.EvidenceByAcquisition!["biotech_state"]);
        Assert.Equal(3L, save2.Metrics.Metrics[BiotechStateSnapshot.BossgroupsSummoned].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, save2.Metrics.Metrics[BiotechStateSnapshot.BossgroupsSummoned].Values[HistoricalBiotechBossReader.Diabolus].IntegerValue);
        Assert.Equal(1L, save2.Metrics.Metrics[BiotechStateSnapshot.BossgroupsSummoned].Values[HistoricalBiotechBossReader.Warqueen].IntegerValue);
        Assert.Equal(1L, save2.Metrics.Metrics[BiotechStateSnapshot.BossesDefeated].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, save2.Metrics.Metrics[BiotechStateSnapshot.BossesDefeated].Values[HistoricalBiotechBossReader.Diabolus].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, save2.Metrics.Metrics[BiotechStateSnapshot.BossesDefeated].Values[MetricEntry.Scalar].Coverage);

        // Link save1 -> save2 via checkpoints on Branch A, plus a sibling Branch B save3 where Apocriton was defeated at exact tick 240_000.
        static void LinkAlias(MetricObservation obs, SaveObservation save)
            => obs.FileAliases.Add(new MetricFileAlias
            {
                FileName = save.FileName,
                FileSize = save.FileSize,
                LastWriteUtc = save.LastWriteUtc,
            });

        var cp1 = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            Key = "cp-1",
            PayloadHash = "hash-cp-1",
            Identity = save1.Obs.Identity.Clone(),
            FormatVersion = SaveReader.Version,
            SaveTick = 60_000,
            ObservedUtc = T0,
            GameVersion = "1.7.5011 rev24",
            ModSetHash = save1.Metrics.ModSetHash,
            ActiveDlcIds = [AcquisitionUnits.Biotech],
        };
        LinkAlias(cp1, save1.Obs);

        var cp2 = new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint,
            Key = "cp-2",
            ParentKey = "cp-1",
            PayloadHash = "hash-cp-2",
            Identity = save2.Obs.Identity.Clone(),
            FormatVersion = SaveReader.Version,
            SaveTick = 180_000,
            ObservedUtc = T0.AddHours(1),
            GameVersion = "1.7.5011 rev24",
            ModSetHash = save2.Metrics.ModSetHash,
            ActiveDlcIds = [AcquisitionUnits.Biotech],
        };
        LinkAlias(cp2, save2.Obs);

        var save3 = ReadBiotechSave(
            "branchB.rws",
            ticksGame: 300_000,
            lastWriteUtc: T0.AddHours(2),
            killedBosses: ["Apocriton"],
            calledCounts: [("Apocriton", 1)]);

        var accB = LiveMetricAccumulator.StartNew([BiotechGeneticsFamily.Family.Spec], 60_000);
        BiotechGeneticsFamily.BossFirstDefeated(accB.Family(BiotechGeneticsFamily.Name)!, HistoricalBiotechBossReader.Apocriton, 240_000);
        var draftB = accB.PrepareSave(300_000, T0.AddHours(2), "cp-1")!;
        var cp3 = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(draftB.Json).Payload!,
            draftB.Json,
            save3.Obs.Identity.Clone(),
            "1.7.5011 rev24",
            [AcquisitionUnits.Biotech]);
        cp3.Key = "cp-3";
        cp3.ParentKey = "cp-1";
        cp3.PayloadHash = "hash-cp-3";
        cp3.ModSetHash = save3.Metrics.ModSetHash;
        LinkAlias(cp3, save3.Obs);

        var profile = new Profile
        {
            SaveObservations = { save1.Obs, save2.Obs, save3.Obs },
            MetricObservations = { save1.Metrics, save2.Metrics, save3.Metrics, cp1, cp2, cp3 },
        };
        var colonies = CampaignMatcher.BuildColonies(profile);
        var colony = Assert.Single(colonies);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        var view = new StatsView
        {
            Colonies = colonies,
            Stats = stats,
            Numeric = MetricCalculator.BuildView(
                profile,
                colonies,
                stats,
                MetricRegistry.Compose(profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits)),
        };
        var lineage = ColonySaveLineage.Build(profile, colonies, colony.Key);

        // Branch B (cp-3, latest saveTick 300_000) is default representative: has exact Apocriton defeat at 240_000 and must NOT leak Branch A's Diabolus defeat.
        var chronicleB = ColonyChronicle.Build(view, colony.Key, lineage)!;
        var apocritonEntry = Assert.Single(chronicleB.Entries, e => e.MilestoneKind == ChronicleMilestoneKind.BossApocritonDefeated);
        Assert.Equal(ChronicleTimingKind.ExactTick, apocritonEntry.TimingKind);
        Assert.Equal(240_000, apocritonEntry.StartTick);
        Assert.Equal(MetricCoverage.Complete, apocritonEntry.Coverage);
        Assert.DoesNotContain(chronicleB.Entries, e => e.MilestoneKind == ChronicleMilestoneKind.BossDiabolusDefeated);

        // Selecting Branch A (cp-2 node) shows Diabolus bounded ProvenInterval [60_000, 180_000] and must NOT leak Branch B's Apocriton defeat.
        var branchAKey = ChronicleBranchKey.Of(lineage.Nodes.Single(n => n.Kind == SaveLineageNodeKind.Checkpoint && n.CheckpointId == "cp-2"));
        var chronicleA = ColonyChronicle.Build(view, colony.Key, lineage, branchAKey)!;
        var diabolusEntry = Assert.Single(chronicleA.Entries, e => e.MilestoneKind == ChronicleMilestoneKind.BossDiabolusDefeated);
        Assert.Equal(ChronicleTimingKind.ProvenInterval, diabolusEntry.TimingKind);
        Assert.Equal(60_000, diabolusEntry.StartTick);
        Assert.Equal(180_000, diabolusEntry.EndTick);
        Assert.Equal(MetricCoverage.Partial, diabolusEntry.Coverage);
        Assert.DoesNotContain(chronicleA.Entries, e => e.MilestoneKind == ChronicleMilestoneKind.BossApocritonDefeated);
    }
}
