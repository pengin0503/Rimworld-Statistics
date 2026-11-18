namespace RimworldStatistics.Core;

/// <summary>State that ending traces are judged from; filled from a save file or from the running game.</summary>
public sealed class TraceInputs
{
    public int ColonistsLaunched { get; init; }
    public bool RoyalAscentSucceeded { get; init; }
    public bool MechhiveSucceeded { get; init; }
    public bool MechanoidsDeactivated { get; init; }
    public string? AnomalyLevelDef { get; init; }

    /// <summary>Types in the save's lineageEndings (empty for a save made before the mod was installed).</summary>
    public IReadOnlyCollection<EndingType> LineageTypes { get; init; } = Array.Empty<EndingType>();
}

/// <summary>Evidence in a save that an ending was reached at least once (spec §4.3, §7.3).</summary>
public static class EndingTraces
{
    public static List<EndingType> Detect(TraceInputs i)
    {
        var found = new HashSet<EndingType>();
        if (i.ColonistsLaunched >= 1)
            found.Add(i.RoyalAscentSucceeded ? EndingType.AmbiguousLaunch : EndingType.Ship);
        if (i.MechhiveSucceeded)
            found.Add(i.MechanoidsDeactivated ? EndingType.CerebrexDestroyed : EndingType.CerebrexScavenged);
        if (i.AnomalyLevelDef == "Embraced")
            found.Add(EndingType.VoidEmbraced);
        else if (i.AnomalyLevelDef == "Disrupted")
            found.Add(EndingType.VoidDisrupted);

        // Traces the save's own ending list already explains are not counted again (spec §4.3).
        found.RemoveWhere(trace => i.LineageTypes.Any(recorded => Explains(recorded, trace)));
        return found.OrderBy(t => t).ToList();
    }

    /// <summary>
    /// Whether a recorded ending of type <paramref name="recorded"/> accounts for a save trace of type <paramref name="trace"/>
    /// (spec §4.3): any launch-style ending explains the launch traces, either Cerebrex ending the Cerebrex traces, and
    /// either void ending the void traces.
    /// </summary>
    public static bool Explains(EndingType recorded, EndingType trace)
        => recorded == trace || (Group(recorded) is { } g && g == Group(trace));

    /// <summary>
    /// Selects the representative record for a single <c>(Type, Ordinal)</c> group, preferring higher departed count,
    /// live play over seeded trace when departed is tied (including 0), and non-dev-mode over dev-mode.
    /// </summary>
    public static EndingEvent Representative(IEnumerable<EndingEvent> group)
        => group.OrderByDescending(e => e.Departed).ThenBy(e => e.Source).ThenBy(e => e.DevMode).First();

    /// <summary>
    /// Returns save traces on <paramref name="colony"/> that are not already explained by any recorded ending
    /// (including dev-mode endings hidden when dev-mode display is off) or recovered lineage ending.
    /// </summary>
    public static EndingType[] Unexplained(Colony? colony)
    {
        if (colony is null || colony.Traces.Count == 0)
            return Array.Empty<EndingType>();
        return colony.Traces
            .Where(trace => !colony.Endings.Any(e => Explains(e.Type, trace))
                && !colony.RecoveredEndings.Any(e => Explains(e.Type, trace)))
            .OrderBy(t => t)
            .ToArray();
    }

    /// <summary>Earliest save timestamp carrying evidence of <paramref name="trace"/> (including explained-trace fallback).</summary>
    public static DateTime? FirstTraceUtc(Colony colony, EndingType trace)
        => colony.Observations
            .Where(o => o.EndingTraces.Contains(trace) || o.ExplainedTraces.Contains(trace))
            .Select(o => o.LastWriteUtc != default ? (DateTime?)o.LastWriteUtc : null)
            .Where(t => t.HasValue)
            .Min();

    private static int? Group(EndingType t) => t switch
    {
        EndingType.Ship or EndingType.RoyalAscent or EndingType.AmbiguousLaunch => 0,
        EndingType.CerebrexScavenged or EndingType.CerebrexDestroyed => 1,
        EndingType.VoidEmbraced or EndingType.VoidDisrupted => 2,
        _ => null,
    };
}
