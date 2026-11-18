namespace RimworldStatistics.Core;

/// <summary>
/// Conservative 1.7 historical salvage for Biotech mechanitor bossgroup summon counts and
/// defeated boss types from <c>GameComponent_Bossgroup</c>.
/// </summary>
internal static class HistoricalBiotechBossReader
{
    public const int MaxBossEntries = 1024;

    public const string Diabolus = "ludeon.rimworld.biotech/Diabolus";
    public const string Warqueen = "ludeon.rimworld.biotech/Warqueen";
    public const string Apocriton = "ludeon.rimworld.biotech/Apocriton";

    private static readonly Dictionary<string, string> KnownBiotechBosses = new(StringComparer.Ordinal)
    {
        ["Diabolus"] = Diabolus,
        ["Warqueen"] = Warqueen,
        ["Apocriton"] = Apocriton,
    };

    public static string QualifySavedBossDef(string? rawDefName, out bool unknown)
    {
        unknown = false;
        if (!string.IsNullOrWhiteSpace(rawDefName) && KnownBiotechBosses.TryGetValue(rawDefName.Trim(), out var q))
            return q;
        unknown = true;
        return MetricKeys.UnknownBucket;
    }

    public static Dictionary<string, MetricEntry>? Build(
        int componentCount,
        bool malformed,
        IReadOnlyList<string>? killedBossDefs,
        IReadOnlyList<string>? calledKeys,
        IReadOnlyList<int>? calledValues,
        IReadOnlyList<string>? activeDlcIds)
    {
        if (activeDlcIds?.Contains(AcquisitionUnits.Biotech, StringComparer.OrdinalIgnoreCase) != true)
            return null;
        if (componentCount != 1 || malformed)
            return null;
        if ((calledKeys?.Count ?? 0) != (calledValues?.Count ?? 0))
            return null;

        var bossFacts = new List<BiotechBossFact>();
        var byKey = new Dictionary<string, (int Summoned, bool Defeated)>(StringComparer.Ordinal);
        var partial = false;

        if (killedBossDefs is not null)
        {
            foreach (var raw in killedBossDefs)
            {
                var key = QualifySavedBossDef(raw, out var unk);
                if (unk)
                    partial = true;
                var prev = byKey.GetValueOrDefault(key);
                byKey[key] = (prev.Summoned, true);
            }
        }

        if (calledKeys is not null && calledValues is not null)
        {
            for (var i = 0; i < calledKeys.Count; i++)
            {
                var count = calledValues[i];
                if (count < 0)
                {
                    partial = true;
                    continue;
                }
                // NumTimesCalledBossgroup lazily inserts 0 when opening gizmos; ignore 0 for modded unknown defs.
                if (count == 0 && !KnownBiotechBosses.ContainsKey(calledKeys[i].Trim()))
                    continue;
                var key = QualifySavedBossDef(calledKeys[i], out var unk);
                if (unk)
                    partial = true;
                var prev = byKey.GetValueOrDefault(key);
                byKey[key] = (prev.Summoned + count, prev.Defeated);
            }
        }

        foreach (var (key, val) in byKey)
            bossFacts.Add(new BiotechBossFact(key, val.Summoned, val.Defeated));

        var built = BiotechStateSnapshot.BuildBossgroups(bossFacts, partial: true);
        // Historical save salvage is always Partial coverage behind Experimental evidence.
        if (partial)
        {
            foreach (var entry in built.Values)
                foreach (var mv in entry.Values.Values)
                    mv.Weakened = true;
        }
        return built;
    }
}
