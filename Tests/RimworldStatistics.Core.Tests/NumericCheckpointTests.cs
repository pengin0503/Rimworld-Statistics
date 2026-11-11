using System.Text.Json;
using System.Xml.Linq;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

/// <summary>
/// Phase C0: the save-embedded checkpoint, its lineage and the profile records built from it (numeric expansion design §2
/// "Cの保存/Cのプロファイル", §3 "Cの確定プロトコル"; plan C0). A dummy counting family stands in for the real ones.
/// </summary>
public sealed class NumericCheckpointTests
{
    private static readonly DateTime T0 = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private const string Campaign = "c0ffee00c0ffee00c0ffee00c0ffee00";

    /// <summary>Counts "events"; also reports the first event day when recorded from the start.</summary>
    private static readonly LiveFamily Dummy = new()
    {
        Name = "test",
        Revision = 1,
        Build = c =>
        {
            c.Put("test.count", c.Value(integer: c.State.Peek("events")?.N ?? 0));
            var first = c.State.Peek("events")?.First;
            c.Put("test.first_day", !c.FromStart
                ? new MetricValue { Coverage = MetricCoverage.Unknown }
                : c.Value(real: first is { } f ? f / 60000.0 : null));
        },
    };

    private static readonly LiveFamily[] Families = { Dummy };
    private static readonly FamilySpec[] Recording = { Dummy.Spec };

    private static void Events(LiveMetricAccumulator acc, int n, int tick = 0)
    {
        var s = acc.Family("test")!;
        for (var i = 0; i < n; i++)
        {
            s.Count("events");
            s.Mark("events", tick);
        }
    }

    private static SaveDraft Save(LiveMetricAccumulator acc, string id, int tick, int minute = 0)
    {
        var d = acc.PrepareSave(tick, T0.AddMinutes(minute), id)!;
        acc.Commit(d);
        return d;
    }

    private static ColonyIdentity Identity() => new()
    {
        SeedString = "seed", PersistentRandomValue = 1, StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 7,
        StartingPawnIds = { "Thing_Human1" }, ModCampaignId = Campaign,
    };

    private static MetricObservation Obs(SaveDraft d)
        => CheckpointMetrics.ToObservation(d.Payload ?? CheckpointJson.Parse(d.Json).Payload!, d.Json, Identity(), "1.7.4985 rev1269", null, Families);

    private static long? Count(MetricObservation? o) => o?.Scalar("test.count", 1)?.IntegerValue;

    private static Profile ProfileWithColony()
    {
        var p = new Profile();
        ProfileMutations.ApplyLiveObservation(p, Campaign, Identity(), new ObservedValues { TicksGame = 1000 }, new DisplayInfo(), T0);
        return p;
    }

    private static readonly MetricDefinition CountDef = new()
    {
        Key = "test.count", StoredKey = "test.count", LedgerIds = "T-0", Category = MetricCategory.Colony, Unit = MetricUnit.Count,
        Provenance = MetricProvenance.LiveAggregate, Source = MetricObservationSource.Checkpoint, Overall = OverallAggregation.Sum,
        LabelKey = "x", DescriptionKey = "x",
    };

    private static NumericRow Overall(Profile p)
    {
        var colonies = CampaignMatcher.BuildColonies(p);
        var view = MetricCalculator.BuildView(p, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()), new[] { CountDef });
        return view.Overall.Single(r => r.Definition.Key == "test.count");
    }

    // ----- codec -----

    [Fact]
    public void Json_RoundTrip_KeepsIdsTotalsAndUnknownFields()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 3, tick: 120000);
        acc.Family("test")!.CountBy("by", "Berserk", 2);
        acc.Family("test")!.Track("open", "Thing_Human9", 500);
        var d = acc.PrepareSave(130000, T0, "P")!;

        var parsed = CheckpointJson.Parse(d.Json);
        Assert.Equal(CheckpointParseStatus.Ok, parsed.Status);
        var p = parsed.Payload!;
        Assert.Equal(1, p.FormatVersion);
        Assert.Equal("P", p.CheckpointId);
        Assert.Null(p.ParentCheckpointId);
        Assert.Equal(130000, p.SaveTick);
        Assert.Equal(T0, p.SavedUtc);
        var f = p.Families["test"];
        Assert.Equal((1, 0, 130000, true), (f.Revision, f.SinceTick, f.ThroughTick, f.Continuous));
        Assert.Equal(3, f.Metrics["events"].N);
        Assert.Equal(120000, f.Metrics["events"].First);
        Assert.Equal(2, f.Metrics["by"].By!["Berserk"]);
        Assert.Equal(500, f.Active["open"]["Thing_Human9"]);
        Assert.Equal(d.Json, CheckpointJson.Serialize(p));   // a payload reads back to the same text

        var withUnknown = d.Json.Replace("\"formatVersion\":1", "\"formatVersion\":1,\"future\":{\"x\":1}")
            .Replace("\"revision\":1", "\"revision\":1,\"futureFamilyField\":[1,2]");
        var again = CheckpointJson.Serialize(CheckpointJson.Parse(withUnknown).Payload!);
        Assert.Contains("\"future\":{\"x\":1}", again);
        Assert.Contains("\"futureFamilyField\":[1,2]", again);
    }

    [Fact]
    public void Resume_PrepareSave_PreservesUnknownRootFieldsAcrossGenerations()
    {
        var initial = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(initial, 1);
        var parent = Save(initial, "P", 10);
        var withFuture = parent.Json.Replace("\"formatVersion\":1",
            "\"formatVersion\":1,\"future\":{\"items\":[1,2],\"note\":\"keep\"},\"extraFlag\":true");

        var resumed = LiveMetricAccumulator.Resume(withFuture, Recording, 10);
        Events(resumed, 2);
        var first = resumed.PrepareSave(20, T0, "A")!;
        Assert.True(first.IsNew);
        Assert.Equal("P", first.Payload!.ParentCheckpointId);
        Assert.Contains("\"future\":{\"items\":[1,2],\"note\":\"keep\"}", first.Json);
        Assert.Contains("\"extraFlag\":true", first.Json);
        Assert.NotSame(resumed.Loaded!.ExtensionData, first.Payload.ExtensionData);

        resumed.Commit(first);
        Events(resumed, 1);
        var second = resumed.PrepareSave(30, T0, "B")!;
        Assert.Contains("\"future\":{\"items\":[1,2],\"note\":\"keep\"}", second.Json);
        Assert.Contains("\"extraFlag\":true", second.Json);
        Assert.Equal("A", second.Payload!.ParentCheckpointId);
    }

    [Fact]
    public void Json_UnreadableFamily_IsKeptVerbatimAndOthersStillRead()
    {
        var json = "{\"formatVersion\":1,\"checkpointId\":\"P\",\"saveTick\":10,\"savedUtc\":\"2026-03-01T12:00:00Z\",\"families\":{"
            + "\"test\":{\"revision\":1,\"sinceTick\":0,\"throughTick\":10,\"continuous\":true,\"metrics\":{\"events\":{\"n\":2}},\"active\":{}},"
            + "\"broken\":{\"revision\":\"x\"},\"future\":{\"revision\":9,\"metrics\":{\"a\":{\"n\":1}}}}}";
        var p = CheckpointJson.Parse(json).Payload!;
        Assert.False(p.Families["test"].IsQuarantined);
        Assert.True(p.Families["broken"].IsQuarantined);
        Assert.Contains("\"broken\":{\"revision\":\"x\"}", CheckpointJson.Serialize(p));
    }

    [Theory]
    [InlineData("{\"formatVersion\":2,\"checkpointId\":\"P\",\"anything\":true}", CheckpointParseStatus.UnknownFormat)]
    [InlineData("{\"formatVersion\":1,\"checkpointId\":\"\",\"saveTick\":0,\"families\":{}}", CheckpointParseStatus.Invalid)]
    [InlineData("{\"formatVersion\":1,\"checkpointId\":\"P\",\"saveTick\":-5,\"families\":{}}", CheckpointParseStatus.Invalid)]
    [InlineData("{\"checkpointId\":\"P\"}", CheckpointParseStatus.Invalid)]
    [InlineData("not json", CheckpointParseStatus.Invalid)]
    [InlineData("[1,2]", CheckpointParseStatus.Invalid)]
    [InlineData("", CheckpointParseStatus.Invalid)]
    public void Json_OtherContent_IsNeverReadAsACheckpoint(string json, CheckpointParseStatus expected)
    {
        var r = CheckpointJson.Parse(json);
        Assert.Equal(expected, r.Status);
        Assert.Null(r.Payload);
    }

    [Fact]
    public void Hash_IsSha256OfTheUtf8Text()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", CheckpointJson.Hash("abc"));
        Assert.NotEqual(CheckpointJson.Hash("{\"a\":1}"), CheckpointJson.Hash("{\"a\":2}"));
    }

    // ----- totals and their caps -----

    [Fact]
    public void FamilyOps_CapKeysButKeepCounting()
    {
        var s = new FamilyState();
        for (var i = 0; i < FamilyOps.KeyCap; i++)
            Assert.True(s.CountBy("by", "k" + i));
        Assert.False(s.CountBy("by", "new"));
        Assert.True(s.CountBy("by", "k0", 2));        // an existing key goes on
        Assert.True(s.Acc("by").Overflow);
        Assert.Equal(FamilyOps.KeyCap, s.Acc("by").By!.Count);
        Assert.Equal(3, s.Acc("by").By!["k0"]);

        for (var i = 0; i < FamilyOps.KeyCap; i++)
            s.Track("open", "p" + i, i);
        Assert.False(s.Track("open", "late", 1));
        Assert.True(s.TrackerOverflowed("open"));
        Assert.True(s.Track("open", "p1", 99));         // an existing id is updated
        Assert.Equal(99, s.Untrack("open", "p1"));
        Assert.Null(s.Tracked("open", "p1"));
    }

    [Fact]
    public void FamilyOps_GapsDistinctAndExtremes()
    {
        var s = new FamilyState();
        s.Gap("raid", 60000);
        s.Gap("raid", 180000);
        s.Gap("raid", 210000);
        var g = s.Acc("raid");
        Assert.Equal((2L, 150000.0, 30000.0, 120000.0, 60000, 210000), (g.N!.Value, g.Sum!.Value, g.Min!.Value, g.Max!.Value, g.First!.Value, g.Last!.Value));

        Assert.True(s.Distinct("tiles", "1,0"));
        Assert.False(s.Distinct("tiles", "1,0"));
        Assert.True(s.Distinct("tiles", "2,0"));
        Assert.Equal(2, s.Acc("tiles").By!.Count);

        s.Observe("tend", 0.5, "Thing_Doc1");
        s.Observe("tend", 0.9, "Thing_Doc2");
        s.Observe("tend", 0.9, "Thing_Doc3");   // a tie keeps the first holder
        s.Observe("tend", 0.1, "Thing_Doc4");
        var t = s.Acc("tend");
        Assert.Equal((4L, 2.4, 0.1, 0.9, "Thing_Doc4", "Thing_Doc2"), (t.N!.Value, Math.Round(t.Sum!.Value, 9), t.Min!.Value, t.Max!.Value, t.MinKey, t.MaxKey));
    }

    // ----- the accumulator across saves and loads -----

    [Fact]
    public void NewGame_FromTickZero_IsCompleteAndKnowsTheFirstEvent()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 2, tick: 90000);
        var d = Save(acc, "P", 120000);
        var o = Obs(d);
        Assert.Equal(2, Count(o));
        Assert.Equal(MetricCoverage.Complete, o.Scalar("test.count", 1)!.Coverage);
        Assert.Equal(1.5, o.Scalar("test.first_day", 1)!.RealValue);
        Assert.Equal(MetricObservationSource.Checkpoint, o.Source);
        Assert.Equal(("P", (string?)null, 120000, T0), (o.Key, o.ParentKey, o.SaveTick, o.ObservedUtc));
        Assert.Equal(CheckpointJson.Hash(d.Json), o.PayloadHash);
    }

    [Fact]
    public void PreInstallSave_StartsLate_IsPartialAndHasNoFirstDay()
    {
        var acc = LiveMetricAccumulator.Resume(null, Recording, 500000);
        Assert.Null(acc.ParentId);
        Assert.Null(acc.Loaded);
        Events(acc, 1, tick: 510000);
        var o = Obs(Save(acc, "P", 600000));
        Assert.Equal(1, Count(o));
        Assert.Equal(MetricCoverage.Partial, o.Scalar("test.count", 1)!.Coverage);
        Assert.Equal(500000, o.Scalar("test.count", 1)!.SinceTick);
        Assert.Equal(MetricCoverage.Unknown, o.Scalar("test.first_day", 1)!.Coverage);
    }

    [Fact]
    public void Resume_ContinuesAFamilyThatRanUpToTheSave()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 3);
        var p = Save(acc, "P", 100);
        var loaded = LiveMetricAccumulator.Resume(p.Json, Recording, 100);
        Assert.Equal("P", loaded.ParentId);
        Assert.Equal("P", loaded.Loaded!.CheckpointId);
        Assert.Equal(p.Json, loaded.LoadedJson);
        Events(loaded, 1);
        var a = Save(loaded, "A", 200);
        Assert.Equal("P", a.Payload!.ParentCheckpointId);
        Assert.Equal(4, Count(Obs(a)));
        Assert.Equal(MetricCoverage.Complete, Obs(a).Scalar("test.count", 1)!.Coverage);
    }

    [Fact]
    public void Resume_AfterAGap_KeepsTheTotalsButIsPartial()
    {
        // The family did not record in the session that saved P (its gate was closed), so P carries it through tick 100 only.
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 3);
        Save(acc, "P0", 100);
        var off = LiveMetricAccumulator.Resume(Save(acc, "P", 100).Json, Array.Empty<FamilySpec>(), 100);
        Assert.Null(off.Family("test"));
        var other = new FamilySpec("other", 1);
        var mixed = LiveMetricAccumulator.Resume(Save(acc, "P1", 100).Json, new[] { other }, 100);
        var carried = mixed.PrepareSave(5000, T0, "C")!;
        Assert.True(carried.IsNew);
        var cf = carried.Payload!.Families["test"];
        Assert.Equal((100, 3L), (cf.ThroughTick, cf.Metrics["events"].N!.Value));   // carried verbatim, not advanced
        Assert.Equal(MetricCoverage.Partial, Obs(carried).Scalar("test.count", 1)!.Coverage);

        var back = LiveMetricAccumulator.Resume(carried.Json, Recording, 5000);
        Events(back, 1);
        var b = Save(back, "B", 6000);
        Assert.False(b.Payload!.Families["test"].Continuous);
        Assert.Equal(4, Count(Obs(b)));
        Assert.Equal(MetricCoverage.Partial, Obs(b).Scalar("test.count", 1)!.Coverage);
    }

    [Fact]
    public void Resume_AnotherRevision_StartsOverAndNeverMixesDefinitions()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 3);
        var p = Save(acc, "P", 100);
        var v2 = new[] { new FamilySpec("test", 2) };
        var loaded = LiveMetricAccumulator.Resume(p.Json, v2, 100);
        Events(loaded, 1);
        var f = Save(loaded, "A", 200).Payload!.Families["test"];
        Assert.Equal((2, 100, 1L), (f.Revision, f.SinceTick, f.Metrics["events"].N!.Value));
    }

    [Fact]
    public void Resume_UnknownFamiliesAndFormats_AreWrittenBackUnchanged()
    {
        var withFuture = "{\"formatVersion\":1,\"checkpointId\":\"P\",\"saveTick\":10,\"savedUtc\":\"2026-03-01T12:00:00Z\",\"families\":{"
            + "\"futureFamily\":{\"revision\":3,\"sinceTick\":0,\"throughTick\":10,\"continuous\":true,\"metrics\":{\"z\":{\"n\":7}},\"active\":{},\"newField\":1}}}";
        var acc = LiveMetricAccumulator.Resume(withFuture, Recording, 10);
        Events(acc, 1);
        var d = acc.PrepareSave(20, T0, "A")!;
        Assert.Contains("\"futureFamily\":{\"revision\":3,\"sinceTick\":0,\"throughTick\":10,\"continuous\":true,\"metrics\":{\"z\":{\"n\":7}},\"active\":{},\"newField\":1}", d.Json);

        var future = "{\"formatVersion\":7,\"checkpointId\":\"F\",\"whatever\":[1]}";
        var opaque = LiveMetricAccumulator.Resume(future, Recording, 10);
        Assert.Null(opaque.Family("test"));     // nothing new is recorded on top of data this version cannot read
        Assert.Null(opaque.Loaded);
        var kept = opaque.PrepareSave(20, T0, "B")!;
        Assert.Equal((future, false), (kept.Json, kept.IsNew));

        var garbage = LiveMetricAccumulator.Resume("{oops", Recording, 10);
        Assert.Null(garbage.Family("test"));
        Assert.Equal("{oops", garbage.PrepareSave(20, T0, "C")!.Json);
    }

    [Fact]
    public void NoRecordingFamily_WritesNothingOrTheLoadedCheckpointUnchanged()
    {
        Assert.Null(LiveMetricAccumulator.StartNew(Array.Empty<FamilySpec>(), 0).PrepareSave(10, T0, "X"));
        Assert.Null(LiveMetricAccumulator.Resume(null, Array.Empty<FamilySpec>(), 10).PrepareSave(20, T0, "X"));

        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 2);
        var p = Save(acc, "P", 100);
        var off = LiveMetricAccumulator.Resume(p.Json, Array.Empty<FamilySpec>(), 100);
        var d = off.PrepareSave(900, T0, "X")!;
        Assert.Equal((p.Json, false), (d.Json, d.IsNew));
        off.Commit(d);
        Assert.Equal("P", off.ParentId);
    }

    [Fact]
    public void Draft_IsDetachedFromLaterEvents()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 2);
        var d = acc.PrepareSave(100, T0, "P")!;
        Events(acc, 5);
        Assert.Equal(2, d.Payload!.Families["test"].Metrics["events"].N);
        Assert.Equal(2, CheckpointJson.Parse(d.Json).Payload!.Families["test"].Metrics["events"].N);
        Assert.Equal(7, acc.Family("test")!.Metrics["events"].N);
    }

    [Fact]
    public void Stop_KeepsWhatWasRecordedThroughTheStop()
    {
        var acc = LiveMetricAccumulator.StartNew(new[] { Dummy.Spec, new FamilySpec("other", 1) }, 0);
        Events(acc, 2);
        acc.Stop("test", 400);
        Assert.Null(acc.Family("test"));
        var d = acc.PrepareSave(1000, T0, "P")!;
        Assert.Equal(400, d.Payload!.Families["test"].ThroughTick);
        Assert.Equal(MetricCoverage.Partial, Obs(d).Scalar("test.count", 1)!.Coverage);
        Assert.Equal(2, Count(Obs(d)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Stop_LastFamilyIsSavedEvenIfAnEarlierDraftCommits(bool commitEarlierDraft)
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 2);
        var earlier = acc.PrepareSave(100, T0, "P")!;
        Events(acc, 3);
        acc.Stop("test", 400);
        if (commitEarlierDraft)
            acc.Commit(earlier);

        var retry = acc.PrepareSave(900, T0, "Retry")!;
        Assert.NotNull(retry);
        Assert.True(retry.IsNew);
        Assert.Equal(5, Count(Obs(retry)));

        // An unsuccessful save of the stopped totals leaves them pending.
        var saved = Save(acc, "S", 1000);
        Assert.Equal(commitEarlierDraft ? "P" : null, saved.Payload!.ParentCheckpointId);
        Assert.Equal(400, saved.Payload.Families["test"].ThroughTick);
        Assert.Equal(5, Count(Obs(saved)));
        var unchanged = acc.PrepareSave(1100, T0, "Next")!;
        Assert.False(unchanged.IsNew);
        Assert.Equal(saved.Json, unchanged.Json);
    }

    /// <summary>Codex review: an interval open before a gap must not become a completed sample after it.</summary>
    [Fact]
    public void Gap_DropsOpenIntervalsButKeepsTotals()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 2);
        acc.Family("test")!.Track("open", "x", 50);
        var p = Save(acc, "P", 100);
        var continued = LiveMetricAccumulator.Resume(p.Json, Recording, 100);
        Assert.Equal(50, continued.Family("test")!.Tracked("open", "x"));   // no gap: the interval goes on
        var later = LiveMetricAccumulator.Resume(p.Json, Recording, 900);   // loaded at another tick: a gap
        Assert.Empty(later.Family("test")!.Active);
        Assert.Equal(2, later.Family("test")!.Metrics["events"].N);
        var missed = LiveMetricAccumulator.StartNew(Recording, 0);
        missed.Family("test")!.Track("open", "y", 10);
        missed.MarkGap("test");
        Assert.Empty(missed.Family("test")!.Active);
    }

    [Fact]
    public void MarkGap_AMissedEventMakesTheFamilyPartialForGood()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 1);
        acc.MarkGap("test");
        acc.MarkGap("not-recording");   // ignored
        var d = Save(acc, "P", 100);
        Assert.False(d.Payload!.Families["test"].Continuous);
        Assert.Equal(MetricCoverage.Partial, Obs(d).Scalar("test.count", 1)!.Coverage);
        Assert.False(LiveMetricAccumulator.Resume(d.Json, Recording, 100).PrepareSave(200, T0, "A")!.Payload!.Families["test"].Continuous);
    }

    // ----- the save protocol -----

    private static int idSeq;
    private static Func<string> Ids(string prefix) => () => prefix + Interlocked.Increment(ref idSeq);

    [Fact]
    public void Protocol_SuccessfulSave_CommitsAndBecomesTheParent()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var session = new CheckpointSaveSession(acc);
        Events(acc, 1);
        session.Enter("savegame", 1, true, 100, T0, () => "A");
        var text = session.TextToWrite(1);
        Assert.NotNull(text);
        var draft = session.Complete("savegame", 1, runOriginal: true, fileExists: true);
        session.Exit("savegame", 1);
        Assert.NotNull(draft);
        Assert.Equal(text, draft!.Json);
        Assert.Equal("A", acc.ParentId);
    }

    [Theory]
    [InlineData(false, true, true)]    // another mod skipped the original save
    [InlineData(true, false, true)]    // the file is not there after the save
    [InlineData(true, true, false)]    // the component did not write (ExposeData not reached)
    public void Protocol_UnconfirmedSave_CommitsNothing(bool runOriginal, bool fileExists, bool exposed)
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var session = new CheckpointSaveSession(acc);
        Events(acc, 1);
        session.Enter("savegame", 1, true, 100, T0, () => "A");
        if (exposed)
            session.TextToWrite(1);
        Assert.Null(session.Complete("savegame", 1, runOriginal, fileExists));
        session.Exit("savegame", 1);
        Assert.Null(acc.ParentId);
    }

    [Fact]
    public void Protocol_FailedSave_KeepsEventsForTheNextSave()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var session = new CheckpointSaveSession(acc);
        Events(acc, 2);
        session.Enter("savegame", 1, true, 100, T0, Ids("P"));
        session.TextToWrite(1);
        var p = session.Complete("savegame", 1, true, true)!;
        session.Exit("savegame", 1);

        Events(acc, 3);
        session.Enter("savegame", 1, true, 200, T0, () => "F");   // throws while writing: no postfix
        session.TextToWrite(1);
        session.Exit("savegame", 1);
        Assert.Equal(p.Payload!.CheckpointId, acc.ParentId);

        session.Enter("savegame", 1, true, 300, T0, () => "S");
        session.TextToWrite(1);
        var s = session.Complete("savegame", 1, true, true)!;
        session.Exit("savegame", 1);
        Assert.Equal(p.Payload.CheckpointId, s.Payload!.ParentCheckpointId);
        Assert.Equal(5, Count(Obs(s)));
    }

    [Fact]
    public void Protocol_LaterPostfixThrowing_DoesNotUndoTheCommit()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var session = new CheckpointSaveSession(acc);
        session.Enter("savegame", 1, true, 100, T0, () => "A");
        session.TextToWrite(1);
        Assert.NotNull(session.Complete("savegame", 1, true, true));
        session.Exit("savegame", 1);   // the finalizer sees another mod's exception and passes it on
        Assert.Equal("A", acc.ParentId);
    }

    [Fact]
    public void Protocol_NestedOrForeignThreadSave_WritesTheLastCheckpointAndCommitsNothing()
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var session = new CheckpointSaveSession(acc);
        session.Enter("savegame", 1, true, 100, T0, () => "P");
        var p = session.TextToWrite(1);
        session.Complete("savegame", 1, true, true);
        session.Exit("savegame", 1);

        Events(acc, 1);
        session.Enter("savegame", 1, true, 200, T0, () => "OUTER");
        session.Enter("savegame", 1, true, 200, T0, () => "INNER");
        Assert.Equal(p, session.TextToWrite(1));
        Assert.Null(session.Complete("savegame", 1, true, true));
        session.Exit("savegame", 1);
        Assert.Null(session.Complete("savegame", 1, true, true));
        session.Exit("savegame", 1);
        Assert.Equal("P", acc.ParentId);

        session.Enter("savegame", 7, false, 300, T0, () => "BG");
        Assert.Equal(p, session.TextToWrite(7));
        Assert.Null(session.Complete("savegame", 7, true, true));
        session.Exit("savegame", 7);
        Assert.Equal("P", acc.ParentId);

        session.Enter("savedscenario", 1, true, 300, T0, () => "SCEN");
        Assert.Null(session.Complete("savedscenario", 1, true, true));
        session.Exit("savedscenario", 1);
        session.Enter("savegame", 1, true, 400, T0, () => "NEXT");
        session.TextToWrite(1);
        Assert.Equal("NEXT", session.Complete("savegame", 1, true, true)!.Payload!.CheckpointId);
        session.Exit("savegame", 1);
    }

    // ----- profile records, lineage and the representative -----

    /// <summary>P has 3; A continues P with +2; B is P reloaded with +1.</summary>
    private static (MetricObservation P, MetricObservation A, MetricObservation B) Branches(int tickA, int tickB, int minuteA = 1, int minuteB = 2)
    {
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        Events(acc, 3);
        var p = Save(acc, "P", 1000);
        Events(acc, 2);
        var a = Save(acc, "A", tickA, minuteA);
        var reloaded = LiveMetricAccumulator.Resume(p.Json, Recording, 1000);
        Events(reloaded, 1);
        var b = Save(reloaded, "B", tickB, minuteB);
        return (Obs(p), Obs(a), Obs(b));
    }

    [Theory]
    [InlineData(2000, 1500, 5)]
    [InlineData(1500, 2000, 4)]
    public void Branches_ShowOneLineageNeverASum(int tickA, int tickB, long expected)
    {
        var (p, a, b) = Branches(tickA, tickB);
        var profile = ProfileWithColony();
        foreach (var o in new[] { p, a, b })
            ProfileMutations.UpsertMetricObservation(profile, o);
        var sel = CheckpointSelector.Select(profile.MetricObservations);
        Assert.Equal(expected, Count(sel.Representative));
        Assert.Equal(CheckpointLineage.Branched, sel.Lineage);
        Assert.Equal(3, sel.Checkpoints);
        Assert.Equal(expected, Overall(profile).Value.Number);   // never 9 (A+B) or 6 (A+B−P)
    }

    [Fact]
    public void Branches_SameTick_FallBackToSaveTimeThenId()
    {
        var (p, a, b) = Branches(2000, 2000, minuteA: 5, minuteB: 9);
        Assert.Equal(4, Count(CheckpointSelector.Select(new[] { p, a, b }).Representative));
        var (p2, a2, b2) = Branches(2000, 2000, minuteA: 5, minuteB: 5);
        Assert.Equal("A", CheckpointSelector.Select(new[] { b2, p2, a2 }).Representative!.Key);
    }

    [Fact]
    public void ReimportAndCopies_AddNothing()
    {
        var (p, a, b) = Branches(2000, 1500);
        var profile = ProfileWithColony();
        foreach (var o in new[] { p, a, b, a, b, p })
            ProfileMutations.UpsertMetricObservation(profile, o);
        var aliasCopy = CloneWithAlias(a, "Copy of A.rws");
        ProfileMutations.UpsertMetricObservation(profile, aliasCopy);
        Assert.Equal(3, profile.MetricObservations.Count);
        Assert.Contains(profile.MetricObservations.Single(o => o.Key == "A").FileAliases, x => x.FileName == "Copy of A.rws");
        Assert.Equal(5, Overall(profile).Value.Number);
    }

    private static MetricObservation CloneWithAlias(MetricObservation o, string file)
    {
        var c = JsonSerializer.Deserialize<MetricObservation>(JsonSerializer.Serialize(o, ProfileJson.Options), ProfileJson.Options)!;
        c.FileAliases.Add(new MetricFileAlias { FileName = file, FileSize = 10, LastWriteUtc = T0 });
        return c;
    }

    [Fact]
    public void SameIdOtherContent_IsSetAsideAndTheLineageUnknown()
    {
        var (p, a, _) = Branches(2000, 1500);
        var forged = CloneWithAlias(a, "other.rws");
        forged.PayloadHash = "0000";
        forged.Metrics["test.count"] = MetricEntry.Of(1, new MetricValue { Coverage = MetricCoverage.Complete, IntegerValue = 999 });
        var profile = ProfileWithColony();
        ProfileMutations.UpsertMetricObservation(profile, p);
        ProfileMutations.UpsertMetricObservation(profile, a);
        ProfileMutations.UpsertMetricObservation(profile, forged);
        var stored = profile.MetricObservations.Single(o => o.Key == "A");
        Assert.True(stored.Conflicted);
        var sel = CheckpointSelector.Select(profile.MetricObservations);
        Assert.Equal(CheckpointLineage.Unknown, sel.Lineage);
        Assert.NotEqual(999, Count(sel.Representative));
        Assert.NotEqual("A", sel.Representative?.Key);
    }

    [Fact]
    public void Ancestors_AreCompressedOnlyOnceALaterChildExists_InAnyOrder()
    {
        var (p, a, b) = Branches(2000, 1500);
        var profile = ProfileWithColony();
        ProfileMutations.UpsertMetricObservation(profile, p);
        Assert.False(profile.MetricObservations.Single(o => o.Key == "P").Compressed);
        ProfileMutations.UpsertMetricObservation(profile, b);
        var stored = profile.MetricObservations.Single(o => o.Key == "P");
        Assert.True(stored.Compressed);
        Assert.NotEmpty(stored.Metrics);   // keep ancestor data available if the child later conflicts
        Assert.Equal((1000, (string?)null), (stored.SaveTick, stored.ParentKey));

        var reverse = ProfileWithColony();
        ProfileMutations.UpsertMetricObservation(reverse, a);
        ProfileMutations.UpsertMetricObservation(reverse, p);   // the parent arrives after its child
        Assert.True(reverse.MetricObservations.Single(o => o.Key == "P").Compressed);
        Assert.Equal(5, Overall(reverse).Value.Number);
        Assert.Equal(CheckpointLineage.Linear, CheckpointSelector.Select(reverse.MetricObservations).Lineage);

        // A child saved at the parent's own tick does not compress it: the time and id still decide between them.
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var q = Obs(Save(acc, "Q", 100));
        var q2 = Obs(Save(acc, "Q2", 100));
        var same = ProfileWithColony();
        ProfileMutations.UpsertMetricObservation(same, q);
        ProfileMutations.UpsertMetricObservation(same, q2);
        Assert.False(same.MetricObservations.Single(o => o.Key == "Q").Compressed);
    }

    [Fact]
    public void ConflictedChild_RestoresAncestorMetricsEvenWhenOriginalSaveCannotBeRead()
    {
        var (parent, child, _) = Branches(2000, 1500);
        var profile = ProfileWithColony();
        ProfileMutations.UpsertMetricObservation(profile, parent);
        ProfileMutations.UpsertMetricObservation(profile, child);
        Assert.True(profile.MetricObservations.Single(o => o.Key == "P").Compressed);

        // The original parent save no longer exists; no reread is possible.
        var conflictingChild = CloneWithAlias(child, "conflict.rws");
        conflictingChild.PayloadHash = "different-content";
        ProfileMutations.UpsertMetricObservation(profile, conflictingChild);

        var restoredParent = profile.MetricObservations.Single(o => o.Key == "P");
        Assert.False(restoredParent.Compressed);
        Assert.Equal(3, Count(CheckpointSelector.Select(profile.MetricObservations).Representative));
        Assert.Equal(3d, Overall(profile).Value.Number);
    }

    /// <summary>Codex review: a conflicted child must not leave its compressed ancestor unusable.</summary>
    [Fact]
    public void ConflictedChild_UncompressesItsAncestor_AndARereadRestoresIt()
    {
        var (p, a, _) = Branches(2000, 1500);
        var reread = CloneWithAlias(p, "P again.rws");   // what a later read of P's file builds (a fresh observation)
        var profile = ProfileWithColony();
        ProfileMutations.UpsertMetricObservation(profile, p);
        ProfileMutations.UpsertMetricObservation(profile, a);
        Assert.True(profile.MetricObservations.Single(o => o.Key == "P").Compressed);
        var forged = CloneWithAlias(a, "other.rws");
        forged.PayloadHash = "0000";
        ProfileMutations.UpsertMetricObservation(profile, forged);
        var stored = profile.MetricObservations.Single(o => o.Key == "P");
        Assert.False(stored.Compressed);   // its only later child is set aside
        ProfileMutations.UpsertMetricObservation(profile, reread);   // re-read, or recovered when P is loaded
        Assert.Equal(3, Count(CheckpointSelector.Select(profile.MetricObservations).Representative));
        Assert.Equal(3, Overall(profile).Value.Number);
    }

    /// <summary>Codex review: a recorded 0 of a DLC output in an observation without that DLC is "not applicable".</summary>
    [Fact]
    public void DlcOutputs_ZeroWithoutTheDlcIsNotApplicable_ButRecordedValuesShow()
    {
        var def = CountDef with { Key = "test.dlc", StoredKey = "test.count", RequiredDlc = AcquisitionUnits.Biotech };
        NumericRow Row(long n, List<string> dlcs)
        {
            var acc = LiveMetricAccumulator.StartNew(Recording, 0);
            Events(acc, (int)n);
            var d = Save(acc, "P" + n + dlcs.Count, 100);
            var obs = CheckpointMetrics.ToObservation(d.Payload!, d.Json, Identity(), null, dlcs, Families);
            var profile = ProfileWithColony();
            ProfileMutations.UpsertMetricObservation(profile, obs);
            var colonies = CampaignMatcher.BuildColonies(profile);
            return MetricCalculator.BuildView(profile, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()), new[] { def })
                .Overall.Single(r => r.Definition.Key == "test.dlc");
        }
        var never = Row(0, new List<string>());
        Assert.Equal((MetricCoverage.Unknown, 0), (never.Value.Coverage, never.Targets));
        var recorded = Row(2, new List<string>());
        Assert.Equal((2.0, 1), (recorded.Value.Number!.Value, recorded.Targets));
        var active = Row(0, new List<string> { AcquisitionUnits.Biotech });
        Assert.Equal((0.0, 1), (active.Value.Number!.Value, active.Targets));
    }

    [Fact]
    public void Lineage_UnknownParentRootsAndCycles()
    {
        MetricObservation Node(string key, string? parent, int tick) => new()
        {
            Source = MetricObservationSource.Checkpoint, Key = key, ParentKey = parent, SaveTick = tick, ObservedUtc = T0,
            Identity = Identity(), PayloadHash = key,
        };
        Assert.Equal(CheckpointLineage.None, CheckpointSelector.Select(Array.Empty<MetricObservation>()).Lineage);
        Assert.Equal(CheckpointLineage.Linear, CheckpointSelector.Select(new[] { Node("A", null, 1), Node("B", "A", 2), Node("C", "B", 3) }).Lineage);
        Assert.Equal(CheckpointLineage.Unknown, CheckpointSelector.Select(new[] { Node("B", "missing", 2), Node("C", "B", 3) }).Lineage);
        Assert.Equal(CheckpointLineage.Unknown, CheckpointSelector.Select(new[] { Node("A", null, 1), Node("Z", null, 2) }).Lineage);
        Assert.Equal(CheckpointLineage.Unknown, CheckpointSelector.Select(new[] { Node("A", "B", 1), Node("B", "A", 2) }).Lineage);
        Assert.Equal(CheckpointLineage.Branched, CheckpointSelector.Select(new[] { Node("A", null, 1), Node("B", "A", 2), Node("C", "A", 3) }).Lineage);
        // Other sources never take part.
        var file = Node("F", null, 9);
        file.Source = MetricObservationSource.SaveFile;
        Assert.Equal(1, CheckpointSelector.Select(new[] { Node("A", null, 1), file }).Checkpoints);
    }

    [Fact]
    public void FailedThenOtherSave_FormTwoSaveBranches_LoadingFContinuesFromF()
    {
        // P → F (replaced, then SafeSaver threw: never committed) → S saved without loading: both children of P.
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var session = new CheckpointSaveSession(acc);
        Events(acc, 1);
        session.Enter("savegame", 1, true, 100, T0, () => "P");
        session.TextToWrite(1);
        var p = session.Complete("savegame", 1, true, true)!;
        session.Exit("savegame", 1);
        Events(acc, 1);
        session.Enter("savegame", 1, true, 200, T0.AddMinutes(1), () => "F");
        var fText = session.TextToWrite(1)!;
        session.Exit("savegame", 1);
        session.Enter("savegame", 1, true, 300, T0.AddMinutes(2), () => "S");
        session.TextToWrite(1);
        var s = session.Complete("savegame", 1, true, true)!;
        session.Exit("savegame", 1);

        var f = CheckpointJson.Parse(fText).Payload!;
        Assert.Equal(("P", "P"), (f.ParentCheckpointId, s.Payload!.ParentCheckpointId));
        var profile = ProfileWithColony();
        ProfileMutations.UpsertMetricObservation(profile, Obs(p));
        ProfileMutations.UpsertMetricObservation(profile, Obs(s));
        ProfileMutations.UpsertMetricObservation(profile, CheckpointMetrics.ToObservation(f, fText, Identity(), null, null, Families));   // imported later
        var sel = CheckpointSelector.Select(profile.MetricObservations);
        Assert.Equal((CheckpointLineage.Branched, "S", 2L), (sel.Lineage, sel.Representative!.Key, Count(sel.Representative)));

        // Loading F itself: F is recovered (idempotent) and the next save's parent is F.
        var fromF = LiveMetricAccumulator.Resume(fText, Recording, 200);
        Assert.Equal("F", fromF.ParentId);
        Events(fromF, 4);
        var t = Save(fromF, "T", 400);
        Assert.Equal("F", t.Payload!.ParentCheckpointId);
        Assert.Equal(6, Count(Obs(t)));
    }

    [Fact]
    public void ProfileJson_RoundTripsTheCheckpointFields()
    {
        var (p, a, _) = Branches(2000, 1500);
        var profile = ProfileWithColony();
        ProfileMutations.UpsertMetricObservation(profile, a);
        ProfileMutations.UpsertMetricObservation(profile, p);
        var json = ProfileJson.Serialize(profile);
        var back = ProfileJson.Deserialize(json);
        var ba = back.MetricObservations.Single(o => o.Key == "A");
        Assert.Equal((MetricObservationSource.Checkpoint, "P", a.PayloadHash, false), (ba.Source, ba.ParentKey, ba.PayloadHash, ba.Compressed));
        Assert.True(back.MetricObservations.Single(o => o.Key == "P").Compressed);
        Assert.Equal(json, ProfileJson.Serialize(back));
        Assert.DoesNotContain("\"conflicted\"", json);
    }

    // ----- import -----

    private static byte[] SaveWith(string? numericStatistics, int ticks = 2000)
    {
        var children = new List<XElement> { new("campaignId", Campaign) };
        if (numericStatistics != null)
        {
            children.Add(new XElement("dataVersion", 2));
            children.Add(new XElement("numericStatistics", numericStatistics));
        }
        return new SaveXmlBuilder().Minimal().Tick(ticks).Component(SaveReader.ComponentClassName, children.ToArray()).Build();
    }

    private static SaveReadResult Read(byte[] bytes, string name = "A.rws")
        => new SaveReader().ReadDetailed(new MemoryStream(bytes), name, bytes.Length, T0);

    [Fact]
    public void Reader_IsVersion15AndReadsTheEmbeddedCheckpoint()
    {
        Assert.Equal(16, SaveReader.Version);
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var d = acc.PrepareSave(2000, T0, "A")!;
        var r = Read(SaveWith(d.Json));
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        var c = r.Checkpoint!;
        Assert.Equal((MetricObservationSource.Checkpoint, "A", (string?)null, 2000, CheckpointJson.Hash(d.Json)), (c.Source, c.Key, c.ParentKey, c.SaveTick, c.PayloadHash));
        Assert.Equal(Campaign, c.Identity.ModCampaignId);
        Assert.Contains(c.FileAliases, x => x.FileName == "A.rws");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("{\"formatVersion\":3,\"checkpointId\":\"X\"}")]
    public void Reader_WithoutAReadableCheckpoint_KeepsTheV1Read(string? text)
    {
        var r = Read(SaveWith(text));
        Assert.Equal(ObservationStatus.Ok, r.Observation.Status);
        Assert.Equal(Campaign, r.Observation.Identity.ModCampaignId);
        Assert.Null(r.Checkpoint);
        Assert.NotNull(r.Metrics);
    }

    [Fact]
    public void Import_RecoversCheckpointsOnceAcrossCopies()
    {
        using var saves = new TempDir();
        using var store = new TempDir();
        var acc = LiveMetricAccumulator.StartNew(Recording, 0);
        var d = acc.PrepareSave(2000, T0, "A")!;
        var bytes = SaveWith(d.Json);
        File.WriteAllBytes(Path.Combine(saves.Path, "A.rws"), bytes);
        File.WriteAllBytes(Path.Combine(saves.Path, "Copy.rws"), bytes);
        var session = new ProfileSession(new ProfileStore(store.Path), () => T0);
        new ImportEngine(new SaveReader()).Run(saves.Path, session, null, CancellationToken.None);
        new ImportEngine(new SaveReader()).Run(saves.Path, session, null, CancellationToken.None);
        var checkpoints = session.Read(p => p.MetricObservations.Where(o => o.Source == MetricObservationSource.Checkpoint).ToList());
        Assert.Single(checkpoints);
        Assert.Equal(new[] { "A.rws", "Copy.rws" }, checkpoints[0].FileAliases.Select(a => a.FileName).Order(StringComparer.Ordinal));
    }
}
