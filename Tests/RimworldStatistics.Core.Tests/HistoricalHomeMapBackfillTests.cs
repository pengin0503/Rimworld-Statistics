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
        Assert.DoesNotContain(FacilitiesSnapshot.MedicalBedsPeak, read.Metrics.Metrics);
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
