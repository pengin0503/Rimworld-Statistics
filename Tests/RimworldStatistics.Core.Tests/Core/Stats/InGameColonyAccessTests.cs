namespace RimworldStatistics.Core.Tests;

public sealed class InGameColonyAccessTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<ProfileSession> _sessions = new();

    public InGameColonyAccessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "rws-ingame-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var s in _sessions)
        {
            try { s.Close(); }
            catch { /* ignore */ }
        }
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Ignore best-effort temp cleanup errors.
        }
    }

    private ProfileSession CreateSession()
    {
        var session = new ProfileSession(new ProfileStore(_tempDir), () => new DateTime(2026, 11, 18, 0, 0, 0, DateTimeKind.Utc));
        _sessions.Add(session);
        return session;
    }

    private static ColonyIdentity MakeIdentity(
        string? campaignId,
        string seed,
        int randomValue,
        string tile,
        int startTick,
        float firstWealth,
        params string[] pawns) => new()
    {
        ModCampaignId = campaignId,
        SeedString = seed,
        PersistentRandomValue = randomValue,
        StartingTile = tile,
        GameStartAbsTick = startTick,
        FirstWealthBits = BitConverter.SingleToUInt32Bits(firstWealth),
        StartingPawnIds = pawns.ToList(),
    };

    private static DisplayInfo MakeDisplay(string? factionName, string worldName = "Terra") => new()
    {
        FactionName = factionName,
        WorldName = worldName,
        Scenario = "Crashlanded",
        Storyteller = "Cassandra",
        Difficulty = "Rough",
        Permadeath = false,
    };

    private static ObservedValues MakeValues(int ticks, double wealthLast = 25_000, double wealthMax = 30_000, int raids = 4) => new()
    {
        TicksGame = ticks,
        RealPlayTime = ticks / 60.0,
        NumRaidsEnemy = raids,
        NumThreatsQueued = raids + 1,
        ColonistsKilled = 1,
        GreatestPopulation = 6,
        WealthLast = wealthLast,
        WealthMax = wealthMax,
        WealthPoints = 3,
    };

    private static StatsView BuildView(ProfileSession session)
        => session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits, ViewScope.All);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TitleScreenOrMissingCampaignId_ResolvesUnrecordedWithoutMutatingProfile(string? campaignId)
    {
        var session = CreateSession();
        var id = MakeIdentity("camp_existing", "seed-a", 101, "12,0", 1000, 12000f, "Thing_Human1");
        session.Mutate(p =>
        {
            ProfileMutations.ApplyLiveObservation(
                p, "camp_existing", id, MakeValues(180_000), MakeDisplay("Existing Colony"),
                new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc), "1.7.4985 rev1269");
        }, SaveMode.None);

        var view = BuildView(session);
        var revisionBefore = session.Revision;
        var keyCountBefore = session.Read(p => p.ColonyKeys.Count);
        var liveCountBefore = session.Read(p => p.LiveCampaigns.Count);

        var result = InGameColonyAccess.Resolve(view, campaignId);

        Assert.False(result.Found);
        Assert.Null(result.ColonyKey);
        Assert.Equal(RecordedPointInfo.None, result.RecordedPoint);
        Assert.Equal(revisionBefore, session.Revision);
        Assert.Equal(keyCountBefore, session.Read(p => p.ColonyKeys.Count));
        Assert.Equal(liveCountBefore, session.Read(p => p.LiveCampaigns.Count));
    }

    [Fact]
    public void NewColony_BeforeFirstSaveIsUnrecorded_ThenAfterSaveResolvesSummaryAndRecordedPoint()
    {
        var session = CreateSession();
        const string campaignId = "camp_new_playthrough";
        var initialIdentity = MakeIdentity(campaignId, "seed-fresh", 202, "44,0", 500, 9500f, "Thing_Human10", "Thing_Human11");
        var display = MakeDisplay("Sunrise Outpost", "BorealWorld");

        // StartedNewGame registers the live campaign without any saved observation.
        session.Mutate(p => ProfileMutations.RegisterLiveCampaign(p, campaignId, initialIdentity, display), SaveMode.None);

        var beforeSaveView = BuildView(session);
        var beforeSave = InGameColonyAccess.Resolve(beforeSaveView, campaignId);
        Assert.False(beforeSave.Found);
        Assert.Null(beforeSave.ColonyKey);
        Assert.False(beforeSave.RecordedPoint.HasObservedPoint);
        Assert.Null(beforeSave.RecordedPoint.LastRecordedUtc);
        Assert.Null(beforeSave.RecordedPoint.LastRecordedTicks);

        // RecordSave applies the live observation on the first save.
        var savedUtc = new DateTime(2026, 11, 18, 4, 30, 0, DateTimeKind.Utc);
        const int savedTicks = 360_000;
        session.MutateWithoutBlocking(p =>
            ProfileMutations.ApplyLiveObservation(
                p, campaignId, initialIdentity, MakeValues(savedTicks, wealthLast: 42_000, wealthMax: 48_000, raids: 7),
                display, savedUtc, "1.7.4985 rev1269"));

        var afterSaveView = BuildView(session);
        var afterSave = InGameColonyAccess.Resolve(afterSaveView, campaignId);
        Assert.True(afterSave.Found);
        Assert.NotNull(afterSave.ColonyKey);
        Assert.True(afterSave.RecordedPoint.HasObservedPoint);
        Assert.Equal(savedUtc, afterSave.RecordedPoint.LastRecordedUtc);
        Assert.Equal(savedTicks, afterSave.RecordedPoint.LastRecordedTicks);

        var summary = session.ReadColonySummaryReport(afterSaveView, afterSave.ColonyKey!);
        Assert.NotNull(summary);
        Assert.Equal("Sunrise Outpost", summary.Overview.FactionName);
        Assert.Equal("BorealWorld", summary.Overview.WorldName);
    }

    [Fact]
    public void SameFactionName_UnsavedNewColonyNeverMatchesExistingSavedColony()
    {
        var session = CreateSession();
        var savedId = MakeIdentity("camp_saved", "seed-1", 11, "5,0", 100, 10_000f, "Thing_Human1");
        var unsavedId = MakeIdentity("camp_unsaved_same_name", "seed-2", 22, "8,0", 200, 11_000f, "Thing_Human9");

        session.Mutate(p =>
        {
            ProfileMutations.ApplyLiveObservation(
                p, "camp_saved", savedId, MakeValues(600_000), MakeDisplay("New Hope", "Alpha"),
                new DateTime(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc), "1.7.4985 rev1269");
            ProfileMutations.RegisterLiveCampaign(
                p, "camp_unsaved_same_name", unsavedId, MakeDisplay("New Hope", "Alpha"));
        }, SaveMode.None);

        var view = BuildView(session);
        Assert.Single(view.Colonies);

        var unsavedResolution = InGameColonyAccess.Resolve(view, "camp_unsaved_same_name");
        Assert.False(unsavedResolution.Found);
        Assert.Null(unsavedResolution.ColonyKey);

        var savedResolution = InGameColonyAccess.Resolve(view, "camp_saved");
        Assert.True(savedResolution.Found);
        Assert.Equal(view.Colonies[0].Key, savedResolution.ColonyKey);
    }

    [Fact]
    public void SameFactionName_TwoSavedColoniesEvenWithCollidingFingerprintResolveStrictlyByCampaignId()
    {
        var session = CreateSession();
        // Same world seed/tile/startTick/wealth fingerprint, disjoint starting pawns -> fp:<hash> and fp:<hash>#2,
        // and identical faction name "New Hope".
        var id1 = MakeIdentity("camp_one", "shared-seed", 777, "19,0", 0, 14_000f, "Thing_Human1", "Thing_Human2");
        var id2 = MakeIdentity("camp_two", "shared-seed", 777, "19,0", 0, 14_000f, "Thing_Human3", "Thing_Human4");
        var utc1 = new DateTime(2026, 11, 15, 8, 0, 0, DateTimeKind.Utc);
        var utc2 = new DateTime(2026, 11, 17, 19, 45, 0, DateTimeKind.Utc);

        session.Mutate(p =>
        {
            ProfileMutations.ApplyLiveObservation(
                p, "camp_one", id1, MakeValues(240_000, wealthLast: 30_000, wealthMax: 35_000, raids: 3),
                MakeDisplay("New Hope", "TwinWorld"), utc1, "1.7.4985 rev1269");
            ProfileMutations.ApplyLiveObservation(
                p, "camp_two", id2, MakeValues(900_000, wealthLast: 85_000, wealthMax: 92_000, raids: 14),
                MakeDisplay("New Hope", "TwinWorld"), utc2, "1.7.4985 rev1269");
        }, SaveMode.None);

        var view = BuildView(session);
        Assert.Equal(2, view.Colonies.Count);

        var res1 = InGameColonyAccess.Resolve(view, "camp_one");
        var res2 = InGameColonyAccess.Resolve(view, "camp_two");

        Assert.True(res1.Found);
        Assert.True(res2.Found);
        Assert.NotEqual(res1.ColonyKey, res2.ColonyKey);
        Assert.Equal(utc1, res1.RecordedPoint.LastRecordedUtc);
        Assert.Equal(240_000, res1.RecordedPoint.LastRecordedTicks);
        Assert.Equal(utc2, res2.RecordedPoint.LastRecordedUtc);
        Assert.Equal(900_000, res2.RecordedPoint.LastRecordedTicks);

        var report1 = session.ReadColonySummaryReport(view, res1.ColonyKey!)!;
        var report2 = session.ReadColonySummaryReport(view, res2.ColonyKey!)!;
        var days1 = report1.KeyRecords.Single(r => r.MetricKey == BasicMetrics.Days).Value.Number!.Value;
        var days2 = report2.KeyRecords.Single(r => r.MetricKey == BasicMetrics.Days).Value.Number!.Value;
        Assert.Equal(240_000 / (double)StatFormat.TicksPerDay, days1, precision: 5);
        Assert.Equal(900_000 / (double)StatFormat.TicksPerDay, days2, precision: 5);
    }

    [Fact]
    public void SwitchingBetweenSavesImportedColonyAndTitleScreen_ResolvesEachStateIndependently()
    {
        var session = CreateSession();
        var liveIdentity = MakeIdentity("camp_live", "seed-live", 301, "9,0", 0, 15_000f, "Thing_Human1");
        var importedIdentity = MakeIdentity(null, "seed-imported", 402, "21,0", 0, 18_500f, "Thing_Human50");
        var liveUtc = new DateTime(2026, 11, 17, 6, 15, 0, DateTimeKind.Utc);
        var importedUtc = new DateTime(2026, 11, 10, 22, 10, 0, DateTimeKind.Utc);

        session.Mutate(p =>
        {
            // Colony A: recorded via live save.
            ProfileMutations.ApplyLiveObservation(
                p, "camp_live", liveIdentity, MakeValues(420_000), MakeDisplay("Bastion"), liveUtc, "1.7.4985 rev1269");

            // Colony B: imported from a pre-install save file on title screen, then loaded in-game (LoadedGame assigns camp_from_load
            // and calls RegisterLiveCampaign without re-saving yet).
            p.SaveObservations.Add(new SaveObservation
            {
                FileName = "OldSave.rws",
                FileSize = 123_456,
                LastWriteUtc = importedUtc,
                Identity = importedIdentity.Clone(),
                Values = MakeValues(780_000),
                Display = MakeDisplay("Legacy Haven"),
                GameVersion = "1.7.4985 rev1269",
            });
            var loadedProbe = importedIdentity.Clone();
            loadedProbe.ModCampaignId = "camp_from_load";
            ProfileMutations.RegisterLiveCampaign(p, "camp_from_load", loadedProbe, MakeDisplay("Legacy Haven"));

            // Colony C: switched to a brand-new save that hasn't been saved yet.
            var unsaved = MakeIdentity("camp_unsaved", "seed-unsaved", 503, "33,0", 0, 9_000f, "Thing_Human99");
            ProfileMutations.RegisterLiveCampaign(p, "camp_unsaved", unsaved, MakeDisplay("Transient"));
        }, SaveMode.None);

        var view = BuildView(session);

        // 1. Playing Colony A
        var stateA = InGameColonyAccess.Resolve(view, "camp_live");
        Assert.True(stateA.Found);
        Assert.Equal(liveUtc, stateA.RecordedPoint.LastRecordedUtc);
        Assert.Equal(420_000, stateA.RecordedPoint.LastRecordedTicks);

        // 2. Switch directly to Colony B (loaded pre-install save, matched via fingerprint to imported observation)
        var stateB = InGameColonyAccess.Resolve(view, "camp_from_load");
        Assert.True(stateB.Found);
        Assert.NotEqual(stateA.ColonyKey, stateB.ColonyKey);
        Assert.Equal(importedUtc, stateB.RecordedPoint.LastRecordedUtc);
        Assert.Equal(780_000, stateB.RecordedPoint.LastRecordedTicks);

        // 3. Switch to unsaved Colony C
        var stateC = InGameColonyAccess.Resolve(view, "camp_unsaved");
        Assert.False(stateC.Found);

        // 4. Return to title screen (CurrentOrNull is null -> campaignId is null)
        var titleState = InGameColonyAccess.Resolve(view, null);
        Assert.False(titleState.Found);
    }

    [Fact]
    public void BranchedSave_ReportsLatestSaveTimestampAndTicksWhileKeepingColonyPeakHistory()
    {
        var session = CreateSession();
        var id = MakeIdentity("camp_branch", "seed-branch", 888, "14,0", 0, 13_000f, "Thing_Human7");
        var display = MakeDisplay("Forked Colony");
        var branch1Utc = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);
        var branch2Utc = new DateTime(2026, 11, 18, 11, 30, 0, DateTimeKind.Utc);

        session.Mutate(p =>
        {
            // Branch 1 reached Day 20 (1,200,000 ticks) at 10:00 UTC.
            ProfileMutations.ApplyLiveObservation(
                p, "camp_branch", id, MakeValues(1_200_000, wealthLast: 90_000, wealthMax: 95_000, raids: 12),
                display, branch1Utc, "1.7.4985 rev1269");

            // Player reloaded an earlier save and saved at Day 12 (720,000 ticks) at 11:30 UTC.
            ProfileMutations.ApplyLiveObservation(
                p, "camp_branch", id, MakeValues(720_000, wealthLast: 50_000, wealthMax: 55_000, raids: 6),
                display, branch2Utc, "1.7.4985 rev1269");
        }, SaveMode.None);

        var view = BuildView(session);
        var resolved = InGameColonyAccess.Resolve(view, "camp_branch");

        Assert.True(resolved.Found);
        Assert.True(resolved.RecordedPoint.HasObservedPoint);
        Assert.Equal(branch2Utc, resolved.RecordedPoint.LastRecordedUtc);
        Assert.Equal(720_000, resolved.RecordedPoint.LastRecordedTicks);

        // Meanwhile lifetime peak ticks on the colony stats remain 1,200,000.
        var colonyStats = view.Stats.Colonies.Single(c => c.Key == resolved.ColonyKey);
        Assert.Equal(1_200_000, colonyStats.MaxTicks);
    }

    [Fact]
    public void EndingSeededTraceOnlyColony_NeverSubstitutesZeroTicksForMissingObservation()
    {
        var session = CreateSession();
        var id = MakeIdentity("camp_trace_only", "seed-trace", 999, "3,0", 0, 10_500f, "Thing_Human21");

        session.Mutate(p =>
        {
            ProfileMutations.RegisterLiveCampaign(p, "camp_trace_only", id, MakeDisplay("Ascended Ruins"));
            ProfileMutations.UpsertEnding(p, new EndingEvent
            {
                CampaignId = "camp_trace_only",
                Type = EndingType.RoyalAscent,
                Ordinal = 0,
                Source = EndingSource.SeededTrace,
                TicksGame = 960_000,
                RealTimeUtc = new DateTime(2026, 11, 18, 2, 0, 0, DateTimeKind.Utc),
                GameVersion = "1.7.4985 rev1269",
            });
        }, SaveMode.None);

        var view = BuildView(session);
        var resolved = InGameColonyAccess.Resolve(view, "camp_trace_only");

        Assert.True(resolved.Found);
        Assert.NotNull(resolved.ColonyKey);
        Assert.False(resolved.RecordedPoint.HasObservedPoint);
        Assert.Null(resolved.RecordedPoint.LastRecordedUtc);
        Assert.Null(resolved.RecordedPoint.LastRecordedTicks);
    }

    [Fact]
    public void ManualMergeAndExclusion_StillResolvesMergedColonyByEitherCampaignId()
    {
        var session = CreateSession();
        var idA = MakeIdentity("camp_merge_a", "seed-ma", 12, "1,0", 0, 10_000f, "Thing_HumanA");
        var idB = MakeIdentity("camp_merge_b", "seed-mb", 34, "2,0", 0, 12_000f, "Thing_HumanB");
        var utcA = new DateTime(2026, 11, 14, 9, 0, 0, DateTimeKind.Utc);
        var utcB = new DateTime(2026, 11, 18, 7, 20, 0, DateTimeKind.Utc);

        session.Mutate(p =>
        {
            ProfileMutations.ApplyLiveObservation(p, "camp_merge_a", idA, MakeValues(300_000), MakeDisplay("Part 1"), utcA);
            ProfileMutations.ApplyLiveObservation(p, "camp_merge_b", idB, MakeValues(540_000), MakeDisplay("Part 2"), utcB);
        }, SaveMode.None);

        var initialView = BuildView(session);
        var keyA = InGameColonyAccess.Resolve(initialView, "camp_merge_a").ColonyKey!;
        var keyB = InGameColonyAccess.Resolve(initialView, "camp_merge_b").ColonyKey!;

        session.Mutate(p =>
        {
            p.UserDecisions.ManualMerges.Add([keyA, keyB]);
            p.UserDecisions.ExcludedCampaignKeys.Add(keyA);
        }, SaveMode.None);

        var mergedView = BuildView(session);
        var resA = InGameColonyAccess.Resolve(mergedView, "camp_merge_a");
        var resB = InGameColonyAccess.Resolve(mergedView, "camp_merge_b");

        Assert.True(resA.Found);
        Assert.True(resB.Found);
        Assert.Equal(resA.ColonyKey, resB.ColonyKey);
        Assert.Equal(utcB, resA.RecordedPoint.LastRecordedUtc);
        Assert.Equal(540_000, resA.RecordedPoint.LastRecordedTicks);

        var summary = session.ReadColonySummaryReport(mergedView, resB.ColonyKey!)!;
        Assert.True(summary.Overview.Excluded);
    }

    [Fact]
    public void MenuOptionHeightFitter_KeepsDefaultWhenFittingAndScalesDownToPreventClipping()
    {
        // MainTabWindow_Menu inner height in RimWorld 1.7: 390 - 2 * 18 = 354f.
        const float pauseMenuInnerHeight = 354f;

        // 6 options (e.g. Permadeath + statistics button): 6 * 45 + 5 * 7 = 305 <= 354 -> stays 45f.
        Assert.Equal(45f, MenuOptionHeightFitter.FitMinHeight(pauseMenuInnerHeight, 6));

        // 7 options (Reload Anytime + statistics button): 7 * 45 + 6 * 7 = 357 > 354 -> scales so total == 354f.
        var sevenHeight = MenuOptionHeightFitter.FitMinHeight(pauseMenuInnerHeight, 7);
        Assert.True(sevenHeight < 45f && sevenHeight > 44f);
        Assert.True(7 * sevenHeight + 6 * MenuOptionHeightFitter.OptionSpacing <= pauseMenuInnerHeight + 0.001f);

        // 8 options (Reload Anytime + DevMode SaveTranslationReport + statistics button): fits cleanly inside 354f.
        var eightHeight = MenuOptionHeightFitter.FitMinHeight(pauseMenuInnerHeight, 8);
        Assert.Equal(38.125f, eightHeight, precision: 4);
        Assert.True(8 * eightHeight + 7 * MenuOptionHeightFitter.OptionSpacing <= pauseMenuInnerHeight + 0.001f);

        // Extreme option count respects FloorMinHeight; invalid/zero available height keeps DefaultMinHeight.
        Assert.Equal(MenuOptionHeightFitter.FloorMinHeight, MenuOptionHeightFitter.FitMinHeight(pauseMenuInnerHeight, 25));
        Assert.Equal(MenuOptionHeightFitter.DefaultMinHeight, MenuOptionHeightFitter.FitMinHeight(0f, 7));
    }
}
