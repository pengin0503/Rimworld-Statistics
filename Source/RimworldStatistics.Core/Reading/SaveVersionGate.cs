namespace RimworldStatistics.Core;

/// <summary>
/// The one place that turns a save's game version into the schema questions the reader asks (S17). The reader used to compare
/// <c>MajorMinor(obs.GameVersion) == "1.7"</c> at every section, so admitting a future game version would have meant finding
/// and changing every copy. A read creates one gate after the meta section, and every section asks a named question here.
/// </summary>
/// <remarks>
/// To admit a future version, add its major.minor to <see cref="CurrentSchemaVersions"/> (and any counter/timed set it belongs
/// to) and add the version-specific schema checks the new sections need in this type. Nothing else in the reader compares a
/// version string. An unknown, malformed or future version answers every question with false: its 1.7-only sections are
/// skipped and its timed outputs stay off, exactly as before this type existed.
/// <para>
/// The sets are deliberately positive lists rather than "1.7 and later": a future schema may change any section, so each
/// version must be audited and admitted explicitly.
/// </para>
/// </remarks>
public sealed class SaveVersionGate
{
    /// <summary>Versions whose current (1.7) section schemas the reader has audited.</summary>
    private static readonly HashSet<string> CurrentSchemaVersions = new(StringComparer.Ordinal) { "1.7" };

    /// <summary>
    /// Versions where an omitted statsRecord counter is known to mean 0: 1.7 by StatsRecord.ExposeData (1.7.4985), 1.5 and
    /// 1.6 by real saves (an explicitly written raid or death counter is never 0 there, and greatestPopulation is never 3).
    /// </summary>
    private static readonly HashSet<string> CounterDefaultKnownVersions = new(StringComparer.Ordinal) { "1.5", "1.6", "1.7" };

    /// <summary>
    /// Versions whose recorder cadences (30000 ticks, and 60000 ticks for Adaptation) are verified: 1.7 by its Defs and IL,
    /// 1.5 and 1.6 by real saves (point counts match the ticks in every save examined). Other versions get no time-based outputs.
    /// </summary>
    private static readonly HashSet<string> TimedVersions = new(StringComparer.Ordinal) { "1.5", "1.6", "1.7" };

    /// <summary>A gate that answers false to every question (a save without a readable game version).</summary>
    public static SaveVersionGate Unknown { get; } = new(null);

    /// <summary>Parses <paramref name="gameVersion"/> once. Null, blank, garbage and future versions give <see cref="Unknown"/>-like answers.</summary>
    public static SaveVersionGate For(string? gameVersion) => new(HistoryMetricReader.MajorMinor(gameVersion));

    private SaveVersionGate(string? majorMinor) => MajorMinor = majorMinor;

    /// <summary>The parsed "major.minor" (e.g. "1.7"), or null when the version has none. Same rule as <see cref="HistoryMetricReader.MajorMinor"/>.</summary>
    public string? MajorMinor { get; }

    /// <summary>True for the versions whose 1.7-only sections (researchManager, entityCodex, taleManager, maps, world objects, ideoManager, bossgroup, historical salvage) are audited.</summary>
    public bool SupportsCurrentSchema => MajorMinor is { } v && CurrentSchemaVersions.Contains(v);

    /// <summary>True for the versions where an omitted statsRecord counter means 0.</summary>
    public bool CounterDefaultsKnown => MajorMinor is { } v && CounterDefaultKnownVersions.Contains(v);

    /// <summary>True for the versions whose history-recorder cadence is verified, so time-based outputs may be built.</summary>
    public bool IsTimedVersion => MajorMinor is { } v && TimedVersions.Contains(v);
}
