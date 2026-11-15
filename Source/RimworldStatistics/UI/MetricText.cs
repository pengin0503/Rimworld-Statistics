using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.UI;

/// <summary>Shared UI text for registered metrics; Details remains the source of formatting rules.</summary>
internal static class MetricText
{
    private static readonly Dictionary<ModContentPack, Dictionary<string, Def>> DefsByPack = new();

    public static string Breakdown(MetricDefinition definition, string key, bool useThingLabel = false)
    {
        if (MetricDisplayPolicy.BreakdownLabelKey(definition, key) is { } translation)
            return translation.Translate();
        if (!OptionalTransitionRecording.TryParseQualifiedDefKey(key, out var owner, out var name))
            return useThingLabel ? DefDatabase<ThingDef>.GetNamedSilentFail(key)?.label ?? key : key;

        var pack = LoadedModManager.RunningMods.FirstOrDefault(mod =>
            string.Equals(mod.PackageId, owner, StringComparison.OrdinalIgnoreCase));
        if (pack is null)
            return key;

        if (!DefsByPack.TryGetValue(pack, out var defs))
        {
            defs = new Dictionary<string, Def>(StringComparer.Ordinal);
            var ambiguous = new HashSet<string>(StringComparer.Ordinal);
            foreach (var def in pack.AllDefs)
            {
                if (string.IsNullOrWhiteSpace(def.label) || ambiguous.Contains(def.defName))
                    continue;
                if (!defs.TryAdd(def.defName, def))
                {
                    defs.Remove(def.defName);
                    ambiguous.Add(def.defName);
                }
            }
            DefsByPack.Add(pack, defs);
        }
        return defs.TryGetValue(name!, out var match) ? match.label : key;
    }

    public static string Label(MetricDefinition definition) => DetailsTab.DefinitionLabel(definition);
    public static string Format(MetricDefinition definition, NumericValue value) => DetailsTab.Format(definition, value);
    public static string Tip(NumericRow row, StatsResult stats) => DetailsTab.OverallTip(row, stats);
    public static string BasicLabel(string key) => OverviewTab.BasicLabel(key);
    public static string State(MetricDefinition definition, MetricCoverage coverage)
        => DetailsTab.CoverageLabel(definition, coverage);

    /// <summary>
    /// A tooltip line with the amount per game day over the value's own recorded days (<see cref="MetricRates"/>), or an
    /// empty string when the value is not an additive total recorded completely over a known interval.
    /// </summary>
    public static string PerDay(MetricDefinition definition, NumericValue value)
    {
        if (MetricRates.PerDay(definition, value) is not { } rate)
            return "";
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var number = rate.PerDay.ToString(rate.PerDay >= 100 ? "N0" : rate.PerDay >= 1 ? "N1" : "N2", inv);
        if (definition.Unit == MetricUnit.Nutrition)
            number = "RWS_Unit_Nutrition".Translate(number);
        return "\n" + (string)"RWS_PerDay".Translate(number, rate.Days.ToString("N1", inv));
    }
}
