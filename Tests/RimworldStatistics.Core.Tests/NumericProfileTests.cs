using System.Text.Json.Nodes;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>Profile schema 2 and the numeric observation contract (numeric expansion design §2, plan A1).</summary>
public sealed class NumericProfileTests
{
    private static readonly DateTime Now = new(2026, 11, 7, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A profile as v1 (schema 1) wrote it: an ending, an exclusion, a colony key and one observation.</summary>
    private const string V1Body = """
        "saveObservations": [ { "fileName": "a.rws", "fileSize": 10, "lastWriteUtc": "2026-11-01T00:00:00Z", "readerVersion": 3, "status": "ok",
          "identity": { "seedString": "s", "persistentRandomValue": 1, "startingTile": "1,0", "gameStartAbsTick": 0, "firstWealthBits": 1, "startingPawnIds": [ "Thing_Human1" ] },
          "values": { "ticksGame": 60000 } } ],
        "liveCampaigns": [],
        "endingEvents": [ { "campaignId": "c1", "type": "ship", "ordinal": 0, "source": "live", "ticksGame": 600000, "realTimeUtc": "2026-11-02T00:00:00Z", "departed": 3 } ],
        "userDecisions": { "excludedCampaignKeys": [ "fp:0011223344556677" ], "manualMerges": [ [ "fp:aa", "fp:bb" ] ], "manualSplits": [] },
        "colonyKeys": [ { "key": "fp:0011223344556677", "fingerprintHash": "0011223344556677", "startingPawnIds": [ "Thing_Human1" ] } ],
        "importState": { "lastScanUtc": "2026-11-03T00:00:00Z" }
        """;

    private static string V1WithoutSchema => "{\n" + V1Body + "\n}";
    private static string V1Explicit => "{\n\"schemaVersion\": 1,\n" + V1Body + "\n}";

    private static Profile Changed(Profile p)
    {
        p.ImportState.LastScanUtc = Now;
        return p;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void V1Profile_LoadsUnchanged_AndFirstWriteIsSchema2WithProtectedCopy(bool explicitVersion)
    {
        using var dir = new TempDir();
        var original = explicitVersion ? V1Explicit : V1WithoutSchema;
        File.WriteAllText(dir.File("profile.json"), original);
        var store = new ProfileStore(dir.Path);

        var r = store.Load(Now);
        Assert.Equal((ProfileLoadSource.Primary, false), (r.Source, r.ReadOnly));
        Assert.Equal(original, File.ReadAllText(dir.File("profile.json")));          // loading alone never rewrites
        Assert.False(File.Exists(dir.File("profile.v1.json")));
        Assert.Empty(r.Profile.MetricObservations);

        // The v1 records are kept as they were.
        Assert.Equal("c1", r.Profile.EndingEvents.Single().CampaignId);
        Assert.Equal(new[] { "fp:0011223344556677" }, r.Profile.UserDecisions.ExcludedCampaignKeys);
        Assert.Equal(new[] { "fp:aa", "fp:bb" }, r.Profile.UserDecisions.ManualMerges.Single());
        Assert.Equal("fp:0011223344556677", r.Profile.ColonyKeys.Single().Key);
        Assert.Equal(3, r.Profile.SaveObservations.Single().ReaderVersion);

        store.Save(Changed(r.Profile));
        Assert.Equal(original, File.ReadAllText(dir.File("profile.v1.json")));
        var written = JsonNode.Parse(File.ReadAllText(dir.File("profile.json")))!;
        Assert.Equal(2, (int)written["schemaVersion"]!);
        Assert.Equal("c1", (string)written["endingEvents"]![0]!["campaignId"]!);
        Assert.Equal("fp:0011223344556677", (string)written["userDecisions"]!["excludedCampaignKeys"]![0]!);
    }

    [Fact]
    public void V1Copy_IsWrittenOnce_AndNeverOverwritten()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("profile.json"), V1Explicit);
        File.WriteAllText(dir.File("profile.v1.json"), "older protected copy");
        var store = new ProfileStore(dir.Path);
        var p = store.Load(Now).Profile;
        store.Save(Changed(p));
        p.ImportState.LastScanUtc = Now.AddHours(1);
        store.Save(p);
        Assert.Equal("older protected copy", File.ReadAllText(dir.File("profile.v1.json")));
        Assert.Equal(2, (int)JsonNode.Parse(File.ReadAllText(dir.File("profile.json")))!["schemaVersion"]!);
    }

    [Fact]
    public void V1Copy_WriteFailure_DefersTheSchema2Write()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("profile.json"), V1Explicit);
        Directory.CreateDirectory(dir.File("profile.v1.json"));   // the copy cannot be written
        var store = new ProfileStore(dir.Path);
        var p = store.Load(Now).Profile;
        Assert.ThrowsAny<IOException>(() => store.Save(Changed(p)));
        Assert.Equal(V1Explicit, File.ReadAllText(dir.File("profile.json")));
        Assert.False(File.Exists(dir.File("profile.json.bak1")));
    }

    [Fact]
    public void V1Copy_AfterRecoveryFromBackup_ProtectsTheBackupThatWasRead()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("profile.json"), "{ \"schemaVersion\": 1, \"saveObs");   // torn
        File.WriteAllText(dir.File("profile.json.bak1"), V1Explicit);
        var store = new ProfileStore(dir.Path);
        var r = store.Load(Now);
        Assert.Equal(ProfileLoadSource.Backup1, r.Source);
        store.Save(Changed(r.Profile));
        Assert.Equal(V1Explicit, File.ReadAllText(dir.File("profile.v1.json")));
        Assert.Equal(2, (int)JsonNode.Parse(File.ReadAllText(dir.File("profile.json")))!["schemaVersion"]!);
    }

    [Fact]
    public void V1Copy_PrimaryReplacedByAnotherSession_WritesNothing()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("profile.json"), V1Explicit);
        var store = new ProfileStore(dir.Path);
        var p = store.Load(Now).Profile;
        File.WriteAllText(dir.File("profile.json"), V1WithoutSchema);   // another game session wrote meanwhile
        Assert.Throws<IOException>(() => store.Save(Changed(p)));
        Assert.Equal(V1WithoutSchema, File.ReadAllText(dir.File("profile.json")));
        Assert.False(File.Exists(dir.File("profile.v1.json")));
    }

    [Fact]
    public void NewProfile_WritesSchema2_WithoutV1Copy()
    {
        using var dir = new TempDir();
        var store = new ProfileStore(dir.Path);
        store.Load(Now);
        store.Save(new Profile());
        Assert.Equal(2, (int)JsonNode.Parse(File.ReadAllText(dir.File("profile.json")))!["schemaVersion"]!);
        Assert.False(File.Exists(dir.File("profile.v1.json")));
    }

    [Fact]
    public void FutureSchema_IsReadOnly()
    {
        using var dir = new TempDir();
        const string json = """{ "schemaVersion": 3, "metricObservations": [ { "source": "somethingNew" } ] }""";
        File.WriteAllText(dir.File("profile.json"), json);
        var store = new ProfileStore(dir.Path);
        var r = store.Load(Now);
        Assert.Equal((ProfileLoadSource.NewerSchema, true), (r.Source, r.ReadOnly));
        Assert.Throws<InvalidOperationException>(() => store.Save(new Profile()));
        Assert.Equal(json, File.ReadAllText(dir.File("profile.json")));
    }

    private const string V2WithUnknowns = """
        {
          "schemaVersion": 2,
          "futureTopLevel": { "a": [ 1, 2 ] },
          "saveObservations": [ { "fileName": "a.rws", "status": "ok", "futureObservationField": "x",
              "identity": { "futureIdentityField": 5 }, "values": { "ticksGame": 1, "futureValue": 2 }, "display": { "futureDisplay": true } } ],
          "endingEvents": [ { "campaignId": "c", "type": "ship", "futureEndingField": 1 } ],
          "metricObservations": [
            { "source": "saveFile", "formatVersion": 4, "key": "abc", "saveTick": 60000, "observedUtc": "2026-11-01T00:00:00Z",
              "fileAliases": [ { "fileName": "a.rws", "fileSize": 10, "lastWriteUtc": "2026-11-01T00:00:00Z", "futureAlias": 1 } ],
              "futureObservationField": [ "keep" ],
              "metrics": {
                "wealth.total.mean": { "revision": 1, "values": { "": { "coverage": "snapshotOnly", "realValue": 123.5, "numerator": 370.5, "denominator": 3, "futureValueField": "v" } }, "futureEntryField": 9 },
                "broken.metric": { "revision": 1, "values": { "": { "coverage": "notACoverage", "integerValue": 3 } } },
                "quest.retained.success": { "revision": 1, "values": { "": { "coverage": "snapshotOnly", "integerValue": 2 } } }
              } },
            { "source": "notASource", "key": "quarantined-observation" }
          ]
        }
        """;

    [Fact]
    public void V2_UnknownFieldsAndBrokenValues_RoundTrip_WithoutDroppingOtherMetrics()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.File("profile.json"), V2WithUnknowns);
        var store = new ProfileStore(dir.Path);
        var r = store.Load(Now);
        Assert.Equal((ProfileLoadSource.Primary, false), (r.Source, r.ReadOnly));

        var usable = r.Profile.MetricObservations.Where(o => !o.IsQuarantined).ToList();
        var obs = Assert.Single(usable);
        Assert.Equal(2, r.Profile.MetricObservations.Count);
        Assert.Equal((MetricObservationSource.SaveFile, "abc", 60000), (obs.Source, obs.Key, obs.SaveTick));
        Assert.Equal(123.5, obs.Metrics["wealth.total.mean"].Values[""].RealValue);
        Assert.Equal(2L, obs.Metrics["quest.retained.success"].Values[""].IntegerValue);
        Assert.True(obs.Metrics["broken.metric"].IsQuarantined);       // only that key is set aside
        Assert.False(obs.Metrics["wealth.total.mean"].IsQuarantined);

        store.Save(Changed(r.Profile));
        var before = JsonNode.Parse(V2WithUnknowns)!;
        var after = JsonNode.Parse(File.ReadAllText(dir.File("profile.json")))!;
        Assert.True(JsonNode.DeepEquals(before["futureTopLevel"], after["futureTopLevel"]));
        var so = after["saveObservations"]![0]!;
        Assert.Equal("x", (string)so["futureObservationField"]!);
        Assert.Equal(5, (int)so["identity"]!["futureIdentityField"]!);
        Assert.Equal(2, (int)so["values"]!["futureValue"]!);
        Assert.True((bool)so["display"]!["futureDisplay"]!);
        Assert.Equal(1, (int)after["endingEvents"]![0]!["futureEndingField"]!);
        var mo = after["metricObservations"]!;
        Assert.True(JsonNode.DeepEquals(before["metricObservations"]![0]!["futureObservationField"], mo[0]!["futureObservationField"]));
        Assert.Equal(1, (int)mo[0]!["fileAliases"]![0]!["futureAlias"]!);
        Assert.Equal(9, (int)mo[0]!["metrics"]!["wealth.total.mean"]!["futureEntryField"]!);
        Assert.Equal("v", (string)mo[0]!["metrics"]!["wealth.total.mean"]!["values"]![""]!["futureValueField"]!);
        Assert.True(JsonNode.DeepEquals(before["metricObservations"]![0]!["metrics"]!["broken.metric"], mo[0]!["metrics"]!["broken.metric"]));
        Assert.True(JsonNode.DeepEquals(before["metricObservations"]![1], mo[1]));
    }

    [Fact]
    public void NonFiniteValue_IsolatesOnlyThatMetric()
    {
        const string json = """
            { "schemaVersion": 2, "metricObservations": [ { "source": "saveFile", "key": "k", "metrics": {
                "a": { "revision": 1, "values": { "": { "coverage": "snapshotOnly", "realValue": "NaN" } } },
                "b": { "revision": 1, "values": { "": { "coverage": "complete", "realValue": 1.5 } } } } } ] }
            """;
        var p = ProfileJson.Deserialize(json);
        var obs = p.MetricObservations.Single();
        Assert.True(obs.Metrics["a"].IsQuarantined);
        Assert.Equal(1.5, obs.Metrics["b"].Values[""].RealValue);
    }

    [Fact]
    public void MetricObservation_Json_UsesContractNames_AndOmitsUnusedValueFields()
    {
        var p = new Profile();
        p.MetricObservations.Add(new MetricObservation
        {
            Source = MetricObservationSource.SaveFile,
            FormatVersion = 4,
            Key = "deadbeef",
            Identity = new ColonyIdentity { SeedString = "s" },
            FileAliases = { new MetricFileAlias { FileName = "a.rws", FileSize = 10, LastWriteUtc = Now } },
            GameVersion = "1.7.4985 rev1269",
            SaveTick = 90000,
            ObservedUtc = Now,
            ActiveDlcIds = new List<string> { "ludeon.rimworld.royalty" },
            Metrics =
            {
                ["quest.retained.success"] = new MetricEntry { Revision = 1, Values = { [""] = new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = 1 } } },
            },
        });
        var node = JsonNode.Parse(ProfileJson.Serialize(p))!;
        Assert.Equal(2, (int)node["schemaVersion"]!);
        var mo = node["metricObservations"]![0]!.AsObject();
        Assert.Equal(new[] { "source", "formatVersion", "key", "identity", "fileAliases", "gameVersion", "saveTick", "observedUtc", "activeDlcIds", "metrics" },
            mo.Select(kv => kv.Key));
        Assert.Equal("saveFile", (string)mo["source"]!);
        var value = mo["metrics"]!["quest.retained.success"]!["values"]![""]!.AsObject();
        Assert.Equal(new[] { "coverage", "integerValue" }, value.Select(kv => kv.Key));
        Assert.Equal("snapshotOnly", (string)value["coverage"]!);
        Assert.Equal("2026-11-07T09:00:00Z", (string)mo["observedUtc"]!);

        var back = ProfileJson.Deserialize(ProfileJson.Serialize(p)).MetricObservations.Single();
        Assert.Equal(1L, back.Metrics["quest.retained.success"].Values[""].IntegerValue);
        Assert.Equal("a.rws", back.FileAliases.Single().FileName);
    }

    [Fact]
    public void V1Profile_HasEmptyMetricObservations_AndNullListsAreNormalized()
    {
        var p = ProfileJson.Deserialize("""{ "metricObservations": [ { "source": "saveFile", "key": "k", "metrics": null, "fileAliases": null, "identity": null } ] }""");
        var obs = p.MetricObservations.Single();
        Assert.Empty(obs.Metrics);
        Assert.Empty(obs.FileAliases);
        Assert.NotNull(obs.Identity);
        Assert.Empty(ProfileJson.Deserialize("{}").MetricObservations);
    }
}
