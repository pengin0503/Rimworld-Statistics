namespace RimworldStatistics.Core.Tests;

public sealed class HistoricalCounterIntervalsTests
{
    private static ColonyIdentity Identity() => new()
    {
        ModCampaignId = "campaign-1", SeedString = "seed", PersistentRandomValue = 1,
        StartingTile = "100,0", FirstWealthBits = 123u,
        StartingPawnIds = { "pawn-a" },
    };

    private static MetricFileAlias Alias(string id) => new()
    {
        FileName = id + ".rws", FileSize = 1234,
        LastWriteUtc = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),
    };

    private static void Point(Profile profile, string key, string? parent, int tick,
        long raids, long deaths, string modHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", string version = "1.7.5000 rev1275",
        bool includeRaids = true)
    {
        var identity = Identity();
        var save = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "sha-" + key,
            GameVersion = version, ModSetHash = modHash, SaveTick = tick,
            Identity = identity.Clone(), FileAliases = { Alias(key) },
        };
        if (includeRaids)
            save.Metrics[MetricKeys.RaidsPerYear] = MetricEntry.Of(
                MetricKeys.Revision(MetricKeys.RaidsPerYear),
                new MetricValue { Coverage = MetricCoverage.Complete, Numerator = raids, Denominator = 1, RealValue = raids });
        save.Metrics[MetricKeys.DeathsPerYear] = MetricEntry.Of(
            MetricKeys.Revision(MetricKeys.DeathsPerYear),
            new MetricValue { Coverage = MetricCoverage.Complete, Numerator = deaths, Denominator = 1, RealValue = deaths });

        profile.MetricObservations.Add(save);
        profile.MetricObservations.Add(new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint, Key = key, ParentKey = parent,
            PayloadHash = new string(char.ToLowerInvariant(key[0]), 64), SaveTick = tick,
            Identity = identity.Clone(), GameVersion = version, FileAliases = { Alias(key) },
        });
    }

    [Fact]
    public void DirectConfirmedCheckpointAncestor_ProducesOnlyPartialIntervalDeltas()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, 6, 3);
        var intervals = HistoricalCounterIntervals.Evaluate(p);
        var interval = Assert.Single(intervals);
        Assert.Equal(("A", "B", 60000, 120000), 
            (interval.ParentCheckpointId, interval.ChildCheckpointId, interval.StartTick, interval.EndTick));
        Assert.Equal(3, interval.RaidsIncrease);
        Assert.Equal(2, interval.ColonistDeathsIncrease);
        Assert.Equal(MetricCoverage.Partial, interval.Coverage);
    }

    [Fact]
    public void DiagnosticsShowNonZeroIntervalsSeparatelyNewestFirst()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, 5, 1);
        Point(p, "C", "A", 180000, 3, 2);
        Point(p, "D", "C", 240000, 3, 2);

        var rows = HistoricalCounterIntervals.ForDiagnostics(p);
        Assert.Collection(rows,
            row => Assert.Equal(("C", 0L, 1L, MetricCoverage.Partial),
                (row.ChildCheckpointId, row.RaidsIncrease, row.ColonistDeathsIncrease, row.Coverage)),
            row => Assert.Equal(("B", 2L, 0L, MetricCoverage.Partial),
                (row.ChildCheckpointId, row.RaidsIncrease, row.ColonistDeathsIncrease, row.Coverage)));
        Assert.Equal(3, HistoricalCounterIntervals.Evaluate(p).Count);
    }

    [Fact]
    public void DiagnosticsIdentifyBothSavesAndDeduplicateAliasesForSameContents()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, 5, 1);
        p.SaveObservations.Add(new SaveObservation
        {
            Identity = Identity(), Status = ObservationStatus.Ok,
            Display = new DisplayInfo { FactionName = "Colony Name" },
            Values = new ObservedValues { TicksGame = 120000 },
        });
        foreach (var point in p.MetricObservations.Where(o => o.Key is "B" or "sha-B"))
            point.FileAliases.Add(Alias("B-copy"));

        var row = Assert.Single(HistoricalCounterIntervals.ForDiagnostics(p));
        Assert.Equal(("Colony Name", "A.rws", "B-copy.rws"), (row.ColonyName, row.ParentSaveName, row.ChildSaveName));
    }

    [Fact]
    public void NoConfirmedParent_EvenSameCampaignAndLaterSave_IsNotCompared()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", null, 120000, 6, 3);
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }

    [Theory]
    [InlineData("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "1.7.5000 rev1275", 120000, 6, 3, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.4995 rev1269", 120000, 6, 3, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.5000 rev1275", 50000, 6, 3, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.5000 rev1275", 120000, 2, 3, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.5000 rev1275", 120000, 6, 0, true)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1.7.5000 rev1275", 120000, 6, 3, false)]
    public void ChangedInputsOrUnverifiedCounters_AreNotComparable(
        string modHash, string version, int endTick, long raids, long deaths, bool includeRaids)
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", endTick, raids, deaths, modHash, version, includeRaids);
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }

    [Fact]
    public void SiblingForks_AreReturnedSeparatelyNeverSummedAsOneLineage()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, 6, 2);
        Point(p, "C", "A", 150000, 5, 4);
        var intervals = HistoricalCounterIntervals.Evaluate(p);
        Assert.Equal(2, intervals.Count);
        Assert.Equal(new[] { "B", "C" }, intervals.Select(x => x.ChildCheckpointId).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { 3L, 2L }, intervals.OrderBy(x => x.ChildCheckpointId).Select(x => x.RaidsIncrease));
    }

    [Fact]
    public void ConflictedOrDuplicatedCheckpointId_RejectsIntervals()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, 6, 2);
        p.MetricObservations.Add(new MetricObservation
        {
            Source = MetricObservationSource.Checkpoint, Key = "A",
            PayloadHash = new string('z', 64), Identity = Identity(),
        });
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }
    [Fact]
    public void CounterValuesExceedingTheSavedInt32Domain_AreNotEvidence()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        Point(p, "B", "A", 120000, (long)int.MaxValue + 1, 3);
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImportUpsert_RetainsSourceSaveObservationsForCheckpointEdges_InEitherScanOrder(bool reverse)
    {
        var source = new Profile();
        Point(source, "A", null, 60000, 3, 1);
        Point(source, "B", "A", 120000, 6, 3);
        var p = new Profile();
        var ids = reverse ? new[] { "B", "A" } : new[] { "A", "B" };
        foreach (var id in ids)
        {
            var checkpoint = source.MetricObservations.Single(x => x.Source == MetricObservationSource.Checkpoint && x.Key == id);
            var saved = source.MetricObservations.Single(x => x.Source == MetricObservationSource.SaveFile && x.Key == "sha-" + id);
            ProfileMutations.UpsertMetricObservation(p, checkpoint);
            ProfileMutations.UpsertMetricObservation(p, saved);
        }
        Assert.Equal(2, p.MetricObservations.Count(x => x.Source == MetricObservationSource.SaveFile));
        Assert.Single(HistoricalCounterIntervals.Evaluate(p));
    }

    [Fact]
    public void ReaderVersionUpgrade_MergesSavedModListHashIntoIdenticalShaObservation()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 3, 1);
        var fromOldReader = p.MetricObservations.Single(x => x.Source == MetricObservationSource.SaveFile);
        var hash = fromOldReader.ModSetHash;
        fromOldReader.ModSetHash = null;
        var refreshed = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile,
            Key = fromOldReader.Key,
            ModSetHash = hash,
            FormatVersion = SaveReader.Version,
            Identity = fromOldReader.Identity.Clone(),
            GameVersion = fromOldReader.GameVersion,
            SaveTick = fromOldReader.SaveTick,
            FileAliases = { Alias("A") },
        };
        ProfileMutations.UpsertMetricObservation(p, refreshed);
        Assert.Equal(hash, fromOldReader.ModSetHash);
    }

    [Fact]
    public void ReusedCheckpointAliasFromLaterSave_DoesNotInvalidateUniqueSourceInterval()
    {
        var profile = new Profile();
        Point(profile, "A", null, 60000, 2, 1);
        Point(profile, "B", "A", 120000, 4, 3);
        var checkpoint = profile.MetricObservations.Single(o =>
            o.Source == MetricObservationSource.Checkpoint && o.Key == "B");
        checkpoint.FileAliases.Add(Alias("later"));
        profile.MetricObservations.Add(new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "sha-later",
            GameVersion = checkpoint.GameVersion, ModSetHash = new string('a', 64),
            SaveTick = 180000, Identity = Identity(),
            FileAliases = { Alias("later") },
        });
        var interval = Assert.Single(HistoricalCounterIntervals.Evaluate(profile));
        Assert.Equal(("A", "B"), (interval.ParentCheckpointId, interval.ChildCheckpointId));
        Assert.Equal(2, interval.RaidsIncrease);
    }

    [Fact]
    public void TwoPlausibleSavesForSameCheckpointStillRejectAmbiguousAncestry()
    {
        var profile = new Profile();
        Point(profile, "A", null, 60000, 2, 1);
        Point(profile, "B", "A", 120000, 4, 3);
        var cp = profile.MetricObservations.Single(o =>
            o.Source == MetricObservationSource.Checkpoint && o.Key == "B");
        cp.FileAliases.Add(Alias("copy"));
        var original = profile.MetricObservations.Single(o =>
            o.Source == MetricObservationSource.SaveFile && o.Key == "sha-B");
        var duplicate = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile, Key = "other-sha",
            GameVersion = original.GameVersion, ModSetHash = original.ModSetHash,
            SaveTick = original.SaveTick, Identity = Identity(),
            FileAliases = { Alias("copy") },
        };
        foreach (var (key, value) in original.Metrics)
            duplicate.Metrics[key] = value;
        profile.MetricObservations.Add(duplicate);
        Assert.Empty(HistoricalCounterIntervals.Evaluate(profile));
    }


    [Fact]
    public void IdenticalModDigestButContradictoryDlcMetadata_IsNotComparable()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 2, 1);
        Point(p, "B", "A", 120000, 4, 2);
        foreach (var o in p.MetricObservations)
            o.ActiveDlcIds = new List<string> { "ludeon.rimworld.royalty" };
        Assert.Single(HistoricalCounterIntervals.Evaluate(p));
        var child = p.MetricObservations.Single(o =>
            o.Source == MetricObservationSource.SaveFile && o.Key == "sha-B");
        child.ActiveDlcIds = new List<string> { "ludeon.rimworld.biotech" };
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }

    [Fact]
    public void CyclicOrNonHexCheckpointProof_DoesNotCreateInterval()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 2, 1);
        Point(p, "B", "A", 120000, 4, 2);
        var a = p.MetricObservations.Single(o =>
            o.Source == MetricObservationSource.Checkpoint && o.Key == "A");
        a.ParentKey = "B"; // an impossible ancestor cycle
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
        a.ParentKey = null;
        var b = p.MetricObservations.Single(o =>
            o.Source == MetricObservationSource.Checkpoint && o.Key == "B");
        b.PayloadHash = new string('z', 64);
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }

    [Fact]
    public void SaveOverwriteWithoutMatchingOriginalAlias_CannotProveParent()
    {
        var p = new Profile();
        Point(p, "A", null, 60000, 2, 1);
        Point(p, "B", "A", 120000, 4, 2);
        var saved = p.MetricObservations.Single(o =>
            o.Source == MetricObservationSource.SaveFile && o.Key == "sha-A");
        saved.FileAliases.Clear();
        saved.FileAliases.Add(new MetricFileAlias
        {
            FileName = "A.rws", FileSize = 9999,
            LastWriteUtc = new DateTime(2026, 11, 9, 0, 0, 0, DateTimeKind.Utc),
        });
        Assert.Empty(HistoricalCounterIntervals.Evaluate(p));
    }
}
