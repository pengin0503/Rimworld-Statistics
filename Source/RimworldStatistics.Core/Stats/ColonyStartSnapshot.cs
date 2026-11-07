namespace RimworldStatistics.Core;

/// <summary>
/// Save-time *starting* tile biome. Only the immutable game startingTile is valid;
/// a currently owned home map or first loaded map would relabel a travelling colony.
/// DefName is used rather than translated labels so comparisons survive locale changes.
/// </summary>
public static class ColonyStartSnapshot
{
    public const string Biome = "colony.start.biome";

    public static Dictionary<string, MetricEntry> Build(string? startingBiomeDefName)
    {
        // Reject ambiguous or external labels. No source == unknown, never zero and
        // never the current home's biome as a fallback.
        var valid = !string.IsNullOrWhiteSpace(startingBiomeDefName)
            && startingBiomeDefName.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        var state = valid ? MetricCoverage.SnapshotOnly : MetricCoverage.Unknown;
        var entry = MetricEntry.Of(MetricKeys.Revision(Biome), new MetricValue
        {
            Coverage = state, IntegerValue = valid ? 1 : null,
        });
        if (valid)
            entry.Values[startingBiomeDefName!] = new MetricValue { Coverage = state, IntegerValue = 1 };
        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal) { [Biome] = entry };
    }
}
