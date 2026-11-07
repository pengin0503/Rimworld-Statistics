namespace RimworldStatistics.Core;

/// <summary>One detached research def as read from the actual registered game definitions.</summary>
public readonly record struct ResearchStateFact(string DefName, bool Finished, int TechLevel);

/// <summary>Save-time research aggregation; no game types are retained in the profile.</summary>
public static class ResearchStateSnapshot
{
    public const string Finished = "research.snapshot.finished";
    public const string FinishedRate = "research.snapshot.finished_rate";
    public const string TechPeak = "research.snapshot.tech_peak";

    public static Dictionary<string, MetricEntry> Build(IEnumerable<ResearchStateFact> projects, bool partial = false)
    {
        var defs = new HashSet<string>(StringComparer.Ordinal);
        long finished = 0;
        long total = 0;
        int? highest = null;
        var keys = new Dictionary<string, MetricValue>(StringComparer.Ordinal);
        foreach (var p in projects)
        {
            if (string.IsNullOrEmpty(p.DefName) || !defs.Add(p.DefName))
            {
                partial = true;
                continue;
            }
            total++;
            if (!p.Finished)
                continue;
            finished++;
            if (p.TechLevel < 0)
            {
                partial = true;
                continue;
            }
            if (!highest.HasValue || p.TechLevel > highest.Value)
            {
                highest = p.TechLevel;
                keys.Clear();
                keys[p.DefName] = new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = p.TechLevel };
            }
            else if (p.TechLevel == highest.Value)
                keys[p.DefName] = new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = p.TechLevel };
        }

        var coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [Finished] = MetricEntry.Of(MetricKeys.Revision(Finished), new MetricValue { Coverage = coverage, IntegerValue = finished }),
            [FinishedRate] = MetricEntry.Of(MetricKeys.Revision(FinishedRate), new MetricValue
            {
                Coverage = total > 0 ? coverage : MetricCoverage.NotApplicable,
                RealValue = total > 0 ? finished / (double)total : null,
                Numerator = finished,
                Denominator = total,
            }),
            [TechPeak] = MetricEntry.Of(MetricKeys.Revision(TechPeak), new MetricValue
            {
                Coverage = highest.HasValue ? coverage : MetricCoverage.NotApplicable, IntegerValue = highest,
            }),
        };
        foreach (var (key, value) in keys)
        {
            value.Coverage = coverage;
            result[TechPeak].Values[key] = value;
        }
        return result;
    }
}
