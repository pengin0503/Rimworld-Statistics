using System.Text.Json;
using RimworldStatistics.Core.Tests.Fixtures;

namespace RimworldStatistics.Core.Tests;

public sealed class CheckpointArchiveTests
{
    private const string Campaign = "archive-test";
    private static readonly DateTime Now = new(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

    private static MetricObservation Observation(string key, string? parent = null, int tick = 10) => new()
    {
        Source = MetricObservationSource.Checkpoint,
        Key = key,
        ParentKey = parent,
        PayloadHash = key + "-content",
        SaveTick = tick,
        Identity = new ColonyIdentity
        {
            ModCampaignId = Campaign, SeedString = "archive", PersistentRandomValue = 1,
            StartingTile = "1,0", GameStartAbsTick = 0, FirstWealthBits = 1,
        },
    };

    private static string Line(MetricObservation o) => JsonSerializer.Serialize(o, ProfileJson.CompactOptions);

    [Fact]
    public void Append_ThenRead_ReturnsEveryCopyOfThatKeyInOrder()
    {
        using var temp = new TempDir();
        var archive = new FileCheckpointArchive(temp.Path);
        archive.Append(Campaign, new[] { Line(Observation("P")), Line(Observation("Q")) });
        archive.Append(Campaign, new[] { Line(Observation("P", tick: 20)) });
        Assert.Equal(new[] { 10, 20 }, archive.Read(Campaign, "P").Select(o => o.SaveTick));
    }

    [Fact]
    public void Read_SkipsATornLastLine()
    {
        using var temp = new TempDir();
        var archive = new FileCheckpointArchive(temp.Path);
        archive.Append(Campaign, new[] { Line(Observation("P")) });
        File.AppendAllText(Assert.Single(Directory.GetFiles(temp.Path)), "{\"source\":");
        Assert.Single(archive.Read(Campaign, "P"));
        archive.Append(Campaign, new[] { Line(Observation("P", tick: 30)) });
        Assert.Equal(new[] { 10, 30 }, archive.Read(Campaign, "P").Select(o => o.SaveTick));
    }

    [Fact]
    public void Read_MissingFileOrDirectory_ReturnsEmpty()
    {
        using var temp = new TempDir();
        var archive = new FileCheckpointArchive(temp.File("missing"));
        Assert.Empty(archive.Read(Campaign, "P"));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("a/b")]
    public void FileName_IsAHashOfTheCampaignId(string campaign)
    {
        using var temp = new TempDir();
        var directory = temp.File("checkpoint-archive");
        var archive = new FileCheckpointArchive(directory);
        archive.Append(campaign, new[] { Line(Observation("P")) });
        Assert.Matches("^[0-9a-f]{32}\\.jsonl$", Path.GetFileName(Assert.Single(Directory.GetFiles(directory))));
    }

    [Fact]
    public void Append_Failure_Throws()
    {
        using var temp = new TempDir();
        var file = temp.File("checkpoint-archive");
        File.WriteAllText(file, "");
        var archive = new FileCheckpointArchive(file);
        var exception = Record.Exception(() => archive.Append(Campaign, new[] { Line(Observation("P")) }));
        Assert.True(exception is IOException or UnauthorizedAccessException);
    }

    private static readonly MetricDefinition BuiltIn = MetricRegistry.Compose(null, MetricDisplayPolicy.AllDetailsVisibleUnits)
        .First(d => d.Source == MetricObservationSource.Checkpoint && d.Selection == ColonySelection.Representative
            && !SnapshotSelectionPolicy.UsesLastCapture(d) && d.RequiredDlc is null && d.Overall == OverallAggregation.Sum);

    private static MetricEntry Count(long value, int? revision = null) => MetricEntry.Of(revision ?? BuiltIn.Revision,
        new MetricValue { IntegerValue = value, Coverage = MetricCoverage.Complete, SinceTick = 0, ThroughTick = 10 });

    private static (Profile Profile, MetricObservation Parent, MetricObservation Child) Pair()
    {
        var p = new Profile();
        var parent = Observation("P");
        var child = Observation("C", "P", 20);
        parent.Metrics.Add(BuiltIn.StoredKey, Count(1));
        child.Metrics.Add(BuiltIn.StoredKey, Count(2));
        ProfileMutations.UpsertMetricObservation(p, parent);
        ProfileMutations.UpsertMetricObservation(p, child);
        Assert.True(parent.Compressed);
        return (p, parent, child);
    }

    [Fact]
    public void Plan_DropsOnlyKeysTheConfirmingChildAlsoHolds()
    {
        var (profile, parent, child) = Pair();
        var other = MetricRegistry.Catalog.First(d => d.Source == MetricObservationSource.Checkpoint
            && d.Selection == ColonySelection.Representative && d.StoredKey != BuiltIn.StoredKey);
        parent.Metrics.Add(other.StoredKey, Count(3, other.Revision));
        Assert.Equal(BuiltIn.StoredKey, Assert.Single(Assert.Single(CheckpointArchivePlanner.Plan(profile)).Drops).Key);
        child.ParentKey = "different";
        Assert.Empty(CheckpointArchivePlanner.Plan(profile));
    }

    [Fact]
    public void Plan_KeepsKeysOfAnotherRevisionInTheChild()
    {
        var (profile, _, child) = Pair();
        child.Metrics[BuiltIn.StoredKey] = Count(2, BuiltIn.Revision + 1);
        Assert.Empty(CheckpointArchivePlanner.Plan(profile));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("quarantined")]
    [InlineData("entry-extension")]
    [InlineData("value-extension")]
    public void Plan_KeepsUnknownKeysQuarantinedEntriesAndExtensionData(string kind)
    {
        var (profile, parent, child) = Pair();
        var entry = parent.Metrics[BuiltIn.StoredKey];
        var extra = JsonSerializer.SerializeToElement(1);
        switch (kind)
        {
            case "unknown":
                parent.Metrics.Remove(BuiltIn.StoredKey);
                parent.Metrics.Add("unknown.archive.key", entry);
                child.Metrics.Add("unknown.archive.key", Count(2));
                break;
            case "quarantined":
                entry.Quarantined = extra;
                break;
            case "entry-extension":
                entry.ExtensionData = new() { ["future"] = extra };
                break;
            case "value-extension":
                entry.Values[MetricEntry.Scalar].ExtensionData = new() { ["future"] = extra };
                break;
        }
        Assert.Empty(CheckpointArchivePlanner.Plan(profile));
    }

    private static ModMetricDescriptor Provider(ColonySelection selection = ColonySelection.Representative) => new()
    {
        OwnerPackageId = "test.archive",
        ProviderId = "test.archive",
        FamilyId = "events",
        MetricName = "counter",
        Revision = 1,
        Source = MetricObservationSource.Checkpoint,
        Selection = selection,
        Overall = OverallAggregation.Sum,
        LabelKey = "Test",
        DescriptionKey = "Test",
    };

    [Theory]
    [InlineData(ColonySelection.ObservationMax)]
    [InlineData(ColonySelection.ObservationMin)]
    [InlineData(ColonySelection.ObservationMaxByBreakdown)]
    public void Plan_KeepsNonRepresentativeProviderDefinitions(ColonySelection selection)
    {
        var (profile, parent, child) = Pair();
        var descriptor = Provider(selection);
        ProfileMutations.RememberModMetrics(profile, new[] { descriptor });
        Assert.Single(profile.ModMetricDescriptors);
        parent.Metrics.Add(descriptor.StableKey, Count(4, 1));
        child.Metrics.Add(descriptor.StableKey, Count(5, 1));
        Assert.Equal(BuiltIn.StoredKey, Assert.Single(Assert.Single(CheckpointArchivePlanner.Plan(profile)).Drops).Key);
    }

    [Fact]
    public void Plan_ArchivesRepresentativeProviderMetricsOnlyWhenAllValidChildrenConfirm()
    {
        var (profile, parent, child) = Pair();
        var descriptor = Provider(ColonySelection.Representative);
        ProfileMutations.RememberModMetrics(profile, new[] { descriptor });
        parent.Metrics.Add(descriptor.StableKey, Count(4, 1));
        child.Metrics.Add(descriptor.StableKey, Count(5, 1));

        var drops = Assert.Single(CheckpointArchivePlanner.Plan(profile)).Drops.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(BuiltIn.StoredKey, drops);
        Assert.Contains(descriptor.StableKey, drops);

        // If a sibling branch does not carry this optional MOD metric, keep it at the branch point.
        var branchWithoutMod = Observation("B", "P", 15);
        branchWithoutMod.Metrics.Add(BuiltIn.StoredKey, Count(2));
        ProfileMutations.UpsertMetricObservation(profile, branchWithoutMod);

        drops = Assert.Single(CheckpointArchivePlanner.Plan(profile)).Drops.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(BuiltIn.StoredKey, drops);
        Assert.DoesNotContain(descriptor.StableKey, drops);
    }

    [Theory]
    [InlineData("uncompressed")]
    [InlineData("conflicted")]
    [InlineData("archived")]
    [InlineData("campaignless")]
    public void Plan_IgnoresUncompressedConflictedArchivedAndCampaignlessAncestors(string kind)
    {
        var (profile, parent, _) = Pair();
        if (kind == "uncompressed") parent.Compressed = false;
        if (kind == "conflicted") parent.Conflicted = true;
        if (kind == "archived") parent.ArchivedMetrics = true;
        if (kind == "campaignless") parent.Identity.ModCampaignId = null;
        Assert.Empty(CheckpointArchivePlanner.Plan(profile));
    }

    [Fact]
    public void Plan_DoesNotChangeTheProfile()
    {
        var (profile, _, _) = Pair();
        var before = ProfileJson.Serialize(profile);
        Assert.Single(CheckpointArchivePlanner.Plan(profile));
        Assert.Equal(before, ProfileJson.Serialize(profile));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Apply_SkipsEntriesThatChangedAfterPlanning(bool changeReference)
    {
        var (profile, parent, _) = Pair();
        var step = Assert.Single(CheckpointArchivePlanner.Plan(profile));
        if (changeReference)
            parent.Metrics[BuiltIn.StoredKey] = Count(7);
        else
            parent.Metrics[BuiltIn.StoredKey].Values.Add("new", Count(7).Values[MetricEntry.Scalar]);
        CheckpointArchivePlanner.Apply(step);
        Assert.Contains(BuiltIn.StoredKey, parent.Metrics.Keys);
        Assert.False(parent.ArchivedMetrics);
    }

    [Fact]
    public void Apply_DoesNothingWhenTheAncestorWasUncompressedMeanwhile()
    {
        var (profile, parent, _) = Pair();
        var step = Assert.Single(CheckpointArchivePlanner.Plan(profile));
        parent.Compressed = false;
        CheckpointArchivePlanner.Apply(step);
        Assert.Contains(BuiltIn.StoredKey, parent.Metrics.Keys);
        Assert.False(parent.ArchivedMetrics);
    }

    private sealed class MemoryArchive : ICheckpointArchive
    {
        public readonly Dictionary<string, List<string>> Lines = new(StringComparer.Ordinal);
        public bool FailAppend { get; set; }
        public bool FailRead { get; set; }
        public bool Missing { get; set; }
        public Action? OnAppend { get; set; }

        public void Append(string campaignId, IReadOnlyList<string> jsonLines)
        {
            if (FailAppend)
                throw new IOException("Archive unavailable.");
            if (!Lines.TryGetValue(campaignId, out var lines))
                Lines.Add(campaignId, lines = new List<string>());
            lines.AddRange(jsonLines);
            OnAppend?.Invoke();
        }

        public IReadOnlyList<MetricObservation> Read(string campaignId, string checkpointKey)
            => FailRead ? throw new IOException("Archive unreadable.")
                : Missing || !Lines.TryGetValue(campaignId, out var lines) ? Array.Empty<MetricObservation>()
                : lines.Select(line => JsonSerializer.Deserialize<MetricObservation>(line, ProfileJson.CompactOptions)!)
                    .Where(o => o.Key == checkpointKey).ToArray();
    }

    private static void Add(ProfileSession session, MetricObservation observation)
        => session.Mutate(p => ProfileMutations.UpsertMetricObservation(p, observation), SaveMode.None);

    private static void Seed(ProfileSession session, ModMetricDescriptor? descriptor = null)
        => session.Mutate(p =>
        {
            ProfileMutations.ApplyLiveObservation(p, Campaign, Observation("P").Identity,
                new ObservedValues { TicksGame = 40 }, new DisplayInfo(), Now);
            if (descriptor is not null)
                ProfileMutations.RememberModMetrics(p, new[] { descriptor });
        }, SaveMode.None);

    private static MetricObservation WithCount(string key, string? parent, int tick, int count)
    {
        var observation = Observation(key, parent, tick);
        observation.Metrics.Add(BuiltIn.StoredKey, Count(count));
        return observation;
    }

    private static MetricObservation Parent(Profile p) => p.MetricObservations.Single(o => o.Key == "P");

    private static string Rows(ProfileSession session)
    {
        var view = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits).Numeric;
        static object Row(NumericRow r) => new
        {
            r.Definition.Key, r.Breakdown, r.Value.Number, r.Value.Coverage, r.Value.Evidence, r.Value.Numerator,
            r.Value.Denominator, r.Value.SampleCount, r.Value.ThroughTick, r.Targets, r.Valid,
            Breakdowns = r.Breakdowns.Select(Row),
        };
        return JsonSerializer.Serialize(new { Overall = view.Overall.Select(Row),
            Colonies = view.Colonies.Select(c => new { c.Key, Rows = c.Rows.Select(Row) }) });
    }

    [Theory]
    [InlineData("linear")]
    [InlineData("removed-provider")]
    [InlineData("branches")]
    [InlineData("conflict")]
    [InlineData("two-conflicts")]
    [InlineData("reimport")]
    public void Archiving_NeverChangesAnyShownValue(string scenario)
    {
        using var plain = new TempDir();
        using var archived = new TempDir();
        var archive = new MemoryArchive();
        var ordinary = new ProfileSession(new ProfileStore(plain.Path), () => Now);
        var tiered = new ProfileSession(new ProfileStore(archived.Path), () => Now, archive);
        var provider = Provider();
        Seed(ordinary, provider);
        Seed(tiered, provider);

        void AddBoth(MetricObservation observation)
        {
            Add(ordinary, observation);
            Add(tiered, JsonSerializer.Deserialize<MetricObservation>(Line(observation), ProfileJson.CompactOptions)!);
        }

        var parent = WithCount("P", null, 10, 1);
        var first = WithCount("C1", "P", 20, 2);
        if (scenario == "removed-provider")
        {
            parent.Metrics[provider.StableKey] = Count(4, 1);
            first.Metrics[provider.StableKey] = Count(5, 1);
        }
        AddBoth(parent);
        AddBoth(first);
        AddBoth(WithCount("C2", scenario == "branches" ? "P" : "C1", 30, 3));
        tiered.Flush();
        Assert.True(tiered.Read(Parent).ArchivedMetrics);
        Assert.Equal(Rows(ordinary), Rows(tiered));

        if (scenario == "reimport")
        {
            var alias = WithCount("P", null, 10, 1);
            alias.FileAliases.Add(new MetricFileAlias { FileName = "copy.rws" });
            alias.Metrics[provider.StableKey] = Count(6, 1);
            AddBoth(alias);
            tiered.Flush();
            Assert.Equal(Rows(ordinary), Rows(tiered));
        }
        if (scenario is "conflict" or "two-conflicts" or "reimport")
        {
            var conflicting = WithCount("C2", "C1", 30, 30);
            conflicting.PayloadHash = "different";
            AddBoth(conflicting);
        }
        if (scenario == "two-conflicts")
        {
            var conflicting = WithCount("C1", "P", 20, 20);
            conflicting.PayloadHash = "different";
            AddBoth(conflicting);
        }
        ordinary.Flush();
        tiered.Flush();
        Assert.Equal(Rows(ordinary), Rows(tiered));
    }

    private static ProfileSession Start(TempDir directory, ICheckpointArchive? archive)
    {
        var session = new ProfileSession(new ProfileStore(directory.Path), () => Now, archive);
        Seed(session);
        Add(session, WithCount("P", null, 10, 1));
        Add(session, WithCount("C", "P", 20, 2));
        return session;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Write_WhenDiskLockIsHeldOrPrimaryWasReplaced_LeavesArchiveUntouched(bool replaced)
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        if (replaced)
            new ProfileStore(temp.Path).Save(new Profile());

        using var held = replaced ? null : File.Open(temp.File("profile.json.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        session.Flush();

        Assert.IsType<IOException>(session.LastSaveError);
        Assert.Empty(archive.Lines);
        Assert.False(session.Read(Parent).ArchivedMetrics);
    }

    [Fact]
    public void Write_AppendsBeforeTheProfileIsSlimmed()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        session.Flush();
        Assert.Contains(BuiltIn.StoredKey, Assert.Single(archive.Read(Campaign, "P")).Metrics.Keys);
        var saved = Parent(new ProfileStore(temp.Path).Load(Now).Profile);
        Assert.True(saved.ArchivedMetrics);
        Assert.DoesNotContain(BuiltIn.StoredKey, saved.Metrics.Keys);
    }

    [Fact]
    public void Write_WhenProfileChangesDuringAppend_KeepsTheNewValue()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        archive.OnAppend = () => session.Mutate(p =>
            Parent(p).Metrics[BuiltIn.StoredKey].Values[MetricEntry.Scalar] = Count(9).Values[MetricEntry.Scalar], SaveMode.None);
        session.Flush();
        var saved = Parent(new ProfileStore(temp.Path).Load(Now).Profile);
        Assert.Equal(9, saved.Scalar(BuiltIn.StoredKey, BuiltIn.Revision)?.IntegerValue);
    }

    [Fact]
    public void Write_UnrelatedMutationDuringAppendStillTrimsArchivedAncestor()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        archive.OnAppend = () => session.Mutate(_ => { }, SaveMode.None);
        session.Flush();
        Assert.True(session.Read(Parent).ArchivedMetrics);
        Assert.Single(archive.Read(Campaign, "P"));
        session.Mutate(_ => { }, SaveMode.None);
        session.Flush();
        Assert.Single(archive.Read(Campaign, "P"));
        Assert.True(Parent(new ProfileStore(temp.Path).Load(Now).Profile).ArchivedMetrics);
    }

    [Fact]
    public void Write_ChildProofChangedDuringAppendDoesNotTrimAncestor()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        archive.OnAppend = () => session.Mutate(p => p.MetricObservations.Single(o => o.Key == "C")
            .Metrics.Clear(), SaveMode.None);
        session.Flush();
        Assert.False(session.Read(Parent).ArchivedMetrics);
        Assert.Contains(BuiltIn.StoredKey, session.Read(Parent).Metrics.Keys);
    }

    [Fact]
    public void Write_WhenAppendFails_KeepsFullMetricsAndStillSavesTheProfile()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive { FailAppend = true };
        var session = Start(temp, archive);
        session.Flush();
        var saved = Parent(new ProfileStore(temp.Path).Load(Now).Profile);
        Assert.False(saved.ArchivedMetrics);
        Assert.Contains(BuiltIn.StoredKey, saved.Metrics.Keys);
        Assert.IsType<IOException>(session.LastArchiveError);
        Assert.Null(session.LastSaveError);
    }

    [Fact]
    public void Backup_AfterTheArchiveIsFixed_RetriesTheAppendWithoutAnotherChange()
    {
        // #315: the failed append left full inline metrics on disk; nothing changes before the user retries the backup.
        using var temp = new TempDir();
        var archive = new MemoryArchive { FailAppend = true };
        var session = Start(temp, archive);
        session.Flush();
        Assert.Throws<IOException>(() => session.WithWritesPaused(() => 0));   // still failing: still blocked
        var onDisk = Parent(new ProfileStore(temp.Path).Load(Now).Profile);
        Assert.False(onDisk.ArchivedMetrics);
        Assert.Contains(BuiltIn.StoredKey, onDisk.Metrics.Keys);

        archive.FailAppend = false;
        Assert.Equal(1, session.WithWritesPaused(() => 1));
        Assert.Null(session.LastArchiveError);
        Assert.Single(archive.Read(Campaign, "P"));
        var saved = new ProfileStore(temp.Path).Load(Now).Profile;
        Assert.True(Parent(saved).ArchivedMetrics);   // the archive reference and the trimmed parent are written together
        Assert.Contains(BuiltIn.StoredKey, saved.MetricObservations.Single(o => o.Key == "C").Metrics.Keys);
        Assert.Equal(2, Overall(new ProfileSession(new ProfileStore(temp.Path), () => Now, archive)).Value.Number);   // C's total

        session.WithWritesPaused(() => 0);   // nothing left to retry: no second copy in the archive
        Assert.Single(archive.Read(Campaign, "P"));
    }

    [Fact]
    public void Restore_AfterTheArchiveIsFixed_ClosesWithoutAnotherChange()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive { FailAppend = true };
        var session = Start(temp, archive);
        session.Flush();
        archive.FailAppend = false;
        session.Close();   // a restore closes the current session first
        Assert.Single(archive.Read(Campaign, "P"));
        Assert.True(Parent(new ProfileStore(temp.Path).Load(Now).Profile).ArchivedMetrics);
    }

    [Fact]
    public void ArchiveReadFailure_IsReportedButDoesNotBlockABackup()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        session.Flush();
        archive.FailRead = true;
        Conflict(session);   // the archived parent must be read back
        Assert.IsType<IOException>(session.LastArchiveError);
        Assert.Equal(1, session.WithWritesPaused(() => 1));   // the files are as they were: nothing waits for the archive
        archive.FailRead = false;
    }

    private static void Conflict(ProfileSession session)
    {
        var incoming = WithCount("C", "P", 20, 2);
        incoming.PayloadHash = "conflict";
        Add(session, incoming);
    }

    private static NumericRow Overall(ProfileSession session) => session.GetView(new StatsOptions(),
        MetricDisplayPolicy.AllDetailsVisibleUnits).Numeric.Overall.Single(r => r.Definition.Key == BuiltIn.Key);

    [Fact]
    public void Conflict_WithUnreadableArchive_ShowsUnknownNeverZero()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        session.Flush();
        archive.Missing = true;
        Conflict(session);
        var parent = session.Read(Parent);
        Assert.True(parent.ArchivedMetrics);
        Assert.False(parent.Compressed);
        Assert.Null(Overall(session).Value.Number);
        Assert.Equal(MetricCoverage.Unknown, Overall(session).Value.Coverage);
        session.Flush();
    }

    [Fact]
    public void Reload_RetriesRehydration()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        session.Flush();
        archive.Missing = true;
        Conflict(session);
        session.Flush();
        archive.Missing = false;
        var reloaded = new ProfileSession(new ProfileStore(temp.Path), () => Now, archive);
        Assert.False(reloaded.Read(Parent).ArchivedMetrics);
        Assert.Equal(1, Overall(reloaded).Value.Number);
    }

    [Fact]
    public void Reimport_WhenAppendFails_KeepsNewMetricsOnDisk()
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        var session = Start(temp, archive);
        session.Flush();
        archive.FailAppend = true;
        var reread = WithCount("P", null, 10, 1);
        reread.Metrics[BuiltIn.StoredKey].Values.Add("new", Count(7).Values[MetricEntry.Scalar]);
        Add(session, reread);
        session.Flush();
        Assert.Contains("new", Parent(new ProfileStore(temp.Path).Load(Now).Profile).Metrics[BuiltIn.StoredKey].Values.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadOnlyOrNoArchive_NeverArchives(bool readOnly)
    {
        using var temp = new TempDir();
        var archive = new MemoryArchive();
        if (readOnly)
            File.WriteAllText(temp.File("profile.json"), "{\"schemaVersion\":3}");
        var session = Start(temp, readOnly ? archive : null);
        session.Flush();
        Assert.Equal(readOnly, session.LoadResult.ReadOnly);
        Assert.False(session.Read(Parent).ArchivedMetrics);
        Assert.Empty(archive.Lines);
    }

    [Fact]
    public void OlderBinary_RoundTripsTheFlag()
    {
        var (profile, parent, _) = Pair();
        Assert.DoesNotContain("archivedMetrics", ProfileJson.Serialize(profile));
        parent.ArchivedMetrics = true;
        var oldReader = JsonSerializer.Deserialize<LegacyObservation>(Line(parent), ProfileJson.CompactOptions)!;
        Assert.True(oldReader.ExtensionData["archivedMetrics"].GetBoolean());
        Assert.True(Parent(ProfileJson.Deserialize(ProfileJson.Serialize(profile))).ArchivedMetrics);
    }

    private sealed class LegacyObservation
    {
        [System.Text.Json.Serialization.JsonExtensionData]
        public Dictionary<string, JsonElement> ExtensionData { get; set; } = new(StringComparer.Ordinal);
    }

    [Fact]
    public void OptionalModCheckpoints_AreArchivedWithoutChangingValuesOrCharts()
    {
        using var plainDir = new TempDir();
        using var tieredDir = new TempDir();
        var archive = new FileCheckpointArchive(tieredDir.File("checkpoint-archive"));
        var ordinary = new ProfileSession(new ProfileStore(plainDir.Path), () => Now);
        var tiered = new ProfileSession(new ProfileStore(tieredDir.Path), () => Now, archive);

        var scalarDesc = Provider(ColonySelection.Representative);
        var breakdownDesc = scalarDesc with
        {
            FamilyId = "crafted", MetricName = "by_def", HasBreakdown = true,
            ProviderId = "test.crafted", OwnerPackageId = "test.crafted",
        };
        var maxDesc = scalarDesc with
        {
            FamilyId = "peak", MetricName = "max_val", Selection = ColonySelection.ObservationMax,
            Overall = OverallAggregation.Max, ProviderId = "test.peak", OwnerPackageId = "test.peak",
        };
        var descriptors = new[] { scalarDesc, breakdownDesc, maxDesc };

        void Init(ProfileSession s) => s.Mutate(p =>
        {
            ProfileMutations.ApplyLiveObservation(p, Campaign, Observation("P").Identity,
                new ObservedValues { TicksGame = 1000 }, new DisplayInfo(), Now);
            ProfileMutations.RememberModMetrics(p, descriptors);
        }, SaveMode.None);

        Init(ordinary);
        Init(tiered);

        string? parent = null;
        for (var i = 1; i <= 25; i++)
        {
            var key = $"cp-{i:D3}";
            MetricObservation MakeObs()
            {
                var obs = Observation(key, parent, i * 100);
                obs.EvidenceByAcquisition = new Dictionary<string, MetricEvidence>(StringComparer.Ordinal)
                {
                    [scalarDesc.AcquisitionId] = MetricEvidence.Verified,
                    [breakdownDesc.AcquisitionId] = MetricEvidence.Experimental,
                    [maxDesc.AcquisitionId] = MetricEvidence.Verified,
                };
                obs.ProviderCaptures = new Dictionary<string, ModProviderCapture>(StringComparer.Ordinal)
                {
                    [scalarDesc.ProviderId] = new(scalarDesc.OwnerPackageId, "1.0.0", "1.0.0"),
                    [breakdownDesc.ProviderId] = new(breakdownDesc.OwnerPackageId, "1.0.0", "1.0.0"),
                    [maxDesc.ProviderId] = new(maxDesc.OwnerPackageId, "1.0.0", "1.0.0"),
                };
                obs.Metrics[BuiltIn.StoredKey] = Count(i);
                obs.Metrics[scalarDesc.StableKey] = MetricEntry.Of(1, new MetricValue
                {
                    IntegerValue = i * 10, Coverage = i == 5 ? MetricCoverage.Partial : MetricCoverage.Complete,
                    SinceTick = 0, ThroughTick = i * 100,
                });
                var bd = MetricEntry.Of(1, new MetricValue
                {
                    IntegerValue = i * 3, Coverage = MetricCoverage.Complete, SinceTick = 0, ThroughTick = i * 100,
                });
                bd.Values["test.crafted/Widget"] = new MetricValue { IntegerValue = i * 2, Coverage = MetricCoverage.Complete };
                bd.Values["test.crafted/Gadget"] = new MetricValue { IntegerValue = i, Coverage = MetricCoverage.Complete };
                obs.Metrics[breakdownDesc.StableKey] = bd;
                obs.Metrics[maxDesc.StableKey] = MetricEntry.Of(1, new MetricValue
                {
                    IntegerValue = i == 12 ? 999 : i, Coverage = MetricCoverage.Complete, SinceTick = 0, ThroughTick = i * 100,
                });
                return obs;
            }
            Add(ordinary, MakeObs());
            Add(tiered, MakeObs());
            parent = key;
        }
        ordinary.Flush();
        tiered.Flush();

        var plainSize = new FileInfo(plainDir.File("profile.json")).Length;
        var tieredSize = new FileInfo(tieredDir.File("profile.json")).Length;
        Assert.True(tieredSize < plainSize, $"Expected archived profile ({tieredSize}) < plain ({plainSize})");

        // Reload both and compare views and chart history
        var ordinaryReloaded = new ProfileSession(new ProfileStore(plainDir.Path), () => Now);
        var tieredReloaded = new ProfileSession(new ProfileStore(tieredDir.Path), () => Now, archive);

        Assert.Equal(Rows(ordinaryReloaded), Rows(tieredReloaded));

        // Representative + breakdown keys were dropped from compressed ancestors while ObservationMax stayed in profile
        var archivedAncestors = tieredReloaded.Read(p => p.MetricObservations.Where(o => o.Compressed).ToList());
        Assert.Equal(24, archivedAncestors.Count);
        Assert.All(archivedAncestors, o =>
        {
            Assert.True(o.ArchivedMetrics);
            Assert.DoesNotContain(scalarDesc.StableKey, o.Metrics.Keys);
            Assert.DoesNotContain(breakdownDesc.StableKey, o.Metrics.Keys);
            Assert.Contains(maxDesc.StableKey, o.Metrics.Keys);
            // Evidence/capture for remaining ObservationMax is retained, while dropped ones are trimmed
            Assert.False(o.EvidenceByAcquisition?.ContainsKey(scalarDesc.AcquisitionId) ?? false);
            Assert.False(o.ProviderCaptures?.ContainsKey(scalarDesc.ProviderId) ?? false);
            Assert.Equal(MetricEvidence.Verified, o.EvidenceByAcquisition![maxDesc.AcquisitionId]);
            Assert.NotNull(o.ProviderCaptures![maxDesc.ProviderId]);
        });

        // Chart history matches point-by-point including Partial point at i=5
        var ordView = ordinaryReloaded.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        var tierView = tieredReloaded.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        var defs = MetricRegistry.Compose(descriptors, MetricDisplayPolicy.AllDetailsVisibleUnits);
        foreach (var targetDef in defs.Where(d => d.ProviderId is not null))
        {
            var ordHist = ordinaryReloaded.ReadChartHistory(ordView, ordView.Colonies[0].Key, targetDef, plainDir.Path);
            var tierHist = tieredReloaded.ReadChartHistory(tierView, tierView.Colonies[0].Key, targetDef, tieredDir.Path);
            Assert.Equal(ordHist.Points.Select(p => (p.Tick, p.Value?.Number, p.Value?.Coverage, p.Value?.Evidence)),
                tierHist.Points.Select(p => (p.Tick, p.Value?.Number, p.Value?.Coverage, p.Value?.Evidence)));
        }
    }

    [Fact]
    public void OptionalMod_BranchConflictAfterArchiving_PreservesFallbackValue()
    {
        using var plainDir = new TempDir();
        using var tieredDir = new TempDir();
        var archive = new MemoryArchive();
        var ordinary = new ProfileSession(new ProfileStore(plainDir.Path), () => Now);
        var tiered = new ProfileSession(new ProfileStore(tieredDir.Path), () => Now, archive);
        var provider = Provider(ColonySelection.Representative);
        Seed(ordinary, provider);
        Seed(tiered, provider);

        void AddBoth(MetricObservation o)
        {
            Add(ordinary, o);
            Add(tiered, JsonSerializer.Deserialize<MetricObservation>(Line(o), ProfileJson.CompactOptions)!);
        }

        // P (has provider) -> C1 (has provider): flush archives P (including provider)
        var pObs = WithCount("P", null, 10, 1);
        pObs.Metrics[provider.StableKey] = Count(42, 1);
        pObs.EvidenceByAcquisition = new() { [provider.AcquisitionId] = MetricEvidence.Experimental };
        var c1 = WithCount("C1", "P", 20, 2);
        c1.Metrics[provider.StableKey] = Count(50, 1);
        AddBoth(pObs);
        AddBoth(c1);
        tiered.Flush();
        Assert.True(tiered.Read(Parent).ArchivedMetrics);

        // Later a sibling branch B without the optional MOD is imported, then C1 conflicts.
        var b = WithCount("B", "P", 15, 3);
        AddBoth(b);
        var c1Conflict = WithCount("C1", "P", 20, 99);
        c1Conflict.PayloadHash = "different-hash";
        AddBoth(c1Conflict);

        ordinary.Flush();
        tiered.Flush();
        Assert.Equal(Rows(ordinary), Rows(tiered));
        var restoredRow = tiered.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits)
            .Numeric.Colonies.Single().Rows.Single(r => r.Definition.Key == provider.StableKey);
        Assert.Equal(42, restoredRow.Value.Number);
        Assert.Equal(MetricCoverage.Partial, restoredRow.Value.Coverage);
        Assert.Equal(MetricEvidence.Experimental, restoredRow.Value.Evidence);
    }

    [Fact]
    public void OptionalMod_RemovalReadditionAndRewind_MatchOrdinaryProfile()
    {
        using var plainDir = new TempDir();
        using var tieredDir = new TempDir();
        var archive = new FileCheckpointArchive(tieredDir.File("archive"));
        var ordinary = new ProfileSession(new ProfileStore(plainDir.Path), () => Now);
        var tiered = new ProfileSession(new ProfileStore(tieredDir.Path), () => Now, archive);
        var provider = Provider(ColonySelection.Representative);
        Seed(ordinary, provider);
        Seed(tiered, provider);

        void AddBoth(MetricObservation o)
        {
            Add(ordinary, o);
            Add(tiered, JsonSerializer.Deserialize<MetricObservation>(Line(o), ProfileJson.CompactOptions)!);
        }

        MetricObservation Step(string key, string? parent, int tick, int count, int? modVal)
        {
            var o = WithCount(key, parent, tick, count);
            if (modVal is { } mv)
            {
                o.Metrics[provider.StableKey] = Count(mv, 1);
                o.EvidenceByAcquisition = new() { [provider.AcquisitionId] = MetricEvidence.Verified };
            }
            return o;
        }

        // Phase 1: MOD present (s1 -> s2 -> s3)
        AddBoth(Step("s1", null, 100, 1, 10));
        AddBoth(Step("s2", "s1", 200, 2, 20));
        AddBoth(Step("s3", "s2", 300, 3, 30));
        tiered.Flush();

        // Phase 2: MOD removed (s4 -> s5), s3 must keep modVal=30 so fallback works
        AddBoth(Step("s4", "s3", 400, 4, null));
        AddBoth(Step("s5", "s4", 500, 5, null));
        tiered.Flush();
        Assert.Equal(Rows(ordinary), Rows(tiered));

        // Phase 3: MOD re-added (s6)
        AddBoth(Step("s6", "s5", 600, 6, 35));
        tiered.Flush();
        Assert.Equal(Rows(ordinary), Rows(tiered));

        // Phase 4: Rewind to s2 and save s7 (earlier tick than s6, then later s8 > s6)
        AddBoth(Step("s7", "s2", 350, 10, 25));
        tiered.Flush();
        Assert.Equal(Rows(ordinary), Rows(tiered));

        AddBoth(Step("s8", "s7", 700, 11, 28));
        ordinary.Flush();
        tiered.Flush();
        Assert.Equal(Rows(ordinary), Rows(tiered));

        var def = MetricRegistry.Compose([provider], MetricDisplayPolicy.AllDetailsVisibleUnits).Single(d => d.Key == provider.StableKey);
        var ov = ordinary.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        var tv = tiered.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        var oh = ordinary.ReadChartHistory(ov, ov.Colonies[0].Key, def, plainDir.Path);
        var th = tiered.ReadChartHistory(tv, tv.Colonies[0].Key, def, tieredDir.Path);
        Assert.Equal(oh.Points.Select(p => (p.Tick, p.Value?.Number, p.ConnectToPrevious)),
            th.Points.Select(p => (p.Tick, p.Value?.Number, p.ConnectToPrevious)));
    }

    [Fact]
    public void OptionalMod_ArchiveCorruptionAndOldSchemaV1Upgrade_HandledSafely()
    {
        using var dir = new TempDir();
        var archiveDir = dir.File("archive");
        var archive = new FileCheckpointArchive(archiveDir);
        var provider = Provider(ColonySelection.Representative);

        // Start from a schemaVersion 1 legacy profile
        File.WriteAllText(dir.File("profile.json"), "{\"schemaVersion\":1,\"saveObservations\":[],\"liveCampaigns\":[]}");
        var session = new ProfileSession(new ProfileStore(dir.Path), () => Now, archive);
        Seed(session, provider);
        var p = WithCount("P", null, 100, 1);
        p.Metrics[provider.StableKey] = Count(15, 1);
        var c = WithCount("C", "P", 200, 2);
        c.Metrics[provider.StableKey] = Count(25, 1);
        Add(session, p);
        Add(session, c);
        session.Flush();
        Assert.True(session.Read(Parent).ArchivedMetrics);

        // Corrupt the archive file (append partial JSON line, then delete file for conflict test)
        var archiveFile = Assert.Single(Directory.GetFiles(archiveDir));
        File.AppendAllText(archiveFile, "{\"corrupt\":");
        var view = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits);
        var def = MetricRegistry.Compose([provider], MetricDisplayPolicy.AllDetailsVisibleUnits).Single(d => d.Key == provider.StableKey);
        var hist = session.ReadChartHistory(view, view.Colonies[0].Key, def, dir.Path);
        Assert.Equal(new double?[] { 15, 25 }, hist.Points.Select(pt => pt.Value?.Number));

        // Now delete archive and invalidate child: must degrade cleanly to Unknown, never 0
        File.Delete(archiveFile);
        var conflict = WithCount("C", "P", 200, 2);
        conflict.PayloadHash = "changed";
        Add(session, conflict);
        var row = session.GetView(new StatsOptions(), MetricDisplayPolicy.AllDetailsVisibleUnits)
            .Numeric.Colonies.Single().Rows.Single(r => r.Definition.Key == provider.StableKey);
        Assert.Null(row.Value.Number);
        Assert.Equal(MetricCoverage.Unknown, row.Value.Coverage);
    }

    [Fact]
    public void Plan_KeepsMetadataWhenUnknownKeysRemainInAncestor()
    {
        var (profile, parent, child) = Pair();
        var provider = Provider(ColonySelection.Representative);
        ProfileMutations.RememberModMetrics(profile, new[] { provider });
        parent.Metrics.Add(provider.StableKey, Count(4, 1));
        child.Metrics.Add(provider.StableKey, Count(5, 1));
        parent.Metrics.Add("mod.unknown.future.metric", Count(9, 1));
        parent.EvidenceByAcquisition = new() { [provider.AcquisitionId] = MetricEvidence.Experimental };
        parent.ProviderCaptures = new() { [provider.ProviderId] = new(provider.OwnerPackageId, "1.0", "1.0") };

        var step = Assert.Single(CheckpointArchivePlanner.Plan(profile));
        CheckpointArchivePlanner.Apply(step);

        Assert.DoesNotContain(provider.StableKey, parent.Metrics.Keys);
        Assert.Contains("mod.unknown.future.metric", parent.Metrics.Keys);
        // Because an unknown key remains, metadata cannot be proved unused and must stay
        Assert.True(parent.EvidenceByAcquisition.ContainsKey(provider.AcquisitionId));
        Assert.True(parent.ProviderCaptures.ContainsKey(provider.ProviderId));
    }

    [Fact]
    public void Plan_KeepsSharedSiblingMetadataAndProviderCaptureExtensionData()
    {
        var (profile, parent, child) = Pair();
        var repDesc = Provider(ColonySelection.Representative);
        var sharedMaxDesc = repDesc with
        {
            MetricName = "peak_counter",
            Selection = ColonySelection.ObservationMax,
            Overall = OverallAggregation.Max,
        };
        var extDesc = new ModMetricDescriptor
        {
            OwnerPackageId = "test.ext",
            ProviderId = "test.ext.provider",
            FamilyId = "ext",
            MetricName = "val",
            Revision = 1,
            Source = MetricObservationSource.Checkpoint,
            Selection = ColonySelection.Representative,
            Overall = OverallAggregation.Sum,
            LabelKey = "L",
            DescriptionKey = "D",
        };
        ProfileMutations.RememberModMetrics(profile, new[] { repDesc, sharedMaxDesc, extDesc });

        parent.Metrics[repDesc.StableKey] = Count(1, 1);
        child.Metrics[repDesc.StableKey] = Count(2, 1);
        parent.Metrics[sharedMaxDesc.StableKey] = Count(10, 1);
        child.Metrics[sharedMaxDesc.StableKey] = Count(5, 1);
        parent.Metrics[extDesc.StableKey] = Count(3, 1);
        child.Metrics[extDesc.StableKey] = Count(4, 1);

        var extra = JsonSerializer.SerializeToElement("keep-me");
        parent.EvidenceByAcquisition = new(StringComparer.Ordinal)
        {
            [repDesc.AcquisitionId] = MetricEvidence.Verified,
            [extDesc.AcquisitionId] = MetricEvidence.Verified,
        };
        parent.ProviderCaptures = new(StringComparer.Ordinal)
        {
            [repDesc.ProviderId] = new(repDesc.OwnerPackageId, "1.0", "1.0"),
            [extDesc.ProviderId] = new(extDesc.OwnerPackageId, "1.0", "1.0")
            {
                ExtensionData = new() { ["future"] = extra },
            },
        };

        var step = Assert.Single(CheckpointArchivePlanner.Plan(profile));
        CheckpointArchivePlanner.Apply(step);

        Assert.DoesNotContain(repDesc.StableKey, parent.Metrics.Keys);
        Assert.DoesNotContain(extDesc.StableKey, parent.Metrics.Keys);
        Assert.Contains(sharedMaxDesc.StableKey, parent.Metrics.Keys);
        // Shared unit/provider with remaining sharedMaxDesc is kept
        Assert.True(parent.EvidenceByAcquisition.ContainsKey(repDesc.AcquisitionId));
        Assert.True(parent.ProviderCaptures.ContainsKey(repDesc.ProviderId));
        // ExtensionData on ProviderCapture protects it while its EvidenceByAcquisition was trimmed
        Assert.False(parent.EvidenceByAcquisition.ContainsKey(extDesc.AcquisitionId));
        Assert.True(parent.ProviderCaptures.ContainsKey(extDesc.ProviderId));
    }

    [Fact]
    public void Apply_SkipsMetadataDropsWhenAnyMetricDropWasSkipped()
    {
        var (profile, parent, child) = Pair();
        var provider = Provider(ColonySelection.Representative);
        ProfileMutations.RememberModMetrics(profile, new[] { provider });
        parent.Metrics.Add(provider.StableKey, Count(4, 1));
        child.Metrics.Add(provider.StableKey, Count(5, 1));
        parent.EvidenceByAcquisition = new() { [provider.AcquisitionId] = MetricEvidence.Verified };
        parent.ProviderCaptures = new() { [provider.ProviderId] = new(provider.OwnerPackageId, "1.0", "1.0") };

        var step = Assert.Single(CheckpointArchivePlanner.Plan(profile));
        // Mutate BuiltIn entry after planning so that one drop is skipped while provider drop succeeds
        parent.Metrics[BuiltIn.StoredKey] = Count(99);
        CheckpointArchivePlanner.Apply(step);

        Assert.Contains(BuiltIn.StoredKey, parent.Metrics.Keys);
        Assert.DoesNotContain(provider.StableKey, parent.Metrics.Keys);
        Assert.True(parent.ArchivedMetrics);
        // Since not all planned drops were applied, metadata stays intact
        Assert.True(parent.EvidenceByAcquisition.ContainsKey(provider.AcquisitionId));
        Assert.True(parent.ProviderCaptures.ContainsKey(provider.ProviderId));
    }
}
