using System.Reflection;
using RimWorld;
using RimworldStatistics.Core;
using Verse;

namespace RimworldStatistics.Metrics;

/// <summary>Best-effort inventory of active mods; missing version metadata is unknown, never guessed.</summary>
internal static class ModProviderGameDiscovery
{
    public static IReadOnlyList<ModPackageInfo> ActivePackages()
        => LoadedModManager.RunningMods
            .Where(mod => !string.IsNullOrWhiteSpace(mod.PackageId))
            .Select(mod => new ModPackageInfo(mod.PackageId, ReadVersion(mod)))
            .ToArray();

    private static string? ReadVersion(object mod)
    {
        // Versions are optional metadata supplied by third-party mods. RimWorld itself does
        // not guarantee the same metadata API for every package or loader version.
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            static object? Member(object? value, string name)
            {
                if (value is null) return null;
                var t = value.GetType();
                return t.GetProperty(name, flags)?.GetValue(value)
                    ?? t.GetField(name, flags)?.GetValue(value);
            }
            // In RimWorld, Mod owns Content (ModContentPack) and Content owns
            // ModMetaData. Some loaders expose the metadata directly on Mod.
            var content = Member(mod, "Content") ?? Member(mod, "content");
            var metadata = Member(mod, "ModMetaData")
                ?? Member(content, "ModMetaData") ?? Member(content, "modMetaData")
                ?? Member(content, "metadata");
            if (metadata is null)
                return null;
            foreach (var name in new[] { "ModVersion", "modVersion", "Version" })
            {
                var value = Member(metadata, name);
                if (value is null)
                    continue;
                var raw = value.ToString();
                if (Version.TryParse(raw, out _))
                    return raw;
            }
        }
        catch (Exception)
        {
            // Unknown version: any adapter requiring a minimum/maximum version stays closed.
        }
        return null;
    }

    /// <summary>Resolve only declared Def names, at startup; no per-tick or per-save scans.</summary>
    public static IReadOnlySet<string> ExistingDefs(IEnumerable<string> required)
    {
        var needed = required.ToHashSet(StringComparer.Ordinal);
        if (needed.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);
        var found = new HashSet<string>(StringComparer.Ordinal);
        void Add(string? id)
        {
            if (id is not null && needed.Contains(id))
                found.Add(id);
        }
        foreach (var d in DefDatabase<ThingDef>.AllDefsListForReading) Add(d?.defName);
        foreach (var d in DefDatabase<HediffDef>.AllDefsListForReading) Add(d?.defName);
        foreach (var d in DefDatabase<ResearchProjectDef>.AllDefsListForReading) Add(d?.defName);
        foreach (var d in DefDatabase<JobDef>.AllDefsListForReading) Add(d?.defName);
        foreach (var d in DefDatabase<IncidentDef>.AllDefsListForReading) Add(d?.defName);
        return found;
    }
}
