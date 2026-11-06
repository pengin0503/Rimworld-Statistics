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
        var lineage = i.LineageTypes;
        if (lineage.Any(t => t is EndingType.Ship or EndingType.RoyalAscent or EndingType.AmbiguousLaunch))
        {
            found.Remove(EndingType.Ship);
            found.Remove(EndingType.AmbiguousLaunch);
        }
        if (lineage.Any(t => t is EndingType.CerebrexScavenged or EndingType.CerebrexDestroyed))
        {
            found.Remove(EndingType.CerebrexScavenged);
            found.Remove(EndingType.CerebrexDestroyed);
        }
        if (lineage.Any(t => t is EndingType.VoidEmbraced or EndingType.VoidDisrupted))
        {
            found.Remove(EndingType.VoidEmbraced);
            found.Remove(EndingType.VoidDisrupted);
        }
        return found.OrderBy(t => t).ToList();
    }
}
