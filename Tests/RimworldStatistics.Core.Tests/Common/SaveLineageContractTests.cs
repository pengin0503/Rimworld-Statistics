using System.Text.Json.Nodes;

namespace RimworldStatistics.Core.Tests.Common;

/// <summary>
/// Common contract of saves and colonies for an optional MOD counter: several saves of one colony, proven parents,
/// sibling branches, a rewind, copied files, a missing parent, saves made without the MOD or its DLC, unknown future
/// data and an old profile. A colony shows the totals of one save, never a sum of saves.
/// </summary>
[Trait("Category", "Contract")]
public sealed class SaveLineageContractTests
{
    private const string Royalty = "Ludeon.RimWorld.Royalty";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AColonyShowsItsLatestSaveAndNeverAddsBranchesOrRewinds(bool breakdown)
    {
        var colony = new TestColony("branches");
        var metric = TestShapes.CounterMetric(breakdown: breakdown);
        var realMetric = TestShapes.RealMetric();
        var families = new[] { TestShapes.FamilyOf(metric), TestShapes.FamilyOf(realMetric) };
        var game = GameSession.NewGame(colony, families);
        game.Record(metric, 2, TestShapes.DefKey("A"));
        game.RecordReal(realMetric, 1.25);
        var root = game.Save(100, "root");
        game.Record(metric, 3, TestShapes.DefKey("B"));
        game.RecordReal(realMetric, 2.5);
        var first = game.Save(200, "first");           // root → first
        game.Record(metric, 1, TestShapes.DefKey("A"));
        game.RecordReal(realMetric, 0.75);
        var second = game.Save(300, "second");         // first → second: 6, real 4.5

        var sibling = GameSession.Load(colony, first, families);
        sibling.Record(metric, 10, TestShapes.DefKey("C"));
        sibling.RecordReal(realMetric, 6.25);
        var branch = sibling.Save(250, "branch");      // first → branch: 15, real 10.0, earlier than second
        var rewound = GameSession.Load(colony, root, families).Save(150, "rewind");   // root → rewind: 2

        var profile = new TestProfile().Played(colony, 300).Remember(metric, realMetric).Import(root, first, second, branch, rewound);
        var view = profile.View();
        Assert.Equal(CheckpointLineage.Branched, view.Colony(colony).Lineage);
        Assert.Equal(6, view.Row(colony, metric.StableKey).Value.Number);
        Assert.Equal(6, view.Overall(metric.StableKey).Value.Number);
        Assert.Equal(4.5, view.Row(colony, realMetric.StableKey).Value.Number!.Value, 6);
        Assert.Equal(4.5, view.Overall(realMetric.StableKey).Value.Number!.Value, 6);
        if (breakdown)
            Assert.Equal(new double?[] { 3, 3 }, view.Row(colony, metric.StableKey).Breakdowns.Select(b => b.Value.Number));

        // The sibling branch is played on and becomes the latest save: its own totals, not added to the other branch.
        // An aborted save (PrepareSave without Commit) neither drops nor double-counts subsequent events.
        sibling.Record(metric, 1, TestShapes.DefKey("C"));
        sibling.RecordReal(realMetric, 0.25);
        Assert.True(sibling.Accumulator.PrepareSave(350, new DateTime(2026, 11, 15, 0, 5, 50, DateTimeKind.Utc), "aborted")!.IsNew);
        sibling.RecordReal(realMetric, 0.25);
        var later = sibling.Save(400, "later");
        view = profile.Played(colony, 400).Import(later).Reopen().View();
        Assert.Equal(16, view.Row(colony, metric.StableKey).Value.Number);
        Assert.Equal(10.5, view.Row(colony, realMetric.StableKey).Value.Number!.Value, 6);
        Assert.Equal(MetricCoverage.Complete, view.Row(colony, metric.StableKey).Value.Coverage);
    }

    [Fact]
    public void AMissingParentNeitherDropsNorReducesTheTotalsOfTheSaveThatWasRead()
    {
        var colony = new TestColony("gap");
        var metric = TestShapes.CounterMetric();
        var game = GameSession.NewGame(colony, TestShapes.FamilyOf(metric));
        game.Record(metric, 2);
        var root = game.Save(100, "root");
        game.Record(metric, 3);
        game.Save(200, "not-imported");
        game.Record(metric, 1);
        var latest = game.Save(300, "latest");

        var view = new TestProfile().Played(colony, 300).Remember(metric).Import(root, latest).View();

        Assert.Equal(CheckpointLineage.Unknown, view.Colony(colony).Lineage);
        Assert.Equal(6, view.Row(colony, metric.StableKey).Value.Number);   // the checkpoint holds its whole lineage's totals
    }

    [Fact]
    public void ACopiedSaveFileIsOneSave()
    {
        var colony = new TestColony("copies");
        var metric = TestShapes.CounterMetric();
        var game = GameSession.NewGame(colony, TestShapes.FamilyOf(metric));
        game.Record(metric, 5);
        var saved = game.Save(100, "c1");
        MetricObservation Read(string file)
        {
            var copy = CheckpointMetrics.ToObservation(CheckpointJson.Parse(saved.Json).Payload!, saved.Json, colony.Identity,
                "1.7.4985", null, [TestShapes.FamilyOf(metric)]);
            copy.FileAliases.Add(new MetricFileAlias { FileName = file, FileSize = 1000, LastWriteUtc = new DateTime(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc) });
            return copy;
        }

        var profile = new TestProfile().Played(colony, 100).Remember(metric).Import(Read("Colony.rws"), Read("Colony - copy.rws"), Read("Colony.rws"));

        var checkpoint = Assert.Single(profile.Profile.MetricObservations, o => o.Source == MetricObservationSource.Checkpoint);
        Assert.Equal(new[] { "Colony.rws", "Colony - copy.rws" }, checkpoint.FileAliases.Select(a => a.FileName));
        Assert.Equal(5, profile.View().Row(colony, metric.StableKey).Value.Number);
    }

    [Fact]
    public void SavesMadeWithoutTheModKeepItsTotalsForWhenItIsBack()
    {
        var colony = new TestColony("mod-sets");
        var metric = TestShapes.CounterMetric();
        var family = TestShapes.FamilyOf(metric);
        var game = GameSession.NewGame(colony, family);
        game.Record(metric, 4);
        var withMod = game.Save(100, "with");

        // The MOD is removed: the save keeps the family's totals untouched, and its observation has no value.
        var without = GameSession.Load(colony, withMod).Save(200, "without");
        Assert.DoesNotContain(metric.StableKey, without.Observation.Metrics.Keys);
        Assert.Equal(CheckpointJson.Parse(withMod.Json).Payload!.Families[metric.AcquisitionId].Metrics[TestShapes.Counter].N,
            CheckpointJson.Parse(without.Json).Payload!.Families[metric.AcquisitionId].Metrics[TestShapes.Counter].N);

        var profile = new TestProfile().Played(colony, 200).Remember(metric).Import(withMod, without);
        var removed = profile.View().Row(colony, metric.StableKey).Value;
        Assert.Equal(4, removed.Number);
        Assert.Equal(MetricCoverage.Partial, removed.Coverage);   // the latest save could not measure it

        // The MOD is back: the totals continue, never restart and never add the old total again; the unrecorded time
        // makes them partial.
        var back = GameSession.Load(colony, without, family);
        back.Record(metric);
        var again = back.Save(300, "again");
        var value = profile.Played(colony, 300).Import(again).View().Row(colony, metric.StableKey).Value;
        Assert.Equal(5, value.Number);
        Assert.Equal(MetricCoverage.Partial, value.Coverage);
    }

    [Fact]
    public void ADlcStatisticWithoutItsDlcIsNotApplicableButARecordedValueStillShows()
    {
        var metric = TestShapes.CounterMetric(dlc: Royalty);
        var family = TestShapes.FamilyOf(metric);
        var quiet = new TestColony("no-royalty");
        var active = new TestColony("had-royalty");
        var none = GameSession.NewGame(quiet, family).Save(100, "quiet", "Ludeon.RimWorld");
        var game = GameSession.NewGame(active, family);
        game.Record(metric, 2);
        var recorded = game.Save(100, "recorded", "Ludeon.RimWorld");   // recorded while active, saved after removal

        var view = new TestProfile().Played(quiet, 100).Played(active, 100).Remember(metric).Import(none, recorded).View();

        Assert.Equal(MetricCoverage.NotApplicable, view.Row(quiet, metric.StableKey).Value.Coverage);
        Assert.Equal(0, view.Row(quiet, metric.StableKey).Targets);
        Assert.Equal(2, view.Row(active, metric.StableKey).Value.Number);
        var overall = view.Overall(metric.StableKey);
        Assert.Equal(2, overall.Value.Number);
        Assert.Equal(1, overall.Targets);   // the colony without the DLC is outside the total, not a zero in it
    }

    [Fact]
    public void UnknownFamiliesAndKeysOfANewerVersionSurviveEveryStep()
    {
        var colony = new TestColony("future");
        var metric = TestShapes.CounterMetric();
        var future = TestShapes.CounterMetric("future_family");
        var game = GameSession.NewGame(colony, TestShapes.FamilyOf(metric), TestShapes.FamilyOf(future));
        game.Record(metric, 1);
        game.Record(future, 9);
        var newer = game.Save(100, "newer");

        // An older version (it knows only one of the families) loads and saves again.
        var older = GameSession.Load(colony, newer, TestShapes.FamilyOf(metric));
        older.Record(metric);
        var saved = older.Save(200, "older");
        Assert.Equal(JsonNode.Parse(CheckpointJson.Serialize(CheckpointJson.Parse(newer.Json).Payload!))!["families"]![future.AcquisitionId]!.ToJsonString(),
            JsonNode.Parse(saved.Json)!["families"]![future.AcquisitionId]!.ToJsonString());

        saved.Observation.Metrics["mod.future.version.metric"] = MetricEntry.Of(3, new MetricValue { IntegerValue = 1, Coverage = MetricCoverage.Complete });
        var json = JsonNode.Parse(ProfileJson.Serialize(new TestProfile().Played(colony, 200).Remember(metric).Import(saved).Profile))!.AsObject();
        json["futureTopLevel"] = new JsonObject { ["kept"] = true };
        var reopened = ProfileJson.Deserialize(json.ToJsonString());
        var written = JsonNode.Parse(ProfileJson.Serialize(reopened))!;

        Assert.True(written["futureTopLevel"]!["kept"]!.GetValue<bool>());
        Assert.Contains("mod.future.version.metric", Assert.Single(reopened.MetricObservations).Metrics.Keys);
    }

    [Fact]
    public void AnOldProfileWithoutNumericRecordsOpensAndTakesNewModRecords()
    {
        var colony = new TestColony("old-profile");
        var old = JsonNode.Parse(ProfileJson.Serialize(new TestProfile().Played(colony, 50).Profile))!.AsObject();
        old["schemaVersion"] = 1;
        old.Remove("modMetricDescriptors");
        old.Remove("metricObservations");

        var profile = new TestProfile(ProfileJson.Deserialize(old.ToJsonString()));
        Assert.Single(profile.Profile.LiveCampaigns);
        Assert.Empty(profile.Profile.MetricObservations);

        var metric = TestShapes.CounterMetric();
        Assert.True(profile.View().View.Colonies.Count == 1);   // nothing numeric yet: the view still builds
        var game = GameSession.NewGame(colony, TestShapes.FamilyOf(metric));
        game.Record(metric, 3);
        var view = profile.Played(colony, 100).Remember(metric).Import(game.Save(100, "c1")).Reopen().View();
        Assert.Equal(3, view.Row(colony, metric.StableKey).Value.Number);
    }
}
