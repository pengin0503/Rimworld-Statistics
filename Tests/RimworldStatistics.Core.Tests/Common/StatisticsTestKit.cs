namespace RimworldStatistics.Core.Tests.Common;

// The shared building blocks of the common statistics contract tests
// (docs/testing/2026-11-15-common-statistics-tests.md). Each helper wraps one production API, so a test reads like the
// game's own lifecycle: record events, save, load, import, show. Nothing here knows a particular MOD, and nothing
// needs game or MOD files.

/// <summary>Optional MOD statistics in the shapes the production code registers.</summary>
internal static class TestShapes
{
    public const string Owner = "example.owner";
    public const string Counter = "completed";
    public const string StoredUnits = "stored_units";

    /// <summary>
    /// The descriptor an integrated MOD counter gets on the game side (<c>OutcomeMetric.Count</c>): one "completed" counter
    /// per family, summed across colonies. The other parameters give the remaining shapes a descriptor accepts.
    /// </summary>
    public static ModMetricDescriptor CounterMetric(string family = "actions", bool breakdown = false, int revision = 1,
        MetricObservationSource source = MetricObservationSource.Checkpoint, OverallAggregation overall = OverallAggregation.Sum,
        ColonySelection selection = ColonySelection.Representative, string? dlc = null, string owner = Owner,
        params ModPackageRequirement[] requires) => new()
    {
        ProviderId = "rws." + family,
        OwnerPackageId = owner,
        FamilyId = family,
        MetricName = Counter,
        Source = source,
        Revision = revision,
        Category = MetricCategory.Colony,
        Unit = MetricUnit.Count,
        Overall = overall,
        Selection = selection,
        HasBreakdown = breakdown,
        RequiredDlc = dlc,
        RequiredPackages = [new ModPackageRequirement(owner), .. requires],
        LabelKey = "RWS_Test_" + family,
        DescriptionKey = "RWS_TestDesc_" + family,
    };

    /// <summary>An integrated MOD fractional quantity (<c>stored_units</c>), summed across colonies.</summary>
    public static ModMetricDescriptor RealMetric(string family = "quantities", int revision = 1) => new()
    {
        ProviderId = "rws." + family,
        OwnerPackageId = Owner,
        FamilyId = family,
        MetricName = StoredUnits,
        Source = MetricObservationSource.Checkpoint,
        Revision = revision,
        Category = MetricCategory.Work,
        Overall = OverallAggregation.Sum,
        Selection = ColonySelection.Representative,
        RequiredPackages = [new ModPackageRequirement(Owner)],
        LabelKey = "RWS_Test_" + family,
        DescriptionKey = "RWS_TestDesc_" + family,
    };

    /// <summary>The event family of <paramref name="descriptor"/>, built like the game side builds it.</summary>
    public static LiveFamily FamilyOf(ModMetricDescriptor descriptor) => new()
    {
        Name = descriptor.AcquisitionId,
        Revision = descriptor.Revision,
        RequiredDlc = descriptor.RequiredDlc,
        Build = output =>
        {
            if (descriptor.MetricName == StoredUnits)
            {
                output.Put(descriptor.StableKey, output.Value(real: output.State.Peek(StoredUnits)?.Sum ?? 0d));
                output.Into[descriptor.StableKey].Revision = output.State.Revision;
            }
            else
            {
                OptionalTransitionRecording.PutCount(output, Counter, descriptor.StableKey, descriptor.HasBreakdown);
            }
        },
    };

    /// <summary>A breakdown key the way an integrated MOD makes it: content owner plus DefName.</summary>
    public static string DefKey(string defName, string owner = "Ludeon.RimWorld")
        => OptionalTransitionRecording.QualifiedDefKey(owner, defName)!;
}

/// <summary>One colony: its identity and campaign id.</summary>
internal sealed class TestColony
{
    public TestColony(string name)
    {
        Name = name;
        Campaign = "campaign-" + name;
        Identity = new ColonyIdentity
        {
            SeedString = "seed-" + name, PersistentRandomValue = name.Length, StartingTile = name.Length + ",0",
            GameStartAbsTick = 0, FirstWealthBits = 7, StartingPawnIds = { "Thing_Human_" + name },
            ModCampaignId = Campaign,
        };
    }

    public string Name { get; }
    public string Campaign { get; }
    public ColonyIdentity Identity { get; }
}

/// <summary>A checkpoint a successful save wrote, and the profile observation the game makes of it.</summary>
internal sealed record SavedCheckpoint(string Id, int Tick, string Json, MetricObservation Observation);

/// <summary>
/// One play session of a colony with some event families recording (<see cref="LiveMetricAccumulator"/>): record, then save.
/// A session starts a new game or loads a saved checkpoint, possibly with other families installed (a MOD added or removed).
/// </summary>
internal sealed class GameSession
{
    private static readonly DateTime T0 = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);
    private readonly LiveMetricAccumulator _accumulator;
    private readonly TestColony _colony;
    private readonly IReadOnlyList<LiveFamily> _families;

    private GameSession(TestColony colony, IReadOnlyList<LiveFamily> families, LiveMetricAccumulator accumulator)
    {
        _colony = colony;
        _families = families;
        _accumulator = accumulator;
    }

    public static GameSession NewGame(TestColony colony, params LiveFamily[] families)
        => new(colony, families, LiveMetricAccumulator.StartNew(families.Select(f => f.Spec).ToArray(), 0));

    /// <summary>Loads <paramref name="save"/> at its own tick, with <paramref name="families"/> installed this time.</summary>
    public static GameSession Load(TestColony colony, SavedCheckpoint save, params LiveFamily[] families)
        => new(colony, families, LiveMetricAccumulator.Resume(save.Json, families.Select(f => f.Spec).ToArray(), save.Tick));

    public LiveMetricAccumulator Accumulator => _accumulator;

    public FamilyState State(ModMetricDescriptor descriptor)
        => _accumulator.Family(descriptor.AcquisitionId) ?? throw new InvalidOperationException(descriptor.AcquisitionId + " is not recording");

    /// <summary>One committed outcome, recorded as an integrated MOD records it.</summary>
    public bool Record(ModMetricDescriptor descriptor, long amount = 1, string? key = null)
        => OptionalTransitionRecording.RecordCount(State(descriptor), TestShapes.Counter, amount, key, descriptor.HasBreakdown);

    /// <summary>One committed fractional outcome, recorded as an integrated MOD records it.</summary>
    public bool RecordReal(ModMetricDescriptor descriptor, double amount)
        => OptionalTransitionRecording.RecordReal(State(descriptor), TestShapes.StoredUnits, amount);

    /// <summary>
    /// A successful save at <paramref name="tick"/>. The observation is built with the families this session knows, as the
    /// game builds it right after the save.
    /// </summary>
    public SavedCheckpoint Save(int tick, string id, params string[] activeDlcs)
    {
        var draft = _accumulator.PrepareSave(tick, T0.AddSeconds(tick), id)
            ?? throw new InvalidOperationException("Nothing to save");
        _accumulator.Commit(draft);
        return new SavedCheckpoint(id, tick, draft.Json, CheckpointMetrics.ToObservation(draft.Payload!, draft.Json,
            _colony.Identity, "1.7.4985", activeDlcs.Length == 0 ? null : [.. activeDlcs], _families));
    }
}

/// <summary>A profile with its colonies, as the statistics window builds it.</summary>
internal sealed class TestProfile
{
    private static readonly DateTime T0 = new(2026, 11, 15, 0, 0, 0, DateTimeKind.Utc);

    public TestProfile(Profile? profile = null) => Profile = profile ?? new Profile();

    public Profile Profile { get; private set; }

    /// <summary>The colony was played (its live campaign is known) up to <paramref name="tick"/>.</summary>
    public TestProfile Played(TestColony colony, int tick)
    {
        ProfileMutations.ApplyLiveObservation(Profile, colony.Campaign, colony.Identity,
            new ObservedValues { TicksGame = tick }, new DisplayInfo { FactionName = colony.Name }, T0.AddSeconds(tick), "1.7.4985");
        return this;
    }

    /// <summary>The player excluded the colony from the totals.</summary>
    public TestProfile Exclude(TestColony colony)
    {
        var key = Assert.Single(CampaignMatcher.BuildColonies(Profile), c => c.CampaignIds().Contains(colony.Campaign)).Key;
        ProfileMutations.SetExcluded(Profile, [key], true);
        return this;
    }

    public TestProfile Remember(params ModMetricDescriptor[] descriptors)
    {
        ProfileMutations.RememberModMetrics(Profile, descriptors);
        return this;
    }

    public TestProfile Import(params MetricObservation[] observations)
    {
        foreach (var observation in observations)
            ProfileMutations.UpsertMetricObservation(Profile, observation);
        return this;
    }

    public TestProfile Import(params SavedCheckpoint[] saves) => Import(saves.Select(s => s.Observation).ToArray());

    /// <summary>Writes the profile and reads it back, as the next game start does.</summary>
    public TestProfile Reopen()
    {
        Profile = ProfileJson.Deserialize(ProfileJson.Serialize(Profile));
        return this;
    }

    /// <summary>The details view with every implemented and every remembered MOD output visible.</summary>
    public ProfileView View()
    {
        var colonies = CampaignMatcher.BuildColonies(Profile);
        var definitions = MetricRegistry.Compose(Profile.ModMetricDescriptors, MetricDisplayPolicy.AllDetailsVisibleUnits);
        var view = MetricCalculator.BuildView(Profile, colonies, StatsCalculator.Calculate(colonies, new StatsOptions()), definitions);
        return new ProfileView(view, colonies);
    }
}

/// <summary>Rows of a built view, looked up by colony and output key.</summary>
internal sealed record ProfileView(NumericView View, IReadOnlyList<Colony> Colonies)
{
    public NumericColony Colony(TestColony colony)
    {
        var key = Assert.Single(Colonies, c => c.CampaignIds().Contains(colony.Campaign)).Key;
        return Assert.Single(View.Colonies, c => c.Key == key);
    }

    public NumericRow Row(TestColony colony, string key) => Assert.Single(Colony(colony).Rows, r => r.Definition.Key == key);

    public NumericRow Overall(string key) => Assert.Single(View.Overall, r => r.Definition.Key == key);

    public bool HasOverall(string key) => View.Overall.Any(r => r.Definition.Key == key);
}

/// <summary>
/// Every combination of a rule's boolean conditions, so a rule test cannot leave out the row that matters (a failed action,
/// a non-player owner, a reservation that was never completed).
/// </summary>
internal static class ConditionMatrix
{
    public static IEnumerable<bool[]> All(int conditions)
    {
        if (conditions is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(conditions));
        for (var bits = 0; bits < 1 << conditions; bits++)
            yield return Enumerable.Range(0, conditions).Select(i => (bits & (1 << i)) != 0).ToArray();
    }

    /// <summary>Asserts that <paramref name="rule"/> agrees with <paramref name="expected"/> on every combination.</summary>
    public static void AssertAgrees(int conditions, Func<bool[], bool> rule, Func<bool[], bool> expected)
    {
        var wrong = All(conditions).Where(c => rule(c) != expected(c))
            .Select(c => "(" + string.Join(", ", c.Select(b => b ? "T" : "F")) + ") → " + (rule(c) ? "counted" : "not counted"))
            .ToArray();
        Assert.True(wrong.Length == 0, "Wrong for: " + string.Join("; ", wrong));
    }

    /// <summary>The usual outcome gate: counted only when every condition holds.</summary>
    public static void AssertOnlyWhenAll(int conditions, Func<bool[], bool> rule)
        => AssertAgrees(conditions, rule, c => c.All(b => b));
}

/// <summary>Managed memory allocated on the calling thread, which other tests running in parallel do not change.</summary>
internal static class Allocations
{
    public static long OnThisThread(Action action)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
