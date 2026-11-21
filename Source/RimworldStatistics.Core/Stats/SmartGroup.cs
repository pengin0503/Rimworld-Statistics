namespace RimworldStatistics.Core;

/// <summary>Condition category for dynamic, rule-based colony scopes ("Smart Groups").</summary>
public enum SmartGroupKind
{
    PermadeathOnly,
    ReloadAnytimeOnly,
    EndingReached,
    MinSurvivalDays,
    Storyteller,
    Difficulty,
    Scenario,
    StartingBiome,
}

/// <summary>
/// Immutable specification of a dynamic condition-based colony scope. Evaluated dynamically against a profile's colonies
/// without persisting colony membership lists, so newly imported or progressing colonies automatically join or leave.
/// </summary>
public sealed record SmartGroupSpec(SmartGroupKind Kind, string Value = "", int MinDays = 0)
{
    public const string Prefix = "smart:";

    /// <summary>Standard survival-day presets offered in the scope menu.</summary>
    public static IReadOnlyList<int> DefaultDayThresholds { get; } = [15, 60, 180, 300];

    public string Id => Kind switch
    {
        SmartGroupKind.PermadeathOnly => "smart:permadeath",
        SmartGroupKind.ReloadAnytimeOnly => "smart:reload_anytime",
        SmartGroupKind.EndingReached => "smart:ending_reached",
        SmartGroupKind.MinSurvivalDays => $"smart:days:{Math.Max(1, MinDays)}",
        SmartGroupKind.Storyteller => EncodeValue("storyteller", Value),
        SmartGroupKind.Difficulty => EncodeValue("difficulty", Value),
        SmartGroupKind.Scenario => EncodeValue("scenario", Value),
        SmartGroupKind.StartingBiome => EncodeValue("biome", Value),
        _ => "smart:permadeath",
    };

    private static string EncodeValue(string tag, string value)
        => string.Concat(Prefix, tag, ":", value.Length, ":", value);

    public static bool IsSmartGroupId(string? id)
        => id is not null && id.StartsWith(Prefix, StringComparison.Ordinal);

    public static bool TryParse(string? id, out SmartGroupSpec? spec)
    {
        spec = null;
        if (string.IsNullOrEmpty(id) || !id.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        var body = id.AsSpan(Prefix.Length);
        if (body.SequenceEqual("permadeath"))
        {
            spec = new SmartGroupSpec(SmartGroupKind.PermadeathOnly);
            return true;
        }
        if (body.SequenceEqual("reload_anytime"))
        {
            spec = new SmartGroupSpec(SmartGroupKind.ReloadAnytimeOnly);
            return true;
        }
        if (body.SequenceEqual("ending_reached"))
        {
            spec = new SmartGroupSpec(SmartGroupKind.EndingReached);
            return true;
        }
        if (body.StartsWith("days:", StringComparison.Ordinal))
        {
            if (int.TryParse(body["days:".Length..], System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var days) && days > 0)
            {
                spec = new SmartGroupSpec(SmartGroupKind.MinSurvivalDays, MinDays: days);
                return true;
            }
            return false;
        }

        if (TryDecodeValue(body, "storyteller:", out var storyteller))
        {
            spec = new SmartGroupSpec(SmartGroupKind.Storyteller, storyteller);
            return true;
        }
        if (TryDecodeValue(body, "difficulty:", out var difficulty))
        {
            spec = new SmartGroupSpec(SmartGroupKind.Difficulty, difficulty);
            return true;
        }
        if (TryDecodeValue(body, "scenario:", out var scenario))
        {
            spec = new SmartGroupSpec(SmartGroupKind.Scenario, scenario);
            return true;
        }
        if (TryDecodeValue(body, "biome:", out var biome))
        {
            spec = new SmartGroupSpec(SmartGroupKind.StartingBiome, biome);
            return true;
        }
        return false;
    }

    private static bool TryDecodeValue(ReadOnlySpan<char> body, string tagWithColon, out string value)
    {
        value = "";
        if (!body.StartsWith(tagWithColon, StringComparison.Ordinal))
            return false;
        var rest = body[tagWithColon.Length..];
        var colon = rest.IndexOf(':');
        if (colon <= 0)
            return false;
        if (!int.TryParse(rest[..colon], System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var expectedLength) || expectedLength <= 0)
            return false;
        var payload = rest[(colon + 1)..];
        if (payload.Length != expectedLength)
            return false;
        value = payload.ToString();
        return !string.IsNullOrWhiteSpace(value);
    }
}

public sealed record SmartGroupCandidate(SmartGroupSpec Spec, int ColonyCount);

/// <summary>Evaluates and discovers <see cref="SmartGroupSpec"/> scopes over recorded colonies.</summary>
public static class SmartGroupEvaluator
{
    public static bool Matches(SmartGroupSpec spec, ColonyStats colony, string? startingBiome = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(colony);
        return spec.Kind switch
        {
            SmartGroupKind.PermadeathOnly => colony.HasObservedPoint && colony.Permadeath == true,
            SmartGroupKind.ReloadAnytimeOnly => colony.HasObservedPoint && colony.Permadeath == false,
            SmartGroupKind.EndingReached => colony.EndingCounts.Values.Sum() > 0,
            SmartGroupKind.MinSurvivalDays => colony.HasObservedPoint
                && spec.MinDays > 0
                && colony.MaxTicks >= spec.MinDays * (long)StatFormat.TicksPerDay,
            SmartGroupKind.Storyteller => !string.IsNullOrWhiteSpace(spec.Value)
                && string.Equals(colony.Storyteller, spec.Value, StringComparison.Ordinal),
            SmartGroupKind.Difficulty => !string.IsNullOrWhiteSpace(spec.Value)
                && string.Equals(colony.Difficulty, spec.Value, StringComparison.Ordinal),
            SmartGroupKind.Scenario => !string.IsNullOrWhiteSpace(spec.Value)
                && string.Equals(colony.Scenario, spec.Value, StringComparison.Ordinal),
            SmartGroupKind.StartingBiome => !string.IsNullOrWhiteSpace(spec.Value)
                && string.Equals(startingBiome, spec.Value, StringComparison.Ordinal),
            _ => false,
        };
    }

    public static void Apply(
        SmartGroupSpec spec,
        Profile? profile,
        IReadOnlyList<Colony> colonies,
        StatsOptions options,
        IReadOnlyDictionary<string, string>? preResolvedBiomes = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(colonies);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyDictionary<string, string>? biomesByKey = preResolvedBiomes;
        if (spec.Kind == SmartGroupKind.StartingBiome && biomesByKey is null && profile is not null)
            biomesByKey = ResolveStartingBiomesByKey(profile, colonies);

        foreach (var colony in colonies)
        {
            var stats = StatsCalculator.CalculateColony(colony, options);
            var biome = biomesByKey is not null && biomesByKey.TryGetValue(colony.Key, out var b) ? b : null;
            colony.OutOfScope = !Matches(spec, stats, biome);
        }
    }

    /// <summary>
    /// Discovers dynamic smart group candidates across all non-excluded colonies in <paramref name="view"/>
    /// (regardless of which scope <paramref name="view"/> currently has active).
    /// </summary>
    public static IReadOnlyList<SmartGroupCandidate> Discover(StatsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var activeColonies = view.Stats.Colonies.Where(c => !c.Excluded).ToArray();
        var biomesByKey = ResolveStartingBiomesFromView(view);

        int CountFor(SmartGroupSpec spec)
            => activeColonies.Count(c => Matches(spec, c, biomesByKey.GetValueOrDefault(c.Key)));

        var list = new List<SmartGroupCandidate>
        {
            new(new SmartGroupSpec(SmartGroupKind.PermadeathOnly), CountFor(new SmartGroupSpec(SmartGroupKind.PermadeathOnly))),
            new(new SmartGroupSpec(SmartGroupKind.ReloadAnytimeOnly), CountFor(new SmartGroupSpec(SmartGroupKind.ReloadAnytimeOnly))),
            new(new SmartGroupSpec(SmartGroupKind.EndingReached), CountFor(new SmartGroupSpec(SmartGroupKind.EndingReached))),
        };

        foreach (var days in SmartGroupSpec.DefaultDayThresholds)
        {
            var spec = new SmartGroupSpec(SmartGroupKind.MinSurvivalDays, MinDays: days);
            list.Add(new SmartGroupCandidate(spec, CountFor(spec)));
        }

        void AddDistinctValues(SmartGroupKind kind, IEnumerable<string?> values)
        {
            foreach (var val in values
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal))
            {
                var spec = new SmartGroupSpec(kind, Value: val);
                var count = CountFor(spec);
                if (count > 0)
                    list.Add(new SmartGroupCandidate(spec, count));
            }
        }

        AddDistinctValues(SmartGroupKind.Storyteller, activeColonies.Select(c => c.Storyteller));
        AddDistinctValues(SmartGroupKind.Difficulty, activeColonies.Select(c => c.Difficulty));
        AddDistinctValues(SmartGroupKind.StartingBiome, activeColonies.Select(c => biomesByKey.GetValueOrDefault(c.Key)));
        AddDistinctValues(SmartGroupKind.Scenario, activeColonies.Select(c => c.Scenario));

        return list;
    }

    public static IReadOnlyDictionary<string, string> ResolveStartingBiomesFromView(StatsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.StartingBiomes.Count > 0)
            return view.StartingBiomes;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var colony in view.Numeric.Colonies)
        {
            var row = colony.Rows.FirstOrDefault(r => r.Definition.Key == ColonyStartSnapshot.Biome);
            if (row?.Value.Number != 1 || row.Breakdowns.Count != 1)
                continue;
            var child = row.Breakdowns[0];
            if (child.Value.Number == 1 && !string.IsNullOrWhiteSpace(child.Breakdown))
                result[colony.Key] = child.Breakdown;
        }
        return result;
    }

    internal static Dictionary<string, string> ResolveStartingBiomesByKey(Profile profile, IReadOnlyList<Colony> colonies)
        => ResolveStartingBiomesByKey(MetricCalculator.Attach(profile, colonies, out _), colonies);

    internal static Dictionary<string, string> ResolveStartingBiomesByKey(
        IReadOnlyDictionary<Colony, List<MetricObservation>> attached,
        IReadOnlyList<Colony> colonies)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var colony in colonies)
        {
            if (!attached.TryGetValue(colony, out var observations))
                continue;
            var latest = MetricObservationOrder.Latest(observations.Where(o =>
                o.Source == MetricObservationSource.SaveSnapshot && !o.Conflicted
                && o.Metrics.ContainsKey(ColonyStartSnapshot.Biome)));
            latest ??= MetricObservationOrder.Latest(observations.Where(o =>
                o.Source == MetricObservationSource.SaveFile && !o.Conflicted
                && o.Metrics.ContainsKey(ColonyStartSnapshot.Biome)));
            if (latest is null || !latest.Metrics.TryGetValue(ColonyStartSnapshot.Biome, out var entry))
                continue;
            if (!entry.Values.TryGetValue(MetricEntry.Scalar, out var scalar) || scalar.Number != 1)
                continue;
            var breakdowns = entry.Values.Where(kv => kv.Key != MetricEntry.Scalar && kv.Value.Number == 1).ToArray();
            if (breakdowns.Length == 1 && !string.IsNullOrWhiteSpace(breakdowns[0].Key))
                result[colony.Key] = breakdowns[0].Key;
        }
        return result;
    }
}
