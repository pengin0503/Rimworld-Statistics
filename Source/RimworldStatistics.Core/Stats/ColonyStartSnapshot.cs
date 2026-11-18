using System.Globalization;

namespace RimworldStatistics.Core;

/// <summary>
/// Save-time *starting* tile biome and immutable <c>&lt;world&gt;&lt;info&gt;</c> generation settings.
/// Only the immutable game startingTile is valid for biome; a currently owned home map or first loaded map
/// would relabel a travelling colony. DefName and enum identifiers are stored rather than translated labels
/// so comparisons survive locale changes.
/// </summary>
public static class ColonyStartSnapshot
{
    public const string Biome = "colony.start.biome";
    public const string WorldTemperature = "colony.world.temperature";
    public const string WorldRainfall = "colony.world.rainfall";
    public const string WorldPollution = "colony.world.pollution";
    public const string InitialMapSize = "colony.world.initial_map_size";

    public static readonly IReadOnlySet<string> ValidTemperatures = new HashSet<string>(StringComparer.Ordinal)
    {
        "VeryCold", "Cold", "LittleBitColder", "Normal", "LittleBitWarmer", "Hot", "VeryHot",
    };

    public static readonly IReadOnlySet<string> ValidRainfalls = new HashSet<string>(StringComparer.Ordinal)
    {
        "AlmostNone", "Little", "LittleBitLess", "Normal", "LittleBitMore", "High", "VeryHigh",
    };

    public static Dictionary<string, MetricEntry> Build(
        string? startingBiomeDefName,
        string? overallTemperature = null,
        string? overallRainfall = null,
        double? worldPollution = null,
        int? initialMapWidth = null,
        int? initialMapHeight = null,
        bool? biotechActive = null)
    {
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);

        // Reject ambiguous or external labels. No source == unknown, never zero and
        // never the current home's biome as a fallback.
        var validBiome = !string.IsNullOrWhiteSpace(startingBiomeDefName)
            && startingBiomeDefName.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        var biomeState = validBiome ? MetricCoverage.SnapshotOnly : MetricCoverage.Unknown;
        var biomeEntry = MetricEntry.Of(MetricKeys.Revision(Biome), new MetricValue
        {
            Coverage = biomeState, IntegerValue = validBiome ? 1 : null,
        });
        if (validBiome)
            biomeEntry.Values[startingBiomeDefName!] = new MetricValue { Coverage = biomeState, IntegerValue = 1 };
        result[Biome] = biomeEntry;

        foreach (var (key, entry) in BuildWorldInfo(overallTemperature, overallRainfall, worldPollution, initialMapWidth, initialMapHeight, biotechActive))
            result[key] = entry;

        return result;
    }

    public static Dictionary<string, MetricEntry> BuildWorldInfo(
        string? overallTemperature,
        string? overallRainfall,
        double? worldPollution,
        int? initialMapWidth,
        int? initialMapHeight,
        bool? biotechActive)
    {
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);

        PutEnumBucket(WorldTemperature, overallTemperature, ValidTemperatures);
        PutEnumBucket(WorldRainfall, overallRainfall, ValidRainfalls);

        if (biotechActive == false)
        {
            result[WorldPollution] = MetricEntry.Of(MetricKeys.Revision(WorldPollution), new MetricValue
            {
                Coverage = MetricCoverage.NotApplicable,
            });
        }
        else if (biotechActive == true && worldPollution is { } pol && double.IsFinite(pol) && pol is >= 0.0 and <= 1.0)
        {
            result[WorldPollution] = MetricEntry.Of(MetricKeys.Revision(WorldPollution), new MetricValue
            {
                Coverage = MetricCoverage.SnapshotOnly,
                RealValue = pol,
            });
        }
        else
        {
            result[WorldPollution] = MetricEntry.Of(MetricKeys.Revision(WorldPollution), new MetricValue
            {
                Coverage = MetricCoverage.Unknown,
            });
        }

        if (initialMapWidth is > 0 and <= 10000 and { } w && initialMapHeight is > 0 and <= 10000 and { } h)
        {
            var cells = (long)w * h;
            var bucket = string.Create(CultureInfo.InvariantCulture, $"{w}x{h}");
            var sizeEntry = MetricEntry.Of(MetricKeys.Revision(InitialMapSize), new MetricValue
            {
                Coverage = MetricCoverage.SnapshotOnly,
                IntegerValue = cells,
            });
            sizeEntry.Values[bucket] = new MetricValue
            {
                Coverage = MetricCoverage.SnapshotOnly,
                IntegerValue = cells,
            };
            result[InitialMapSize] = sizeEntry;
        }
        else
        {
            result[InitialMapSize] = MetricEntry.Of(MetricKeys.Revision(InitialMapSize), new MetricValue
            {
                Coverage = MetricCoverage.Unknown,
            });
        }

        return result;

        void PutEnumBucket(string key, string? token, IReadOnlySet<string> allowed)
        {
            var trimmed = token?.Trim();
            var valid = !string.IsNullOrEmpty(trimmed) && allowed.Contains(trimmed);
            var cov = valid ? MetricCoverage.SnapshotOnly : MetricCoverage.Unknown;
            var e = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
            {
                Coverage = cov,
                IntegerValue = valid ? 1 : null,
            });
            if (valid)
                e.Values[trimmed!] = new MetricValue { Coverage = cov, IntegerValue = 1 };
            result[key] = e;
        }
    }
}
