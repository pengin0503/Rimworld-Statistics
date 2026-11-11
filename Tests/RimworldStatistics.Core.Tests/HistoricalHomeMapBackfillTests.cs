using System.Text;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class HistoricalHomeMapBackfillTests
{
    private static readonly DateTime Stamp = new(2026, 11, 9, 11, 0, 0, DateTimeKind.Utc);

    private static XElement Player(int id = 14) => new("li",
        new XElement("def", "PlayerColony"), new XElement("loadID", id));

    private static XElement WorldObject(int id, string def = "Settlement", string faction = "Faction_14")
        => new("li", new XElement("def", def), new XElement("tile", "100,0"),
            new XElement("ID", id), new XElement("faction", faction),
            new XElement("questTags"));

    private static XElement Map(int id, string parent, bool pocket = false)
        => new("li", new XElement("uniqueID", id),
            new XElement("generatorDef", "Base_Player"),
            new XElement("mapInfo", new XElement("parent", parent),
                pocket ? new XElement("isPocketMap", "True") : null),
            new XElement("things", new XElement("thing",
                new XAttribute("Class", "Plant"), new XElement("def", "Plant_Grass"),
                new XElement("id", "Plant_Grass1"))));

    private static byte[] Save(
        IEnumerable<XElement>? maps = null,
        IEnumerable<XElement>? worldObjects = null,
        IEnumerable<XElement>? factions = null,
        string version = "1.7.4985 rev1269")
    {
        var root = XDocument.Load(new MemoryStream(new SaveXmlBuilder().Minimal()
            .Scenario("Crashlanded", playerFactionDef: "PlayerColony")
            .Meta(version).Build()));
        var world = root.Root!.Element("game")!.Element("world")!;
        world.Add(new XElement("factionManager",
            new XElement("allFactions", factions ?? new[] { Player() })));
        world.Add(new XElement("worldObjects", new XElement("worldObjects",
            worldObjects ?? new[] { WorldObject(42) })));
        root.Root.Element("game")!.Add(new XElement("maps",
            maps ?? new[] { Map(0, "WorldObject_42") }));
        return Encoding.UTF8.GetBytes(root.ToString());
    }

    private static SaveReadResult Read(byte[] bytes)
        => new SaveReader().ReadDetailed(new MemoryStream(bytes, writable: false), "home.rws", bytes.Length, Stamp);

    [Fact]
    public void ExplicitOwnedVanillaSettlementMap_IsPartialSavedStateOnly()
    {
        var bytes = Save();
        var before = bytes.ToArray();
        var read = Read(bytes);
        Assert.Equal(before, bytes);
        Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
        Assert.NotNull(read.Metrics);
        var value = read.Metrics!.Metrics[FacilitiesSnapshot.HomeMapsPeak].Values[MetricEntry.Scalar];
        Assert.Equal(1L, value.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, value.Coverage);
        Assert.Equal(MetricEvidence.Experimental, read.Metrics.EvidenceByAcquisition!["facilities"]);
        Assert.DoesNotContain("facilities.medical_beds.peak", read.Metrics.Metrics);
        Assert.DoesNotContain(ColonyStartSnapshot.Biome, read.Metrics.Metrics);
    }

    [Fact]
    public void UnknownOtherHomeParent_DoesNotBecomeZeroOrInflateKnownSubset()
    {
        var bytes = Save(
            maps: new[] { Map(0, "WorldObject_42"), Map(1, "WorldObject_43") },
            worldObjects: new[] { WorldObject(42), WorldObject(43, "ModdedGravshipParent") });
        var read = Read(bytes);
        var value = read.Metrics!.Metrics[FacilitiesSnapshot.HomeMapsPeak].Values[MetricEntry.Scalar];
        Assert.Equal(1L, value.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, value.Coverage);

        var onlyUnknown = Read(Save(maps: new[] { Map(0, "WorldObject_43") },
            worldObjects: new[] { WorldObject(43, "ModdedGravshipParent") }));
        Assert.DoesNotContain(FacilitiesSnapshot.HomeMapsPeak, onlyUnknown.Metrics!.Metrics.Keys);
    }

    [Theory]
    [InlineData("wrong-faction")]
    [InlineData("not-a-settlement")]
    [InlineData("pocket")]
    [InlineData("missing-reference")]
    [InlineData("missing-player")]
    [InlineData("unknown-version")]
    [InlineData("duplicate-map")]
    [InlineData("duplicate-object")]
    [InlineData("duplicate-player")]
    [InlineData("duplicate-faction-id")]
    [InlineData("invalid-player")]
    [InlineData("invalid-object-id")]
    public void UnverifiedOrAmbiguousEvidence_RemainsUnknown(string variant)
    {
        byte[] bytes = variant switch
        {
            "wrong-faction" => Save(worldObjects: new[] { WorldObject(42, faction: "Faction_5") }),
            "not-a-settlement" => Save(worldObjects: new[] { WorldObject(42, def: "Site") }),
            "pocket" => Save(maps: new[] { Map(0, "WorldObject_42", pocket: true) }),
            "missing-reference" => Save(maps: new[] { Map(0, "WorldObject_999") }),
            "missing-player" => Save(factions: new[] {
                new XElement("li", new XElement("def", "OutlanderCivil"), new XElement("loadID", "14")) }),
            "unknown-version" => Save(version: "1.6.4000"),
            "duplicate-map" => Save(maps: new[] { Map(0, "WorldObject_42"), Map(0, "WorldObject_42") }),
            "duplicate-object" => Save(worldObjects: new[] { WorldObject(42), WorldObject(42) }),
            "duplicate-player" => Save(factions: new[] { Player(), Player(15) }),
            "duplicate-faction-id" => Save(factions: new[] { Player(),
                new XElement("li", new XElement("def", "OutlanderCivil"), new XElement("loadID", "14")) }),
            "invalid-player" => Save(factions: new[] {
                new XElement("li", new XElement("def", "PlayerColony"), new XElement("loadID", "invalid")) }),
            "invalid-object-id" => Save(worldObjects: new[] {
                new XElement("li", new XElement("def", "Settlement"),
                    new XElement("ID", "not-an-int"), new XElement("faction", "Faction_14")) }),
            _ => throw new ArgumentOutOfRangeException(nameof(variant)),
        };
        var read = Read(bytes);
        Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
        Assert.DoesNotContain(FacilitiesSnapshot.HomeMapsPeak, read.Metrics!.Metrics.Keys);
    }

    [Fact]
    public void ExcessMapsAndHugeScalar_AreBoundedWithoutSuppressingUnrelatedMetrics()
    {
        var maps = Enumerable.Range(0, 129) // one above the reader's 128-map bound
            .Select(i => Map(i, "WorldObject_42")).ToArray();
        var overflow = Read(Save(maps: maps));
        Assert.Equal(ObservationStatus.Ok, overflow.Observation.Status);
        Assert.DoesNotContain(FacilitiesSnapshot.HomeMapsPeak, overflow.Metrics!.Metrics.Keys);
        Assert.Contains(MetricKeys.ModsLatest, overflow.Metrics.Metrics.Keys);

        // Large values, even whitespace, must not be buffered unboundedly.
        var longName = Map(0, "WorldObject_" + new string(' ', 300_000) + "42");
        var overlong = Read(Save(maps: new[] { longName }));
        Assert.Equal(ObservationStatus.Ok, overlong.Observation.Status);
        Assert.DoesNotContain(FacilitiesSnapshot.HomeMapsPeak, overlong.Metrics!.Metrics.Keys);
    }

    private static XElement Turret(string id, string faction = "Faction_14", string className = "Building_TurretGun")
        => new("thing", new XAttribute("Class", className),
            new XElement("def", "Turret_Mortar"),
            new XElement("id", id), new XElement("faction", faction),
            new XElement("map", "0"), new XElement("pos", "(1, 0, 2)"));

    private static XElement TurretMap(int mapId, string parent, params XElement[] things)
    {
        var map = Map(mapId, parent);
        map.Element("things")!.ReplaceNodes(things);
        return map;
    }

    [Fact]
    public void OnlyExplicitPlayerOwnedTurretsOnVerifiedHomeMap_CountAsPartialSnapshot()
    {
        // In actual 1.7 R2 game type index Building_TurretGun inherits Building_Turret.
        var bytes = Save(maps: new[] {
            TurretMap(0, "WorldObject_42",
                Turret("Thing_Turret1"), Turret("Thing_Turret2"),
                Turret("Thing_Turret2"), // duplicate thing ID never double-counts
                Turret("Thing_Hostile", faction: "Faction_77"),
                Turret("Thing_UnknownMod", className: "SomeMod.Turret"),
                new XElement("thing", new XAttribute("Class", "Medicine"), new XElement("id", "Med1"))),
            TurretMap(1, "WorldObject_43", Turret("Thing_SiteTurret"))
        }, worldObjects: new[] { WorldObject(42), WorldObject(43, def: "Site") });
        var original = bytes.ToArray();
        var r = Read(bytes);
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        Assert.Equal(original, bytes);
        var value = r.Metrics!.Metrics[FacilitiesSnapshot.TurretsPeak].Values[MetricEntry.Scalar];
        Assert.Equal(2L, value.IntegerValue);
        Assert.Equal(MetricCoverage.Partial, value.Coverage);
        Assert.Equal(MetricEvidence.Experimental, r.Metrics.EvidenceByAcquisition!["facilities"]);
        Assert.DoesNotContain("facilities.medical_beds.peak", r.Metrics.Metrics.Keys);
        Assert.DoesNotContain("facilities.running_generators.latest", r.Metrics.Metrics.Keys);
    }

    [Fact]
    public void TurretsWithoutProvenOwnerOrHomeRemainAbsentRatherThanZero()
    {
        var hostile = Read(Save(maps: new[] {
            TurretMap(0, "WorldObject_42", Turret("Thing_Turret1", "Faction_77"))
        }));
        Assert.DoesNotContain(FacilitiesSnapshot.TurretsPeak, hostile.Metrics!.Metrics.Keys);

        var site = Read(Save(maps: new[] {
            TurretMap(0, "WorldObject_43", Turret("Thing_Turret1"))
        }, worldObjects: new[] { WorldObject(43, def: "Site") }));
        Assert.DoesNotContain(FacilitiesSnapshot.TurretsPeak, site.Metrics!.Metrics.Keys);

        var unknownVersion = Read(Save(maps: new[] {
            TurretMap(0, "WorldObject_42", Turret("Thing_Turret1"))
        }, version: "1.6.4566 rev607"));
        Assert.DoesNotContain(FacilitiesSnapshot.TurretsPeak, unknownVersion.Metrics!.Metrics.Keys);

        var empty = Read(Save(maps: new[] { TurretMap(0, "WorldObject_42") }));
        Assert.DoesNotContain(FacilitiesSnapshot.TurretsPeak, empty.Metrics!.Metrics.Keys);
    }

    [Fact]
    public void MalformedTurrets_DoNotAuthorizeAnInventedPositiveCount()
    {
        var save = Read(Save(maps: new[] {
            TurretMap(0, "WorldObject_42",
                Turret("", "Faction_14"),
                Turret("Thing_Turret2", "Faction_77"),
                Turret("Thing_Turret3", "", "Building_TurretGun"))
        }));
        Assert.DoesNotContain(FacilitiesSnapshot.TurretsPeak, save.Metrics!.Metrics.Keys);
        Assert.Contains(FacilitiesSnapshot.HomeMapsPeak, save.Metrics.Metrics.Keys);
    }

    private static XElement StockThing(string id, string def, string className, int count,
        string? faction = null, bool forbidden = false)
        => new("thing", new XAttribute("Class", className),
            new XElement("def", def), new XElement("id", id),
            new XElement("map", "0"),
            new XElement("stackCount", count),
            faction is null ? null : new XElement("faction", faction),
            forbidden ? new XElement("forbidden", "True") : null);

    [Fact]
    public void KnownCoreSilverAndMedicine_OnVerifiedHomeCountOnlyPositivePartialStocks()
    {
        var map = TurretMap(0, "WorldObject_42",
            StockThing("Silver1", "Silver", "ThingWithComps", 400),
            StockThing("Silver1", "Silver", "ThingWithComps", 400), // same ID, one stack
            StockThing("SilverHostile", "Silver", "ThingWithComps", 500, faction: "Faction_77"),
            StockThing("SilverForbidden", "Silver", "ThingWithComps", 50, forbidden: true),
            StockThing("SilverOtherClass", "Silver", "Thing", 60),
            StockThing("Med1", "MedicineHerbal", "Medicine", 2),
            StockThing("Med2", "MedicineIndustrial", "Medicine", 3),
            StockThing("ModdedMed", "MedicineModded", "Medicine", 8),
            StockThing("SilverBadCount", "Silver", "ThingWithComps", -9));
        var bytes = Save(maps: new[] { map });
        var r = Read(bytes);
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        Assert.Equal(MetricCoverage.Partial, r.Metrics!.Metrics[StockSnapshot.SilverLatest].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(400, r.Metrics.Metrics[StockSnapshot.SilverLatest].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(5, r.Metrics.Metrics[StockSnapshot.MedicinePeak].Values[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, r.Metrics.Metrics[StockSnapshot.MedicinePeak].Values[MetricEntry.Scalar].Coverage);
        Assert.Equal(MetricEvidence.Experimental, r.Metrics.EvidenceByAcquisition!["stock"]);
        Assert.DoesNotContain(StockSnapshot.MaterialsLatest, r.Metrics.Metrics);
        Assert.DoesNotContain(StockSnapshot.FoodLatest, r.Metrics.Metrics);
    }

    [Fact]
    public void HistoricalStock_NoConfirmedPositiveEvidence_MustNotInventZero()
    {
        var empty = Read(Save(maps: new[] { TurretMap(0, "WorldObject_42") }));
        Assert.DoesNotContain(StockSnapshot.SilverLatest, empty.Metrics!.Metrics);
        Assert.DoesNotContain(StockSnapshot.MedicinePeak, empty.Metrics.Metrics);

        var unowned = Read(Save(maps: new[] {
            TurretMap(0, "WorldObject_43", StockThing("Silver1", "Silver", "ThingWithComps", 100))
        }, worldObjects: new[] { WorldObject(43, def: "Site") }));
        Assert.DoesNotContain(StockSnapshot.SilverLatest, unowned.Metrics!.Metrics);

        var old = Read(Save(version: "1.6.4566 rev607", maps: new[] {
            TurretMap(0, "WorldObject_42", StockThing("Silver1", "Silver", "ThingWithComps", 100))
        }));
        Assert.DoesNotContain(StockSnapshot.SilverLatest, old.Metrics!.Metrics);
    }

    [Fact]
    public void HistoricalStock_ConflictedIdAndInvalidStackNeverInflateTotals()
    {
        var bytes = Save(maps: new[] { TurretMap(0, "WorldObject_42",
            StockThing("SilverSame", "Silver", "ThingWithComps", 100),
            StockThing("SilverSame", "Silver", "ThingWithComps", 500, faction: "Faction_77"),
            StockThing("MedBad", "MedicineUltratech", "Medicine", int.MaxValue),
            StockThing("MedOK", "MedicineHerbal", "Medicine", 1)) });
        var r = Read(bytes);
        Assert.DoesNotContain(StockSnapshot.SilverLatest, r.Metrics!.Metrics);
        Assert.Equal(1, r.Metrics.Metrics[StockSnapshot.MedicinePeak].Values[MetricEntry.Scalar].IntegerValue);
    }

    // R2 Assembly-CSharp: Pawn.IsPrisonerOfColony checks guest status AND host player;
    // Pawn.IsSlaveOfColony checks guest status AND pawn's player faction.
    // joinStatus is a requested future joining mode, NOT current enslavement.
    private static XElement SavedHuman(string id, string? faction,
        string? guestStatus = null, string hostFaction = "null",
        string joinStatus = "JoinAsSlave", string kind = "Colonist")
    {
        return new XElement("thing", new XAttribute("Class", "Pawn"),
            new XElement("def", "Human"), new XElement("kindDef", kind),
            new XElement("id", id), faction is null ? null : new XElement("faction", faction),
            new XElement("guest",
                new XElement("hostFaction", hostFaction),
                guestStatus is null ? null : new XElement("guestStatus", guestStatus),
                new XElement("joinStatus", joinStatus)),
            new XElement("healthTracker", new XElement("hediffSet")));
    }

    [Fact]
    public void HistoricalRoster_OnlyVerifiedLivingHomeHumanStatusesBecomePartial()
    {
        const string slaves = "population.snapshot.slaves";
        var bytes = Save(maps: new[] {
            TurretMap(0, "WorldObject_42",
                SavedHuman("Free1", "Faction_14"),
                SavedHuman("Free1", "Faction_14"),
                SavedHuman("Free2", "Faction_14", joinStatus: "JoinAsColonist"),
                SavedHuman("Slave1", "Faction_14", guestStatus: "Slave"),
                SavedHuman("Prisoner1", "Faction_99", guestStatus: "Prisoner", hostFaction: "Faction_14"),
                SavedHuman("Foreign1", "Faction_99"),
                SavedHuman("Guest1", "Faction_99", guestStatus: "Guest", hostFaction: "Faction_14"),
                SavedHuman("AnotherHostPrisoner", "Faction_99", guestStatus: "Prisoner", hostFaction: "Faction_50"),
                new XElement("thing", new XAttribute("Class", "Pawn"), new XElement("def", "Sheep"),
                    new XElement("id", "Sheep1"), new XElement("faction", "Faction_14"))),
            TurretMap(1, "WorldObject_43", SavedHuman("OutsideHome", "Faction_14"))
        }, worldObjects: new[] { WorldObject(42), WorldObject(43, "Site") });
        var original = bytes.ToArray();
        var read = Read(bytes);
        Assert.Equal(ObservationStatus.Ok, read.Observation.Status);
        Assert.Equal(original, bytes);
        foreach (var (key, expected) in new[] {
            (RosterSnapshot.Free, 2L), (RosterSnapshot.Prisoners, 1L), (slaves, 1L) })
        {
            var value = read.Metrics!.Metrics[key].Values[MetricEntry.Scalar];
            Assert.Equal(expected, value.IntegerValue);
            Assert.Equal(MetricCoverage.Partial, value.Coverage);
        }
        Assert.Equal(MetricEvidence.Experimental, read.Metrics!.EvidenceByAcquisition!["roster"]);
        Assert.DoesNotContain(RosterSnapshot.Animals, read.Metrics.Metrics);
    }

    [Fact]
    public void HistoricalRoster_UnknownsAndUnprovenZeroStayMissing()
    {
        const string slaves = "population.snapshot.slaves";
        var noPawns = Read(Save());
        foreach (var key in new[] { RosterSnapshot.Free, RosterSnapshot.Prisoners, slaves })
            Assert.DoesNotContain(key, noPawns.Metrics!.Metrics);

        var wrongMap = Read(Save(maps: new[] {
            TurretMap(0, "WorldObject_43", SavedHuman("F1", "Faction_14"))
        }, worldObjects: new[] { WorldObject(43, "Site") }));
        Assert.DoesNotContain(RosterSnapshot.Free, wrongMap.Metrics!.Metrics);

        var wrongVersion = Read(Save(version: "1.6.4633 rev1261", maps: new[] {
            TurretMap(0, "WorldObject_42", SavedHuman("F1", "Faction_14"))
        }));
        Assert.DoesNotContain(RosterSnapshot.Free, wrongVersion.Metrics!.Metrics);

        var contradictory = Read(Save(maps: new[] {
            TurretMap(0, "WorldObject_42",
                SavedHuman("Same", "Faction_14"),
                SavedHuman("Same", "Faction_14", guestStatus: "Slave"),
                SavedHuman("BadJoin", "Faction_99", joinStatus: "JoinAsSlave"),
                SavedHuman("NoHost", "Faction_99", guestStatus: "Prisoner"),
                SavedHuman("UnknownStatus", "Faction_14", guestStatus: "ModStatus"))
        }));
        foreach (var key in new[] { RosterSnapshot.Free, RosterSnapshot.Prisoners, slaves })
            Assert.DoesNotContain(key, contradictory.Metrics!.Metrics);
    }

    [Fact]
    public void ReaderUpgrade_ReimportsOnceAndLeavesOriginalUnmodified()
    {
        using var saves = new TempDir();
        using var profileDir = new TempDir();
        var content = Save();
        var path = saves.File("historic.rws");
        File.WriteAllBytes(path, content);
        File.SetLastWriteTimeUtc(path, Stamp);
        var session = new ProfileSession(new ProfileStore(profileDir.Path), () => Stamp);
        var old = Read(content).Observation;
        old.ReaderVersion = SaveReader.Version - 1;
        old.FileName = "historic.rws";
        old.LastWriteUtc = File.GetLastWriteTimeUtc(path);
        session.Mutate(p => ProfileMutations.AddSaveObservation(p, old), SaveMode.None);

        var importer = new ImportEngine(new SaveReader());
        var first = importer.Run(saves.Path, session, null, CancellationToken.None);
        Assert.Equal((1, 0), (first.Read, first.Failed));
        Assert.True(session.Read(p => p.MetricObservations.Any(o =>
            o.Metrics.ContainsKey(FacilitiesSnapshot.HomeMapsPeak))));
        var second = importer.Run(saves.Path, session, null, CancellationToken.None);
        Assert.Equal((0, 1, 0), (second.Read, second.Skipped, second.Failed));
        Assert.Equal(content, File.ReadAllBytes(path));
        Assert.Equal(Stamp, File.GetLastWriteTimeUtc(path));
    }
}
