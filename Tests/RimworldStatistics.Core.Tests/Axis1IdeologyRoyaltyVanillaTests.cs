using System.Text;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class Axis1IdeologyRoyaltyVanillaTests
{
    private static readonly DateTime T0 = new(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void IdeologyStateSnapshot_AndHistoricalReader_RespectsFluidVsFixedAndOmittedIdZero()
    {
        var built = IdeologyStateSnapshot.Build(fluidReforms: null, connectedGauranlenTrees: 0, collectedRelics: 2);
        Assert.Equal(MetricCoverage.NotApplicable, built[IdeologyStateSnapshot.FluidReforms].Values[MetricEntry.Scalar].Coverage);
        Assert.Null(built[IdeologyStateSnapshot.FluidReforms].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.SnapshotOnly, built[IdeologyStateSnapshot.ConnectedGauranlenTreesPeak].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(0L, built[IdeologyStateSnapshot.ConnectedGauranlenTreesPeak].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, built[IdeologyStateSnapshot.CollectedRelicsPeak].Values[MetricEntry.Scalar].IntegerValue);

        static SaveReadResult ReadIdeoSave(
            bool ideologyDlc,
            XElement ideosElement,
            string playerPrimaryRef = "Ideo_0",
            string[]? minorRefs = null)
        {
            var mods = ideologyDlc
                ? new[] { "ludeon.rimworld", AcquisitionUnits.Ideology }
                : new[] { "ludeon.rimworld" };
            var root = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
                .MetaMods("1.7.5011 rev24", mods)
                .Tick(120_000, start: 3_600_000)
                .Build()));
            var game = root.Root!.Element("game")!;
            var world = game.Element("world")!;
            var factionManager = new XElement("factionManager",
                new XElement("allFactions",
                    new XElement("li",
                        new XElement("def", "PlayerColony"),
                        new XElement("name", "Ideo Settlement"),
                        new XElement("loadID", 1),
                        new XElement("relations"),
                        new XElement("ideos",
                            new XElement("primaryIdeo", playerPrimaryRef),
                            new XElement("ideosMinor", (minorRefs ?? []).Select(r => new XElement("li", r)))))));
            world.Add(factionManager);
            world.Add(new XElement("ideoManager", ideosElement));

            var bytes = Encoding.UTF8.GetBytes(root.ToString());
            return new SaveReader().ReadDetailed(new MemoryStream(bytes), "ideo.rws", bytes.Length, T0);
        }

        // 1. Omitted <id> resolves to Ideo_0 for fluid player ideo and extracts reformCount conservatively.
        var fluidRead = ReadIdeoSave(
            ideologyDlc: true,
            new XElement("ideos",
                new XElement("li",
                    new XElement("fluid", "True"),
                    new XElement("development", new XElement("reformCount", 3))),
                new XElement("li",
                    new XElement("id", 5),
                    new XElement("fluid", "False"))));
        Assert.Equal(ObservationStatus.Ok, fluidRead.Observation.Status);
        var fluidScalar = fluidRead.Metrics!.Metrics[IdeologyStateSnapshot.FluidReforms].Values[MetricEntry.Scalar];
        Assert.Equal(MetricCoverage.Partial, fluidScalar.Coverage);
        Assert.True(fluidScalar.Weakened);
        Assert.Equal(3L, fluidScalar.IntegerValue);

        // 2. Fixed (non-fluid) player ideo emits NotApplicable rather than fabricating 0.
        var fixedRead = ReadIdeoSave(
            ideologyDlc: true,
            new XElement("ideos",
                new XElement("li",
                    new XElement("id", 7),
                    new XElement("fluid", "False"))),
            playerPrimaryRef: "Ideo_7");
        var fixedScalar = fixedRead.Metrics!.Metrics[IdeologyStateSnapshot.FluidReforms].Values[MetricEntry.Scalar];
        Assert.Equal(MetricCoverage.NotApplicable, fixedScalar.Coverage);
        Assert.Null(fixedScalar.IntegerValue);

        // 3. Non-Ideology save never emits fluid reform metrics even if XML nodes appear.
        var nonIdeoRead = ReadIdeoSave(
            ideologyDlc: false,
            new XElement("ideos",
                new XElement("li",
                    new XElement("fluid", "True"),
                    new XElement("development", new XElement("reformCount", 2)))));
        Assert.False(nonIdeoRead.Metrics!.Metrics.ContainsKey(IdeologyStateSnapshot.FluidReforms));

        // 4. Unresolved reference leaves the metric Unknown (unemitted).
        var unresolvedRead = ReadIdeoSave(
            ideologyDlc: true,
            new XElement("ideos",
                new XElement("li",
                    new XElement("id", 1),
                    new XElement("fluid", "True"),
                    new XElement("development", new XElement("reformCount", 1)))),
            playerPrimaryRef: "Ideo_99");
        Assert.False(unresolvedRead.Metrics!.Metrics.ContainsKey(IdeologyStateSnapshot.FluidReforms));
    }

    [Fact]
    public void IdeologyExpansionFamily_AndRoyaltyFamilies_TrackConfirmationsAndMarkLegacyPartial()
    {
        var acc = LiveMetricAccumulator.StartNew(
        [
            IdeologyExpansionFamily.Family.Spec,
            RoyaltyFamily.Family.Spec,
            RoyaltyLinkingFamily.Family.Spec,
        ], 0);

        var ideo = acc.Family(IdeologyExpansionFamily.Name)!;
        IdeologyExpansionFamily.PrisonerEnslaved(ideo, wasColonyPrisoner: true, nowColonySlave: true);
        IdeologyExpansionFamily.PrisonerEnslaved(ideo, wasColonyPrisoner: false, nowColonySlave: true);
        IdeologyExpansionFamily.PrisonerEnslaved(ideo, wasColonyPrisoner: true, nowColonySlave: false);
        IdeologyExpansionFamily.BiosculpterCycleCompleted(ideo, playerPod: true, colonyOccupant: true, "medic");
        IdeologyExpansionFamily.BiosculpterCycleCompleted(ideo, playerPod: true, colonyOccupant: true, "bioregeneration");
        IdeologyExpansionFamily.BiosculpterCycleCompleted(ideo, playerPod: true, colonyOccupant: true, ""); // unknown bucket -> Partial
        IdeologyExpansionFamily.BiosculpterCycleCompleted(ideo, playerPod: false, colonyOccupant: true, "pleasure");

        var roy = acc.Family(RoyaltyFamily.Name)!;
        RoyaltyFamily.PermitUsed(roy, playerOwned: true, confirmed: true, "ludeon.rimworld.royalty/CallMilitaryAidSmall");
        RoyaltyFamily.PermitUsed(roy, playerOwned: false, confirmed: true, "ludeon.rimworld.royalty/CallMilitaryAidSmall");
        RoyaltyFamily.PsycastSucceeded(roy, playerFreeColonist: true, confirmed: true, "ludeon.rimworld.royalty/Skip");
        RoyaltyFamily.PsycastSucceeded(roy, playerFreeColonist: true, confirmed: true, "ludeon.rimworld.royalty/Skip");
        RoyaltyFamily.PsycastSucceeded(roy, playerFreeColonist: true, confirmed: false, "ludeon.rimworld.royalty/Skip");

        var link = acc.Family(RoyaltyLinkingFamily.Name)!;
        RoyaltyLinkingFamily.AnimaLinkingCompleted(link, colonyPawn: true, beforePsylinkLevel: 1, afterPsylinkLevel: 2);
        RoyaltyLinkingFamily.AnimaLinkingCompleted(link, colonyPawn: true, beforePsylinkLevel: 2, afterPsylinkLevel: 2); // no gain ignored
        RoyaltyLinkingFamily.AnimaLinkingCompleted(link, colonyPawn: false, beforePsylinkLevel: 0, afterPsylinkLevel: 1);

        var draft = acc.PrepareSave(60_000, T0, "cp-ideo-roy-1")!;
        var obs = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(draft.Json).Payload!,
            draft.Json,
            new ColonyIdentity { SeedString = "ideo-roy", StartingTile = "5,0", FirstWealthBits = 1 },
            "1.7.5011 rev24",
            [AcquisitionUnits.Ideology, AcquisitionUnits.Royalty]);

        Assert.Equal(1L, obs.Metrics[IdeologyExpansionFamily.PrisonersEnslaved].Values[MetricEntry.Scalar].IntegerValue);
        var bioValues = obs.Metrics[IdeologyExpansionFamily.BiosculpterCompletions].Values;
        Assert.Equal(MetricCoverage.Partial, bioValues[MetricEntry.Scalar].Coverage);
        Assert.Equal(3L, bioValues[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, bioValues["medic"].IntegerValue);
        Assert.Equal(1L, bioValues["bioregeneration"].IntegerValue);
        Assert.Equal(1L, bioValues[MetricKeys.UnknownBucket].IntegerValue);

        Assert.Equal(1L, obs.Metrics[RoyaltyFamily.PermitUses].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, obs.Metrics[RoyaltyFamily.PermitUsesByDef].Values["ludeon.rimworld.royalty/CallMilitaryAidSmall"].IntegerValue);
        Assert.Equal(MetricCoverage.Complete, obs.Metrics[RoyaltyFamily.PermitUsesByDef].Values[MetricEntry.Scalar].Coverage);

        Assert.Equal(2L, obs.Metrics[RoyaltyFamily.Psycasts].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, obs.Metrics[RoyaltyFamily.PsycastsByDef].Values["ludeon.rimworld.royalty/Skip"].IntegerValue);

        Assert.Equal(1L, obs.Metrics[RoyaltyLinkingFamily.AnimaTreeLinks].Values[MetricEntry.Scalar].IntegerValue);

        // Legacy checkpoint mismatch: permit scalar recorded without def breakdown weakens PermitUsesByDef to Partial.
        var legacyAcc = LiveMetricAccumulator.StartNew([RoyaltyFamily.Family.Spec], 0);
        var legacyRoy = legacyAcc.Family(RoyaltyFamily.Name)!;
        RoyaltyFamily.PermitUsed(legacyRoy, playerOwned: true, confirmed: true, permitDefKey: null);
        var legacyDraft = legacyAcc.PrepareSave(10_000, T0, "cp-roy-legacy")!;
        var legacyObs = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(legacyDraft.Json).Payload!,
            legacyDraft.Json,
            new ColonyIdentity { SeedString = "legacy-roy", StartingTile = "6,0", FirstWealthBits = 1 },
            "1.7.5011 rev24",
            [AcquisitionUnits.Royalty]);
        Assert.Equal(1L, legacyObs.Metrics[RoyaltyFamily.PermitUses].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, legacyObs.Metrics[RoyaltyFamily.PermitUsesByDef].Values[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void RaidAnimalsAndRosterMechs_TrackSpeciesAndFactionBreakdownsSafely()
    {
        var acc = LiveMetricAccumulator.StartNew(
        [
            RaidFamily.Family.Spec,
            AnimalsFamily.Family.Spec,
        ], 0);

        var raid = acc.Family(RaidFamily.Name)!;
        RaidFamily.Arrived(raid, new RaidArrival
        {
            ArrivalMode = "EdgeWalkIn",
            Strategy = "ImmediateAttack",
            FactionDef = "ludeon.rimworld/Pirate",
            Points = 400f,
            MapId = "map-1",
            PawnIds = ["r1", "r2"],
        }, 1000);
        RaidFamily.MemberLost(raid, "r1", RaidLoss.Killed, 1500);
        RaidFamily.MemberLost(raid, "r2", RaidLoss.Exited, 1800); // retreated

        RaidFamily.Arrived(raid, new RaidArrival
        {
            ArrivalMode = "EdgeWalkIn",
            Strategy = "ImmediateAttack",
            FactionDef = "ludeon.rimworld/Mechanoid",
            Points = 600f,
            MapId = "map-1",
            PawnIds = ["m1", "m2"],
        }, 2000);
        RaidFamily.MemberLost(raid, "m1", RaidLoss.Killed, 2300);
        RaidFamily.MemberLost(raid, "m2", RaidLoss.Killed, 2400); // wiped

        var anim = acc.Family(AnimalsFamily.Name)!;
        AnimalsFamily.Tamed(anim, wasWild: true, nowPlayer: true, "ludeon.rimworld/Muffalo");
        AnimalsFamily.Born(anim, playerAnimal: true, "ludeon.rimworld/Muffalo");
        AnimalsFamily.Died(anim, new DeathFacts { PawnId = "a1", VictimKind = DeathFacts.Other, ColonyAnimal = true, SlaughterDesignated = true, ExecutionCut = true, AnimalSpecies = "ludeon.rimworld/Muffalo" });
        AnimalsFamily.Died(anim, new DeathFacts { PawnId = "a2", VictimKind = DeathFacts.Other, Wild = true, HuntDesignated = true, Killer = KillerSide.PlayerPawn, AnimalSpecies = "ludeon.rimworld/Elk" });

        var draft = acc.PrepareSave(10_000, T0, "cp-van-1")!;
        var obs = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(draft.Json).Payload!,
            draft.Json,
            new ColonyIdentity { SeedString = "van", StartingTile = "8,0", FirstWealthBits = 1 },
            "1.7.5011 rev24",
            null);

        Assert.Equal(1L, obs.Metrics[RaidFamily.RetreatedByFaction].Values["ludeon.rimworld/Pirate"].IntegerValue);
        Assert.Equal(1L, obs.Metrics[RaidFamily.WipedByFaction].Values["ludeon.rimworld/Mechanoid"].IntegerValue);
        Assert.Equal(MetricCoverage.Complete, obs.Metrics[RaidFamily.RetreatedByFaction].Values[MetricEntry.Scalar].Coverage);

        Assert.Equal(1L, obs.Metrics[AnimalsFamily.TamesBySpecies].Values["ludeon.rimworld/Muffalo"].IntegerValue);
        Assert.Equal(1L, obs.Metrics[AnimalsFamily.BirthsBySpecies].Values["ludeon.rimworld/Muffalo"].IntegerValue);
        Assert.Equal(1L, obs.Metrics[AnimalsFamily.SlaughteredBySpecies].Values["ludeon.rimworld/Muffalo"].IntegerValue);
        Assert.Equal(1L, obs.Metrics[AnimalsFamily.HuntedBySpecies].Values["ludeon.rimworld/Elk"].IntegerValue);

        // Legacy mismatch check for AnimalsFamily species breakdown
        var legacyAnimAcc = LiveMetricAccumulator.StartNew([AnimalsFamily.Family.Spec], 0);
        AnimalsFamily.Tamed(legacyAnimAcc.Family(AnimalsFamily.Name)!, wasWild: true, nowPlayer: true, species: null);
        var legacyAnimDraft = legacyAnimAcc.PrepareSave(5_000, T0, "cp-anim-leg")!;
        var legacyAnimObs = CheckpointMetrics.ToObservation(
            CheckpointJson.Parse(legacyAnimDraft.Json).Payload!,
            legacyAnimDraft.Json,
            new ColonyIdentity { SeedString = "anim-leg", StartingTile = "9,0", FirstWealthBits = 1 },
            "1.7.5011 rev24",
            null);
        Assert.Equal(MetricCoverage.Partial, legacyAnimObs.Metrics[AnimalsFamily.TamesBySpecies].Values[MetricEntry.Scalar].Coverage);

        // Roster mech species peak + CrossColonyCatalog ThingDef breakdown routing
        var roster = RosterSnapshot.Build(new[]
        {
            new RosterPawnFact { Id = "mech-1", Kind = RosterKind.ColonyMech, Species = "ludeon.rimworld.biotech/Mech_Militor" },
            new RosterPawnFact { Id = "mech-2", Kind = RosterKind.ColonyMech, Species = "ludeon.rimworld.biotech/Mech_Militor" },
            new RosterPawnFact { Id = "other-3", Kind = RosterKind.Other, Species = "ludeon.rimworld.biotech/Mech_Constructoid" },
        });
        Assert.Equal(2L, roster[RosterSnapshot.Mechs].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(2L, roster[RosterSnapshot.MechSpeciesPeak].Values["ludeon.rimworld.biotech/Mech_Militor"].IntegerValue);
        Assert.False(roster[RosterSnapshot.MechSpeciesPeak].Values.ContainsKey("ludeon.rimworld.biotech/Mech_Constructoid"));

        Assert.True(CrossColonyCatalog.UsesThingDefBreakdown(MetricRegistry.Find(RosterSnapshot.MechSpeciesPeak)!));
        Assert.True(CrossColonyCatalog.UsesThingDefBreakdown(MetricRegistry.Find(AnimalsFamily.TamesBySpecies)!));
        Assert.True(CrossColonyCatalog.UsesThingDefBreakdown(MetricRegistry.Find(MembershipFamily.HumanBirthsBySpecies)!));

        // Lifetime cumulative registration for every new Axis 1 live event metric
        foreach (var liveKey in new[]
        {
            RaidFamily.RetreatedByFaction, RaidFamily.WipedByFaction,
            AnimalsFamily.TamesBySpecies, AnimalsFamily.BirthsBySpecies, AnimalsFamily.SlaughteredBySpecies, AnimalsFamily.HuntedBySpecies,
            RoyaltyFamily.PermitUsesByDef, RoyaltyFamily.PsycastsByDef, RoyaltyLinkingFamily.AnimaTreeLinks,
            IdeologyExpansionFamily.PrisonersEnslaved, IdeologyExpansionFamily.BiosculpterCompletions,
        })
        {
            var def = MetricRegistry.Find(liveKey);
            Assert.NotNull(def);
            Assert.True(MetricDisplayPolicy.IsLifetimeCumulative(def!), liveKey);
        }
    }

    [Fact]
    public void SaveReader_ReadsWorldEnvironmentInfo_HandlingOmittedDefaultsAndBiotechOff()
    {
        static SaveReadResult ReadWorldSave(bool biotechActive, params XElement[] extraWorldInfo)
        {
            var mods = biotechActive
                ? new[] { "ludeon.rimworld", AcquisitionUnits.Biotech }
                : new[] { "ludeon.rimworld" };
            var root = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
                .MetaMods("1.7.5011 rev24", mods)
                .Tick(60_000, start: 3_600_000)
                .Build()));
            var info = root.Root!.Element("game")!.Element("world")!.Element("info")!;
            foreach (var el in extraWorldInfo)
                info.Add(el);
            var bytes = Encoding.UTF8.GetBytes(root.ToString());
            return new SaveReader().ReadDetailed(new MemoryStream(bytes), "world.rws", bytes.Length, T0);
        }

        // 1. Explicit world environment settings with Biotech active
        var explicitRead = ReadWorldSave(
            biotechActive: true,
            new XElement("overallTemperature", "Hot"),
            new XElement("overallRainfall", "High"),
            new XElement("pollution", "0.35"),
            new XElement("initialMapSize", "(275, 0, 275)"));
        var explicitMetrics = explicitRead.Metrics!.Metrics;
        Assert.Equal(MetricCoverage.SnapshotOnly, explicitMetrics[ColonyStartSnapshot.WorldTemperature].Values["Hot"].Coverage);
        Assert.Equal(MetricCoverage.SnapshotOnly, explicitMetrics[ColonyStartSnapshot.WorldRainfall].Values["High"].Coverage);
        Assert.Equal(0.35, explicitMetrics[ColonyStartSnapshot.WorldPollution].Values[MetricEntry.Scalar].RealValue!.Value, 4);
        Assert.Equal(75625L, explicitMetrics[ColonyStartSnapshot.InitialMapSize].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(75625L, explicitMetrics[ColonyStartSnapshot.InitialMapSize].Values["275x275"].IntegerValue);

        // 2. Omitted Scribe defaults (VeryCold / AlmostNone / 0f) when initialMapSize confirms complete WorldInfo
        var omittedRead = ReadWorldSave(
            biotechActive: true,
            new XElement("initialMapSize", "(250, 0, 250)"));
        var omittedMetrics = omittedRead.Metrics!.Metrics;
        Assert.Equal(MetricCoverage.SnapshotOnly, omittedMetrics[ColonyStartSnapshot.WorldTemperature].Values["VeryCold"].Coverage);
        Assert.Equal(MetricCoverage.SnapshotOnly, omittedMetrics[ColonyStartSnapshot.WorldRainfall].Values["AlmostNone"].Coverage);
        Assert.Equal(MetricCoverage.SnapshotOnly, omittedMetrics[ColonyStartSnapshot.WorldPollution].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(0.0, omittedMetrics[ColonyStartSnapshot.WorldPollution].Values[MetricEntry.Scalar].RealValue!.Value, 5);

        // 3. Biotech inactive => WorldPollution is NotApplicable even if pollution element was written
        var noBiotechRead = ReadWorldSave(
            biotechActive: false,
            new XElement("overallTemperature", "Normal"),
            new XElement("overallRainfall", "Normal"),
            new XElement("pollution", "0.25"),
            new XElement("initialMapSize", "(250, 0, 250)"));
        Assert.Equal(MetricCoverage.NotApplicable, noBiotechRead.Metrics!.Metrics[ColonyStartSnapshot.WorldPollution].Values[MetricEntry.Scalar].Coverage);
        Assert.Null(noBiotechRead.Metrics!.Metrics[ColonyStartSnapshot.WorldPollution].Values[MetricEntry.Scalar].RealValue);

        // 4. Minimal XML without initialMapSize never fabricates VeryCold/AlmostNone defaults (remains unemitted / Unknown)
        var minimalRead = ReadWorldSave(biotechActive: true);
        Assert.False(minimalRead.Metrics!.Metrics.ContainsKey(ColonyStartSnapshot.WorldTemperature));
        Assert.False(minimalRead.Metrics!.Metrics.ContainsKey(ColonyStartSnapshot.WorldRainfall));
        Assert.False(minimalRead.Metrics!.Metrics.ContainsKey(ColonyStartSnapshot.InitialMapSize));
        var unknownSnapshot = ColonyStartSnapshot.BuildWorldInfo(null, null, null, null, null, biotechActive: true);
        Assert.Equal(MetricCoverage.Unknown, unknownSnapshot[ColonyStartSnapshot.WorldTemperature].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(MetricCoverage.Unknown, unknownSnapshot[ColonyStartSnapshot.WorldRainfall].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(MetricCoverage.Unknown, unknownSnapshot[ColonyStartSnapshot.InitialMapSize].Values[MetricEntry.Scalar].Coverage);
    }

    [Fact]
    public void SaveReader_AndColonyChronicle_HandlePermanentTalesAuxiliaryDisplayAndBranchIsolation()
    {
        const int StartAbs = 3_600_000;

        static (SaveObservation Obs, MetricObservation Metrics) ReadBranchSave(
            string fileName,
            int ticksGame,
            DateTime lastWriteUtc,
            string? checkpointId,
            string? parentId,
            params (string Def, int Id, int DateAbs, bool Hidden)[] tales)
        {
            var root = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
                .MetaMods("1.7.5011 rev24", ["ludeon.rimworld"])
                .Tick(ticksGame, start: StartAbs)
                .Build()));
            var game = root.Root!.Element("game")!;
            var comps = game.Element("components") ?? new XElement("components");
            if (comps.Parent is null)
                game.Add(comps);
            comps.Add(new XElement("li", new XAttribute("Class", "RimworldStatistics.GameComponent_RimworldStatistics"),
                new XElement("campaignId", "camp-tales-1")));

            game.Add(new XElement("taleManager",
                new XElement("tales",
                    tales.Select(t => new XElement("li",
                        new XElement("def", t.Def),
                        new XElement("id", t.Id),
                        new XElement("date", t.DateAbs),
                        new XElement("hidden", t.Hidden ? "True" : "False"))))));

            var bytes = Encoding.UTF8.GetBytes(root.ToString());
            var res = new SaveReader().ReadDetailed(new MemoryStream(bytes), fileName, bytes.Length, lastWriteUtc);
            Assert.Equal(ObservationStatus.Ok, res.Observation.Status);
            return (res.Observation, res.Metrics!);
        }

        // Branch root at tick 60_000 with TileSettled
        var rootSave = ReadBranchSave("root.rws", 60_000, T0, "cp-root", null,
            ("TileSettled", 1, StartAbs + 0, false),
            ("MajorThreat", 2, StartAbs + 30_000, true),       // hidden -> ignored
            ("RawMaterial", 3, StartAbs + 15_000, false),      // unrelated def -> ignored
            ("Marriage", 4, StartAbs + 120_000, false));       // future relative to rootSave tick -> ignored

        Assert.NotNull(rootSave.Obs.PermanentTales);
        var onlyRootTale = Assert.Single(rootSave.Obs.PermanentTales!);
        Assert.Equal(new PermanentTaleFact(PermanentTaleKind.TileSettled, 1, 0), onlyRootTale);

        // Branch A (later save tick 240_000, becomes default representative): has Marriage id=10 at tick 180_000
        var branchASave = ReadBranchSave("branchA.rws", 240_000, T0.AddHours(2), "cp-A", "cp-root",
            ("TileSettled", 1, StartAbs + 0, false),
            ("Marriage", 10, StartAbs + 180_000, false));

        // Branch B (earlier save tick 180_000): has MajorThreat id=20 at tick 120_000 (different timeline!)
        var branchBSave = ReadBranchSave("branchB.rws", 180_000, T0.AddHours(1), "cp-B", "cp-root",
            ("TileSettled", 1, StartAbs + 0, false),
            ("MajorThreat", 20, StartAbs + 120_000, false));

        static MetricObservation MakeCp(string key, string? parentKey, SaveObservation save)
        {
            var cp = new MetricObservation
            {
                Source = MetricObservationSource.Checkpoint,
                Key = key,
                ParentKey = parentKey,
                PayloadHash = "h-" + key,
                Identity = save.Identity.Clone(),
                FormatVersion = SaveReader.Version,
                SaveTick = save.Values.TicksGame,
                ObservedUtc = save.LastWriteUtc,
                GameVersion = "1.7.5011 rev24",
            };
            cp.FileAliases.Add(new MetricFileAlias { FileName = save.FileName, FileSize = save.FileSize, LastWriteUtc = save.LastWriteUtc });
            return cp;
        }

        var cpRoot = MakeCp("cp-root", null, rootSave.Obs);
        var cpA = MakeCp("cp-A", "cp-root", branchASave.Obs);
        var cpB = MakeCp("cp-B", "cp-root", branchBSave.Obs);

        var profile = new Profile
        {
            SaveObservations = { rootSave.Obs, branchASave.Obs, branchBSave.Obs },
            MetricObservations = { rootSave.Metrics, branchASave.Metrics, branchBSave.Metrics, cpRoot, cpA, cpB },
        };

        // Dominates must never allow a save lacking earlier PermanentTales to prune the earlier observation,
        // and AddSaveObservation retains superseded observations whose tales are not covered.
        var newerNoTales = new SaveObservation
        {
            FileName = branchASave.Obs.FileName,
            FileSize = branchASave.Obs.FileSize + 1,
            LastWriteUtc = branchASave.Obs.LastWriteUtc.AddMinutes(1),
            ReaderVersion = SaveReader.Version,
            ContentHash = "other-hash",
            GameVersion = branchASave.Obs.GameVersion,
            Identity = branchASave.Obs.Identity.Clone(),
            Values = new ObservedValues { TicksGame = branchASave.Obs.Values.TicksGame + 100 },
            PermanentTales = null,
        };
        Assert.False(ProfileMutations.Dominates(newerNoTales, branchASave.Obs));
        Assert.True(ProfileMutations.Dominates(branchASave.Obs, branchASave.Obs));

        var reReadProfile = new Profile();
        ProfileMutations.AddSaveObservation(reReadProfile, branchASave.Obs);
        ProfileMutations.AddSaveObservation(reReadProfile, newerNoTales);
        Assert.Equal(2, reReadProfile.SaveObservations.Count);
        Assert.Contains(reReadProfile.SaveObservations, o => o.Superseded && o.PermanentTales is { Count: 2 });

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

        // Numeric SocialFamily.Marriages must NOT be fabricated from PermanentTales.
        Assert.Equal(MetricCoverage.Unknown, view.Numeric.Overall.Single(r => r.Definition.Key == SocialFamily.Marriages).Value.Coverage);

        // Default representative branch (cp-A) shows TileSettled + Marriage (id 10), must NOT leak Branch B's MajorThreat (id 20).
        var chronA = ColonyChronicle.Build(view, colony.Key, lineage)!;
        var aTales = chronA.Entries.Where(e => e.Kind == ChronicleEntryKind.HistoricalTale).ToArray();
        Assert.Equal(2, aTales.Length);
        Assert.Contains(aTales, t => t.TaleKind == PermanentTaleKind.TileSettled && t.TaleId == 1);
        Assert.Contains(aTales, t => t.TaleKind == PermanentTaleKind.Marriage && t.TaleId == 10 && t.StartTick == 180_000);
        Assert.DoesNotContain(aTales, t => t.TaleKind == PermanentTaleKind.MajorThreat);
        Assert.All(aTales, t => Assert.Null(t.RelatedMetricKey));

        // Filter counts and Tales mode isolation
        var filterItems = chronA.Entries.Select(e => new ChronicleFilterableEntry<ChronicleEntry>(e, e.TaleKind?.ToString() ?? "", [], e)).ToArray();
        var filteredTales = ColonyChronicleFilter.Apply(
            filterItems,
            Array.Empty<ChronicleFilterableUndatedTrace<ChronicleUndatedTrace>>(),
            ChronicleFilterQuery.Create(ChronicleKindFilter.Tales, null, null, null));
        Assert.Equal(2, filteredTales.MatchingTalesCount);
        Assert.Equal(2, filteredTales.Rows.Length);
        Assert.All(filteredTales.Rows, r => Assert.Equal(ChronicleEntryKind.HistoricalTale, r.Kind));

        // Selecting Branch B (cp-B) shows TileSettled + MajorThreat (id 20) and must NOT leak Branch A's Marriage (id 10).
        var branchBKey = ChronicleBranchKey.Of(lineage.Nodes.Single(n => n.Kind == SaveLineageNodeKind.Checkpoint && n.CheckpointId == "cp-B"));
        var chronB = ColonyChronicle.Build(view, colony.Key, lineage, branchBKey)!;
        var bTales = chronB.Entries.Where(e => e.Kind == ChronicleEntryKind.HistoricalTale).ToArray();
        Assert.Equal(2, bTales.Length);
        Assert.Contains(bTales, t => t.TaleKind == PermanentTaleKind.TileSettled && t.TaleId == 1);
        Assert.Contains(bTales, t => t.TaleKind == PermanentTaleKind.MajorThreat && t.TaleId == 20 && t.StartTick == 120_000);
        Assert.DoesNotContain(bTales, t => t.TaleKind == PermanentTaleKind.Marriage);
        Assert.Null(chronA.TalesMissingThroughTick);
        Assert.Null(chronB.TalesMissingThroughTick);
    }

    [Fact]
    public void PermanentTalesOverTheCap_KeepTheNewest_AndTheChronicleSaysWhatNoEarlierSaveHolds()
    {
        const int StartAbs = 3_600_000;

        static (SaveObservation Obs, MetricObservation Metrics) ReadSave(string fileName, int ticksGame, DateTime lastWriteUtc,
            int threats)
        {
            var root = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
                .MetaMods("1.7.5011 rev24", ["ludeon.rimworld"])
                .Tick(ticksGame, start: StartAbs)
                .Build()));
            var game = root.Root!.Element("game")!;
            var comps = game.Element("components") ?? new XElement("components");
            if (comps.Parent is null)
                game.Add(comps);
            comps.Add(new XElement("li", new XAttribute("Class", "RimworldStatistics.GameComponent_RimworldStatistics"),
                new XElement("campaignId", "camp-tale-cap")));
            // Saved oldest first, as TaleManager.Add appends: the founding, then one major threat every 1,000 ticks.
            var tales = new List<XElement>
            {
                new("li", new XElement("def", "TileSettled"), new XElement("id", 1_000), new XElement("date", StartAbs)),
            };
            for (var i = 1; i <= threats; i++)
                tales.Add(new XElement("li", new XElement("def", "MajorThreat"), new XElement("id", i),
                    new XElement("date", StartAbs + i * 1_000)));
            game.Add(new XElement("taleManager", new XElement("tales", tales)));

            var bytes = Encoding.UTF8.GetBytes(root.ToString());
            var res = new SaveReader().ReadDetailed(new MemoryStream(bytes), fileName, bytes.Length, lastWriteUtc);
            Assert.Equal(ObservationStatus.Ok, res.Observation.Status);
            return (res.Observation, res.Metrics!);
        }

        // 301 tales (the founding and 300 threats): the newest 256 are kept, threats 45 to 300.
        var late = ReadSave("late.rws", 400_000, T0.AddHours(2), threats: 300);
        Assert.Equal(SaveReader.MaxPermanentTales, late.Obs.PermanentTales!.Count);
        Assert.Equal(new PermanentTaleFact(PermanentTaleKind.MajorThreat, 45, 45_000), late.Obs.PermanentTales[0]);
        Assert.Equal(new PermanentTaleFact(PermanentTaleKind.MajorThreat, 300, 300_000), late.Obs.PermanentTales[^1]);
        Assert.Equal(44_000, late.Obs.PermanentTalesDroppedThroughTick);

        // Exactly the cap: nothing is left out.
        var exact = ReadSave("exact.rws", 400_000, T0.AddHours(3), threats: SaveReader.MaxPermanentTales - 1);
        Assert.Equal(SaveReader.MaxPermanentTales, exact.Obs.PermanentTales!.Count);
        Assert.Null(exact.Obs.PermanentTalesDroppedThroughTick);

        // The field is written only when set, and read back.
        var json = System.Text.Json.JsonSerializer.Serialize(late.Obs);
        Assert.Contains("\"PermanentTalesDroppedThroughTick\":44000", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PermanentTalesDroppedThroughTick", System.Text.Json.JsonSerializer.Serialize(exact.Obs), StringComparison.Ordinal);
        Assert.Equal(44_000, System.Text.Json.JsonSerializer.Deserialize<SaveObservation>(json)!.PermanentTalesDroppedThroughTick);

        static ColonyChronicle Chronicle(params (SaveObservation Obs, MetricObservation Metrics, string Cp, string? Parent)[] saves)
        {
            var profile = new Profile();
            foreach (var (obs, metrics, cpKey, parent) in saves)
            {
                var cp = new MetricObservation
                {
                    Source = MetricObservationSource.Checkpoint, Key = cpKey, ParentKey = parent, PayloadHash = "h-" + cpKey,
                    Identity = obs.Identity.Clone(), FormatVersion = SaveReader.Version, SaveTick = obs.Values.TicksGame,
                    ObservedUtc = obs.LastWriteUtc, GameVersion = "1.7.5011 rev24",
                };
                cp.FileAliases.Add(new MetricFileAlias { FileName = obs.FileName, FileSize = obs.FileSize, LastWriteUtc = obs.LastWriteUtc });
                profile.SaveObservations.Add(obs);
                profile.MetricObservations.Add(metrics);
                profile.MetricObservations.Add(cp);
            }
            var colonies = CampaignMatcher.BuildColonies(profile);
            var colony = Assert.Single(colonies);
            var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
            var view = new StatsView
            {
                Colonies = colonies, Stats = stats,
                Numeric = MetricCalculator.BuildView(profile, colonies, stats,
                    MetricRegistry.Compose(profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits)),
            };
            return ColonyChronicle.Build(view, colony.Key, ColonySaveLineage.Build(profile, colonies, colony.Key))!;
        }

        static ChronicleEntry[] Tales(ColonyChronicle c) => c.Entries.Where(e => e.Kind == ChronicleEntryKind.HistoricalTale).ToArray();

        // Only the late save: what it left out is said to be missing.
        var alone = Chronicle((late.Obs, late.Metrics, "cp-late", null));
        Assert.Equal(SaveReader.MaxPermanentTales, Tales(alone).Length);
        Assert.Equal(44_000, alone.TalesMissingThroughTick);

        // An earlier save on the same branch, read when the colony had 140 threats, holds the ones the late save left out.
        var early = ReadSave("early.rws", 150_000, T0.AddHours(1), threats: 140);
        Assert.Null(early.Obs.PermanentTalesDroppedThroughTick);
        var both = Chronicle((early.Obs, early.Metrics, "cp-early", null), (late.Obs, late.Metrics, "cp-late", "cp-early"));
        var bothTales = Tales(both);
        Assert.Equal(301, bothTales.Length);
        Assert.Contains(bothTales, t => t.TaleKind == PermanentTaleKind.TileSettled && t.StartTick == 0);
        Assert.Null(both.TalesMissingThroughTick);
    }

    [Fact]
    public void MissingTales_AreThoseNoSaveFarEnoughOnHolds()
    {
        static ColonyChronicle.SaveFact Save(int tick, int? droppedThrough, bool tales = true)
            => new($"s{tick}.rws", 1, T0, tick, "camp", [], [], [],
                tales ? [new PermanentTaleFact(PermanentTaleKind.MajorThreat, tick, tick)] : null, droppedThrough);

        Assert.Null(ColonyChronicle.MissingTalesThroughTick([]));
        Assert.Null(ColonyChronicle.MissingTalesThroughTick([Save(400_000, null), Save(100_000, 50_000)]));   // the latest holds all
        Assert.Equal(44_000, ColonyChronicle.MissingTalesThroughTick([Save(400_000, 44_000)]));
        // An earlier save that was already past the gap fills it, down to what it left out itself.
        Assert.Null(ColonyChronicle.MissingTalesThroughTick([Save(400_000, 44_000), Save(44_000, null)]));
        Assert.Equal(10_000, ColonyChronicle.MissingTalesThroughTick([Save(400_000, 44_000), Save(150_000, 10_000)]));
        Assert.Null(ColonyChronicle.MissingTalesThroughTick([Save(400_000, 44_000), Save(150_000, 10_000), Save(12_000, null)]));
        // One that was not yet that far on cannot: tales after it and up to the gap are in no save.
        Assert.Equal(44_000, ColonyChronicle.MissingTalesThroughTick([Save(400_000, 44_000), Save(30_000, null)]));
        // A save whose tales were not read proves nothing either way.
        Assert.Equal(44_000, ColonyChronicle.MissingTalesThroughTick([Save(400_000, 44_000), Save(200_000, null, tales: false)]));
        Assert.Null(ColonyChronicle.MissingTalesThroughTick(
            [Save(400_000, 44_000), Save(200_000, null, tales: false), Save(100_000, null)]));
    }
}
