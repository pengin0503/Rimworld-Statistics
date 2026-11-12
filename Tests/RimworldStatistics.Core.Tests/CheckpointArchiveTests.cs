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
    [InlineData(ColonySelection.Representative)]
    public void Plan_KeepsProviderFallbackAndNonRepresentativeDefinitions(ColonySelection selection)
    {
        var (profile, parent, child) = Pair();
        var descriptor = Provider(selection);
        ProfileMutations.RememberModMetrics(profile, new[] { descriptor });
        Assert.Single(profile.ModMetricDescriptors);
        parent.Metrics.Add(descriptor.StableKey, Count(4, 1));
        child.Metrics.Add(descriptor.StableKey, Count(5, 1));
        Assert.Equal(BuiltIn.StoredKey, Assert.Single(Assert.Single(CheckpointArchivePlanner.Plan(profile)).Drops).Key);
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
            => Missing || !Lines.TryGetValue(campaignId, out var lines) ? Array.Empty<MetricObservation>()
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
}
