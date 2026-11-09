using System.Text;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>Old .rws files may retain player-to-faction relationships, but not every
/// runtime FactionDef flag; recovered observations must remain explicitly partial.</summary>
public sealed class HistoricFactionBackfillTests
{
    private static readonly DateTime Stamp = new(2026, 11, 9, 9, 0, 0, DateTimeKind.Utc);

    private static XElement Faction(string def, int id, string? playerKind = null, int? goodwill = null)
        => new("li",
            new XElement("def", def),
            new XElement("loadID", id),
            playerKind is null ? null : new XElement("relations", new XElement("li",
                new XElement("other", "Faction_1"),
                new XElement("kind", playerKind),
                goodwill is null ? null : new XElement("goodwill", goodwill.Value))));

    private static byte[] Save(params XElement[] factions)
    {
        using var original = new MemoryStream(new SaveXmlBuilder().Minimal()
            .Scenario("Crashlanded", playerFactionDef: "PlayerColony").Build());
        var root = XDocument.Load(original);
        root.Root!.Element("game")!.Element("world")!.Add(
            new XElement("factionManager", new XElement("allFactions", factions)));
        return Encoding.UTF8.GetBytes(root.ToString());
    }

    private static SaveReadResult Read(byte[] input)
        => new SaveReader().ReadDetailed(new MemoryStream(input, writable: false), "historical.rws", input.Length, Stamp);

    [Fact]
    public void Read_RetainedVanillaPlayerRelations_ArePartialSaveFileEvidence()
    {
        var input = Save(Faction("PlayerColony", 1), Faction("OutlanderCivil", 2, "Ally", 80),
            Faction("OutlanderRough", 3, "Hostile", -90));
        var before = input.ToArray();
        var output = Read(input);
        Assert.Equal(before, input);
        Assert.Equal(ObservationStatus.Ok, output.Observation.Status);
        Assert.NotNull(output.Metrics);
        Assert.Equal(MetricObservationSource.SaveFile, output.Metrics!.Source);
        var relations = output.Metrics.Metrics[FactionStateSnapshot.Relations].Values;
        Assert.Equal(2L, relations[MetricEntry.Scalar].IntegerValue);
        Assert.Equal(MetricCoverage.Partial, relations[MetricEntry.Scalar].Coverage);
        Assert.Equal(1L, relations["ally"].IntegerValue);
        Assert.Equal(1L, relations["hostile"].IntegerValue);
        Assert.Equal(MetricEvidence.Experimental, output.Metrics.EvidenceByAcquisition!["factions"]);
        var goodwill = output.Metrics.Metrics[FactionStateSnapshot.GoodwillRange].Values;
        Assert.Equal(-90L, goodwill["min"].IntegerValue);
        Assert.Equal(80L, goodwill["max"].IntegerValue);
    }

    [Fact]
    public void Read_MissingPlayerOrUnknownFactionIdentity_DoesNotInventZero()
    {
        var missingPlayer = Read(Save(Faction("OutlanderCivil", 2, "Hostile", -90)));
        Assert.NotNull(missingPlayer.Metrics);
        Assert.DoesNotContain(FactionStateSnapshot.Relations, missingPlayer.Metrics!.Metrics.Keys);

        var unknownOnly = Read(Save(Faction("PlayerColony", 1),
            Faction("ModdedFaction", 3, "Ally", 90)));
        Assert.DoesNotContain(FactionStateSnapshot.Relations, unknownOnly.Metrics!.Metrics.Keys);
    }

    [Fact]
    public void Read_DuplicateFactionIdOrInvalidRelation_RemainsUnknown()
    {
        var duplicate = Read(Save(Faction("PlayerColony", 1),
            Faction("OutlanderCivil", 2, "Ally", 80),
            Faction("OutlanderRough", 2, "Hostile", -80)));
        Assert.DoesNotContain(FactionStateSnapshot.Relations, duplicate.Metrics!.Metrics.Keys);
        var invalid = Read(Save(Faction("PlayerColony", 1),
            Faction("OutlanderCivil", 2, "Ally", 200)));
        Assert.DoesNotContain(FactionStateSnapshot.Relations, invalid.Metrics!.Metrics.Keys);
    }

    [Fact]
    public void Display_UsesRecoveredPartialReadingOnlyIfNoCapturedLiveValueExists()
    {
        var read = Read(Save(Faction("PlayerColony", 1), Faction("OutlanderCivil", 2, "Ally", 80)));
        var profile = new Profile();
        ProfileMutations.AddSaveObservation(profile, read.Observation);
        ProfileMutations.UpsertMetricObservation(profile, read.Metrics!);
        var colonies = CampaignMatcher.BuildColonies(profile);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        var recovered = MetricCalculator.BuildView(profile, colonies, stats, MetricRegistry.Catalog)
            .Colonies.Single().Rows.Single(r => r.Definition.Key == FactionStateSnapshot.Relations);
        Assert.Equal(1.0, recovered.Value.Number);
        Assert.Equal(MetricCoverage.Partial, recovered.Value.Coverage);
        Assert.Equal(MetricEvidence.Experimental, recovered.Value.Evidence);

        var live = new MetricObservation
        {
            Source = MetricObservationSource.SaveSnapshot,
            Key = "live-faction-1", Identity = read.Observation.Identity.Clone(),
            SaveTick = read.Observation.Values.TicksGame + 10,
            ObservedUtc = Stamp.AddHours(1),
            Metrics = FactionStateSnapshot.Build(new[] { new FactionStateFact("f", true, false, true, -5) }),
        };
        ProfileMutations.UpsertMetricObservation(profile, live);
        var preferred = MetricCalculator.BuildView(profile, colonies, stats, MetricRegistry.Catalog)
            .Colonies.Single().Rows.Single(r => r.Definition.Key == FactionStateSnapshot.Relations);
        Assert.Equal(1.0, preferred.Value.Number);
        Assert.Equal(MetricCoverage.SnapshotOnly, preferred.Value.Coverage);
    }
    [Fact]
    public void Read_ModSetFingerprint_ChangesWhenSaveSpecificLoadOrderChanges()
    {
        byte[] Make(params string[] ids) => new SaveXmlBuilder().Minimal()
            .MetaMods("1.7.5000 rev1275", ids).Build();
        var a = Read(Make("ludeon.rimworld", "brrainz.harmony", "mod.first"));
        var b = Read(Make("ludeon.rimworld", "brrainz.harmony", "mod.second"));
        var c = Read(Make("brrainz.harmony", "ludeon.rimworld", "mod.first"));
        Assert.NotNull(a.Metrics!.ModSetHash);
        Assert.Equal(64, a.Metrics.ModSetHash!.Length);
        Assert.NotEqual(a.Metrics.ModSetHash, b.Metrics!.ModSetHash);
        Assert.NotEqual(a.Metrics.ModSetHash, c.Metrics!.ModSetHash);
    }

    [Fact]
    public void Display_LaterSaveWithoutValidatedFactionEvidence_DoesNotEraseOlderPartialReading()
    {
        var old = Read(Save(Faction("PlayerColony", 1), Faction("OutlanderCivil", 2, "Hostile", -85)));
        var profile = new Profile();
        ProfileMutations.AddSaveObservation(profile, old.Observation);
        ProfileMutations.UpsertMetricObservation(profile, old.Metrics!);
        var later = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "later-save",
            Identity = old.Observation.Identity.Clone(),
            SaveTick = old.Observation.Values.TicksGame + 10000,
            ObservedUtc = Stamp.AddDays(1),
        };
        ProfileMutations.UpsertMetricObservation(profile, later);
        var colonies = CampaignMatcher.BuildColonies(profile);
        var stats = StatsCalculator.Calculate(colonies, new StatsOptions());
        var value = MetricCalculator.BuildView(profile, colonies, stats, MetricRegistry.Catalog)
            .Colonies.Single().Rows.Single(x => x.Definition.Key == FactionStateSnapshot.Relations).Value;
        Assert.Equal(1.0, value.Number);
        Assert.Equal(MetricCoverage.Partial, value.Coverage);
    }

    [Fact]
    public void Import_ReaderUpgradeRevisitsOldSaveOnceWithoutChangingItsBytes()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var bytes = Save(Faction("PlayerColony", 1), Faction("OutlanderCivil", 2, "Ally", 85));
        var filename = saves.File("old.rws");
        File.WriteAllBytes(filename, bytes);
        File.SetLastWriteTimeUtc(filename, Stamp);
        var session = new ProfileSession(new ProfileStore(store.Path), () => Stamp);
        var old = Read(bytes).Observation;
        old.ReaderVersion = 5;
        old.FileName = "old.rws";
        old.LastWriteUtc = File.GetLastWriteTimeUtc(filename);
        session.Mutate(p => ProfileMutations.AddSaveObservation(p, old), SaveMode.None);
        var engine = new ImportEngine(new SaveReader());
        var first = engine.Run(saves.Path, session, null, CancellationToken.None);
        Assert.Equal((1, 0, 0), (first.Read, first.Skipped, first.Failed));
        Assert.True(session.Read(p => p.MetricObservations.Any(o => o.Source == MetricObservationSource.SaveFile &&
            o.Metrics.ContainsKey(FactionStateSnapshot.Relations))));
        var second = engine.Run(saves.Path, session, null, CancellationToken.None);
        Assert.Equal((0, 1, 0), (second.Read, second.Skipped, second.Failed));
        Assert.Equal(bytes, File.ReadAllBytes(filename));
    }

}
