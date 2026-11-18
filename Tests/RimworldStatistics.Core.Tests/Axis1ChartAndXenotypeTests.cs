using System.Text;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class Axis1ChartAndXenotypeTests
{
    private static readonly DateTime Stamp = new(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThreatPointsAndAdaptation_AreRecoveredFromDebugAutoRecorderGroupWithProperScaleAndCadence()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        // ThreatPoints are stored as DefaultThreatPointsNow / 10f every 30,000 ticks (0.5d).
        // Adaptation is stored as AdaptDays (signed!) every 60,000 ticks (1.0d).
        var rawXml = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
            .Tick(ticksGame: 120001, start: 0)
            .WealthTotalDeflate(1000f, 1100f, 1200f, 1300f, 1400f)
            .Build()));
        var groups = rawXml.Root!.Element("game")!.Element("history")!.Element("autoRecorderGroups")!;
        var debugRecorders = new XElement("recorders",
            new XElement("li",
                new XElement("def", HistoryMetricReader.ThreatPoints),
                new XElement("recordsDeflate", Convert.ToBase64String(SaveXmlBuilder.Deflate(
                    SaveXmlBuilder.FloatBytes(35f, 80f, 125.5f, 90f, 150f))))),
            new XElement("li",
                new XElement("def", HistoryMetricReader.Adaptation),
                new XElement("recordsDeflate", Convert.ToBase64String(SaveXmlBuilder.Deflate(
                    SaveXmlBuilder.FloatBytes(0f, -2.5f, 4.0f))))));
        groups.Add(new XElement("li", new XElement("def", "Debug"), debugRecorders));

        var path = saves.File("debug_recorders.rws");
        File.WriteAllBytes(path, Encoding.UTF8.GetBytes(rawXml.ToString()));

        var read = new SaveReader().ReadDetailed(path);
        Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
        var metrics = read.Metrics!.Metrics;

        // ThreatPoints rescaled ×10.0: [350, 800, 1255, 900, 1500]
        Assert.Equal(1500.0, metrics[MetricKeys.ThreatPointsPeak].Values[MetricEntry.Scalar].RealValue!.Value, 5);
        Assert.Equal(1500.0, metrics[MetricKeys.ThreatPointsLatest].Values[MetricEntry.Scalar].RealValue!.Value, 5);
        Assert.Equal((350.0 + 800.0 + 1255.0 + 900.0 + 1500.0) / 5.0,
            metrics[MetricKeys.ThreatPointsMean].Values[MetricEntry.Scalar].RealValue!.Value, 5);

        // Adaptation signed days: [0.0, -2.5, 4.0]
        Assert.Equal(4.0, metrics[MetricKeys.AdaptationLatest].Values[MetricEntry.Scalar].RealValue!.Value, 5);
        Assert.Equal(4.0, metrics[MetricKeys.AdaptationPeak].Values[MetricEntry.Scalar].RealValue!.Value, 5);
        Assert.Equal(-2.5, metrics[MetricKeys.AdaptationMin].Values[MetricEntry.Scalar].RealValue!.Value, 5);
        Assert.Equal((0.0 - 2.5 + 4.0) / 3.0,
            metrics[MetricKeys.AdaptationMean].Values[MetricEntry.Scalar].RealValue!.Value, 5);
        Assert.True(MetricRegistry.Find(MetricKeys.AdaptationLatest)!.Signed);
        Assert.True(MetricRegistry.Find(MetricKeys.AdaptationMin)!.Signed);

        // Per-colony chart history & RecorderChartCache round-trip
        var cache = new RecorderChartCache(maxEntries: 16, maxPoints: 1000);
        var session = new ProfileSession(new ProfileStore(store.Path), () => Stamp, recorderCharts: cache);
        session.Mutate(p =>
        {
            p.SaveObservations.Add(read.Observation);
            p.MetricObservations.Add(read.Metrics!);
        }, SaveMode.None);
        var view = session.GetView(new StatsOptions());
        var colonyKey = Assert.Single(view.Colonies).Key;

        var threatChart = session.ReadChartHistory(view, colonyKey, MetricRegistry.Find(MetricKeys.ThreatPointsLatest)!, saves.Path);
        Assert.Equal(MetricHistoryKind.GameRecorder, threatChart.Kind);
        Assert.Equal(new[] { 0, 30000, 60000, 90000, 120000 }, threatChart.Points.Select(p => p.Tick).ToArray());
        Assert.Equal(new[] { 350.0, 800.0, 1255.0, 900.0, 1500.0 },
            threatChart.Points.Select(p => p.Value!.Number!.Value).ToArray());

        var adaptChart = session.ReadChartHistory(view, colonyKey, MetricRegistry.Find(MetricKeys.AdaptationLatest)!, saves.Path);
        Assert.Equal(MetricHistoryKind.GameRecorder, adaptChart.Kind);
        Assert.Equal(new[] { 0, 60000, 120000 }, adaptChart.Points.Select(p => p.Tick).ToArray());
        Assert.Equal(new[] { 0.0, -2.5, 4.0 },
            adaptChart.Points.Select(p => p.Value!.Number!.Value).ToArray());

        // Cached second read does not re-parse
        var parsesBefore = cache.Parses;
        var threatChartAgain = session.ReadChartHistory(view, colonyKey, MetricRegistry.Find(MetricKeys.ThreatPointsMean)!, saves.Path);
        Assert.Equal(parsesBefore, cache.Parses);
        Assert.Equal(threatChart.Points.Count, threatChartAgain.Points.Count);
    }

    [Fact]
    public void XenotypeRules_MapsCustomAndHybridToReservedBucketWithoutLeakingNames()
    {
        Assert.Null(XenotypeRules.BreakdownKey(
            biotechActive: false, hasGeneTracker: true, xenotypeName: "SecretName", hybrid: true,
            packageId: AcquisitionUnits.Biotech, defName: "Baseliner"));
        Assert.Null(XenotypeRules.BreakdownKey(
            biotechActive: true, hasGeneTracker: false, xenotypeName: null, hybrid: false,
            packageId: AcquisitionUnits.Biotech, defName: "Baseliner"));

        Assert.Equal(MetricKeys.CustomXenotypeBucket, XenotypeRules.BreakdownKey(
            biotechActive: true, hasGeneTracker: true, xenotypeName: "PlayerCustomXeno", hybrid: false,
            packageId: AcquisitionUnits.Biotech, defName: "Baseliner"));
        Assert.Equal(MetricKeys.CustomXenotypeBucket, XenotypeRules.BreakdownKey(
            biotechActive: true, hasGeneTracker: true, xenotypeName: null, hybrid: true,
            packageId: AcquisitionUnits.Biotech, defName: "Baseliner"));
        Assert.Equal("ludeon.rimworld.biotech/Hussar", XenotypeRules.BreakdownKey(
            biotechActive: true, hasGeneTracker: true, xenotypeName: "   ", hybrid: false,
            packageId: AcquisitionUnits.Biotech, defName: "Hussar"));

        Assert.Equal("RWS_Details_CustomXenotype",
            MetricDisplayPolicy.BreakdownLabelKey(MetricRegistry.Find(RosterSnapshot.FreeXenotypePeak)!, MetricKeys.CustomXenotypeBucket));
    }

    [Fact]
    public void RosterSnapshot_EmitsXenotypePeaksOnlyWhenBiotechActiveAndSupportsCustomBucketWithoutPartialDemotion()
    {
        var noBiotech = RosterSnapshot.Build(new[]
        {
            new RosterPawnFact
            {
                Id = "Human1", Kind = RosterKind.FreeColonist,
                Species = "ludeon.rimworld/Human", AgeYears = 25,
            },
        }, biotechActive: false);
        Assert.DoesNotContain(RosterSnapshot.FreeXenotypePeak, noBiotech.Keys);
        Assert.DoesNotContain(RosterSnapshot.PrisonerXenotypePeak, noBiotech.Keys);
        Assert.DoesNotContain(RosterSnapshot.SlaveXenotypePeak, noBiotech.Keys);

        var withBiotech = RosterSnapshot.Build(new[]
        {
            new RosterPawnFact
            {
                Id = "Human1", Kind = RosterKind.FreeColonist,
                Species = "ludeon.rimworld/Human", Xenotype = "ludeon.rimworld.biotech/Dirtmole", AgeYears = 25,
            },
            new RosterPawnFact
            {
                Id = "Human2", Kind = RosterKind.FreeColonist,
                Species = "ludeon.rimworld/Human", Xenotype = MetricKeys.CustomXenotypeBucket, AgeYears = 30,
            },
            new RosterPawnFact
            {
                Id = "Human3", Kind = RosterKind.Prisoner,
                Species = "ludeon.rimworld/Human", Xenotype = MetricKeys.CustomXenotypeBucket,
            },
            new RosterPawnFact
            {
                Id = "Human4", Kind = RosterKind.Slave,
                Species = "ludeon.rimworld/Human", Xenotype = "ludeon.rimworld.biotech/Genie",
            },
        }, biotechActive: true);

        var free = withBiotech[RosterSnapshot.FreeXenotypePeak].Values;
        Assert.Equal(MetricCoverage.SnapshotOnly, free[MetricEntry.Scalar].Coverage);
        Assert.Equal(2L, free[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, free["ludeon.rimworld.biotech/Dirtmole"].IntegerValue);
        Assert.Equal(1L, free[MetricKeys.CustomXenotypeBucket].IntegerValue);

        var prisoners = withBiotech[RosterSnapshot.PrisonerXenotypePeak].Values;
        Assert.Equal(MetricCoverage.SnapshotOnly, prisoners[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, prisoners[MetricKeys.CustomXenotypeBucket].IntegerValue);

        var slaves = withBiotech[RosterSnapshot.SlaveXenotypePeak].Values;
        Assert.Equal(MetricCoverage.SnapshotOnly, slaves[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, slaves["ludeon.rimworld.biotech/Genie"].IntegerValue);
    }

    [Fact]
    public void HistoricalHomeMapReader_SalvagesBiotechXenotypePeaksWithoutStoringCustomNames()
    {
        const string secretCustomName = "MyPrivateCustomXenotype42";
        var root = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
            .MetaMods("1.7.5011 rev24", "ludeon.rimworld", AcquisitionUnits.Biotech, AcquisitionUnits.Odyssey, AcquisitionUnits.Ideology)
            .Scenario("Crashlanded", playerFactionDef: "PlayerColony")
            .Build()));
        var game = root.Root!.Element("game")!;
        var world = game.Element("world")!;
        world.Add(new XElement("factionManager", new XElement("allFactions",
            new XElement("li", new XElement("def", "PlayerColony"), new XElement("loadID", 14)),
            new XElement("li", new XElement("def", "Pirate"), new XElement("loadID", 20)))));
        world.Add(new XElement("worldObjects", new XElement("worldObjects",
            new XElement("li", new XElement("def", "Settlement"), new XElement("tile", "100,0"),
                new XElement("ID", 42), new XElement("faction", "Faction_14")))));

        XElement Pawn(string id, string faction, string kindDef, XElement guest, XElement genes)
            => new("thing", new XAttribute("Class", "Pawn"),
                new XElement("def", "Human"),
                new XElement("id", id),
                new XElement("kindDef", kindDef),
                new XElement("faction", faction),
                new XElement("healthTracker"),
                guest,
                genes);

        XElement FreeGuest() => new("guest", new XElement("hostFaction", "null"));

        game.Add(new XElement("maps",
            new XElement("li",
                new XElement("uniqueID", 0),
                new XElement("generatorDef", "Base_Player"),
                new XElement("mapInfo", new XElement("parent", "WorldObject_42")),
                new XElement("things",
                    // Free colonist 1: omitted <xenotype> defaults to Baseliner
                    Pawn("Human1", "Faction_14", "Colonist", FreeGuest(), new XElement("genes")),
                    // Free colonist 2: Odyssey Starjack
                    Pawn("Human2", "Faction_14", "Colonist", FreeGuest(), new XElement("genes", new XElement("xenotype", "Starjack"))),
                    // Free colonist 3: custom xenotype name -> RimWorld/CustomXenotype (never retains text)
                    Pawn("Human3", "Faction_14", "Colonist", FreeGuest(), new XElement("genes",
                        new XElement("xenotypeName", secretCustomName),
                        new XElement("hybrid", "True"))),
                    // Prisoner: unresolvable modded XenotypeDef -> "?" bucket
                    Pawn("Human4", "Faction_20", "Pirate",
                        new XElement("guest",
                            new XElement("hostFaction", "Faction_14"),
                            new XElement("guestStatus", "Prisoner")),
                        new XElement("genes", new XElement("xenotype", "SomeMod_AlphaXeno"))),
                    // Slave: Sanguophage (in 1.7, IsSlaveOfColony requires Faction == player && GuestStatus == Slave)
                    Pawn("Human5", "Faction_14", "Slave",
                        new XElement("guest",
                            new XElement("hostFaction", "Faction_14"),
                            new XElement("guestStatus", "Slave")),
                        new XElement("genes", new XElement("xenotype", "Sanguophage")))))));

        var bytes = Encoding.UTF8.GetBytes(root.ToString());
        var read = new SaveReader().ReadDetailed(new MemoryStream(bytes, writable: false), "xeno.rws", bytes.Length, Stamp);
        Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
        var metrics = read.Metrics!.Metrics;

        var free = metrics[RosterSnapshot.FreeXenotypePeak].Values;
        Assert.Equal(MetricCoverage.Partial, free[MetricEntry.Scalar].Coverage);
        Assert.Equal(3L, free[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(1L, free["ludeon.rimworld.biotech/Baseliner"].IntegerValue);
        Assert.Equal(1L, free["ludeon.rimworld.odyssey/Starjack"].IntegerValue);
        Assert.Equal(1L, free[MetricKeys.CustomXenotypeBucket].IntegerValue);

        var prisoners = metrics[RosterSnapshot.PrisonerXenotypePeak].Values;
        Assert.Equal(1L, prisoners[MetricKeys.UnknownBucket].IntegerValue);

        var slaves = metrics[RosterSnapshot.SlaveXenotypePeak].Values;
        Assert.Equal(1L, slaves["ludeon.rimworld.biotech/Sanguophage"].IntegerValue);

        // Ensure custom player string never leaks into observation JSON/keys
        var serialized = System.Text.Json.JsonSerializer.Serialize(read);
        Assert.DoesNotContain(secretCustomName, serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void MembershipBirths_TracksSpeciesAndXenotypeBreakdownsWhileKeepingLegacyScalarComplete()
    {
        // Pure post-upgrade campaign: Complete scalar and breakdowns
        var fresh = new FamilyState { SinceTick = 0, ThroughTick = 60000 };
        MembershipFamily.Born(fresh, "baby1", 100, species: "ludeon.rimworld/Human", xenotype: MetricKeys.CustomXenotypeBucket);
        MembershipFamily.Born(fresh, "baby2", 200, species: "author.har/Ratkin", xenotype: "ludeon.rimworld.biotech/Highmate");
        var freshOut = new Dictionary<string, MetricEntry>();
        MembershipFamily.Family.Build(new FamilyOutputContext(fresh, 60000, freshOut));

        Assert.Equal(2L, freshOut[MembershipFamily.HumanBirths].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Complete, freshOut[MembershipFamily.HumanBirths].Values[MetricEntry.Scalar].Coverage);

        var freshSpecies = freshOut[MembershipFamily.HumanBirthsBySpecies].Values;
        Assert.Equal(MetricCoverage.Complete, freshSpecies[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, freshSpecies["ludeon.rimworld/Human"].IntegerValue);
        Assert.Equal(1L, freshSpecies["author.har/Ratkin"].IntegerValue);

        var freshXeno = freshOut[MembershipFamily.HumanBirthsByXenotype].Values;
        Assert.Equal(MetricCoverage.Complete, freshXeno[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, freshXeno[MetricKeys.CustomXenotypeBucket].IntegerValue);
        Assert.Equal(1L, freshXeno["ludeon.rimworld.biotech/Highmate"].IntegerValue);

        // Legacy checkpoint with births recorded before species/xenotype breakdown existed:
        // HumanBirths scalar stays Complete, while breakdowns become Partial because legacy births lack species/xenotype.
        var legacy = new FamilyState { SinceTick = 0, ThroughTick = 60000 };
        MembershipFamily.Born(legacy, "legacy_baby", 50); // legacy unattributed birth
        MembershipFamily.Born(legacy, "new_baby", 150, species: "ludeon.rimworld/Human", xenotype: "ludeon.rimworld.biotech/Baseliner");
        var legacyOut = new Dictionary<string, MetricEntry>();
        MembershipFamily.Family.Build(new FamilyOutputContext(legacy, 60000, legacyOut));

        Assert.Equal(2L, legacyOut[MembershipFamily.HumanBirths].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Complete, legacyOut[MembershipFamily.HumanBirths].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(MetricCoverage.Partial, legacyOut[MembershipFamily.HumanBirthsBySpecies].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, legacyOut[MembershipFamily.HumanBirthsBySpecies].Values["ludeon.rimworld/Human"].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, legacyOut[MembershipFamily.HumanBirthsByXenotype].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, legacyOut[MembershipFamily.HumanBirthsByXenotype].Values["ludeon.rimworld.biotech/Baseliner"].IntegerValue);
    }
}
