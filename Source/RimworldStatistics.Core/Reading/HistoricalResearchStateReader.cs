using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace RimworldStatistics.Core;

/// <summary>
/// Provisional 1.7 saved-research recovery based on the audited 1.7.4985
/// official resolved ResearchProjectDefs. All inferred values are PARTIAL,
/// because mod patches and exact save-build DefDatabase are not known.
/// </summary>
public static class HistoricalResearchStateReader
{
    private const int MaxProjects = 8192;
    private const string ResourceName = "RimworldStatistics.VanillaResearchCostCatalog.json";

    private sealed record Def(string Name, float Cost, int Tech, string PackageId);
    private static readonly Lazy<IReadOnlyList<Def>?> Definitions = new(LoadDefinitions);

    public static int ReferenceCount => Definitions.Value?.Count ?? 0;

    private static IReadOnlyList<Def>? LoadDefinitions()
    {
        try
        {
            using var stream = typeof(HistoricalResearchStateReader).Assembly.GetManifestResourceStream(ResourceName);
            if (stream is null)
                return null;
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.GetProperty("schema").GetInt32() != 1
                || root.GetProperty("sourceVersion").GetString() != "1.7.4985 rev1269")
                return null;
            var entries = new List<Def>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in root.GetProperty("projects").EnumerateArray())
            {
                if (item.GetArrayLength() != 4)
                    return null;
                var name = item[0].GetString();
                var cost = item[1].GetSingle();
                var tech = item[2].GetString();
                var dlc = item[3].GetString();
                var technology = tech switch
                {
                    "Neolithic" => 2,
                    "Medieval" => 3,
                    "Industrial" => 4,
                    "Spacer" => 5,
                    "Ultra" => 6,
                    _ => -1
                };
                var packageId = dlc switch
                {
                    "Core" => "ludeon.rimworld",
                    "Royalty" => "ludeon.rimworld.royalty",
                    "Ideology" => "ludeon.rimworld.ideology",
                    "Biotech" => "ludeon.rimworld.biotech",
                    "Odyssey" => "ludeon.rimworld.odyssey",
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)
                    || !float.IsFinite(cost) || cost <= 0 || technology < 0 || packageId is null)
                    return null;
                entries.Add(new Def(name, cost, technology, packageId));
            }
            return entries.Count == 122 ? entries : null;
        }
        catch (Exception)
        {
            // A missing/malformed embedded reference is not evidence of zero finished projects.
            return null;
        }
    }

    /// <summary>Null means insufficient evidence; returned coverage is always Partial.</summary>
    public static Dictionary<string, MetricEntry>? Build(
        IReadOnlyList<string>? keys, IReadOnlyList<string>? values,
        IReadOnlyCollection<string>? activeModIds, bool malformed = false)
    {
        var defs = Definitions.Value;
        if (defs is null || activeModIds is null || activeModIds.Count == 0
            || keys is null || values is null || keys.Count == 0
            || keys.Count > MaxProjects || values.Count > MaxProjects)
            return null;
        var active = activeModIds.Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!active.Contains("ludeon.rimworld"))
            return null;

        var progress = new Dictionary<string, float>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        var partial = malformed || keys.Count != values.Count;
        var count = Math.Min(keys.Count, values.Count);
        for (var i = 0; i < count; i++)
        {
            var key = keys[i]?.Trim();
            if (string.IsNullOrEmpty(key) || key.Length > 256
                || !float.TryParse(values[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || !float.IsFinite(value) || value < 0)
            {
                partial = true;
                if (!string.IsNullOrEmpty(key))
                {
                    progress.Remove(key);
                    duplicates.Add(key);
                }
                continue;
            }
            if (!progress.TryAdd(key, value))
            {
                progress.Remove(key);
                duplicates.Add(key);
                partial = true;
            }
        }

        var official = defs.Where(d => active.Contains(d.PackageId)).ToArray();
        if (official.Length == 0)
            return null;
        var known = official.Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
        if (progress.Keys.Any(k => !known.Contains(k)))
            partial = true;
        var records = new List<ResearchStateFact>(official.Length);
        foreach (var def in official)
        {
            if (duplicates.Contains(def.Name))
            {
                partial = true;
                continue;
            }
            // Missing keys in ResearchManager.progress have the game's default progress 0.
            // The reference catalog is *not* a MOD-resolved runtime DefDatabase.
            var amount = progress.GetValueOrDefault(def.Name);
            records.Add(new ResearchStateFact(def.Name, amount >= def.Cost, def.Tech));
        }

        // The 1.7.4985 official Def archive is being used provisionally for
        // 1.7.5000, and other MODs can patch baseCost. Never claim Complete.
        return ResearchStateSnapshot.Build(records, partial: true || partial);
    }
}
