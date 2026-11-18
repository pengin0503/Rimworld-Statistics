namespace RimworldStatistics.Core;

/// <summary>One detached bossgroup entry from <c>GameComponent_Bossgroup</c> at save time.</summary>
public readonly record struct BiotechBossFact(string QualifiedDef, int TimesSummoned, bool Defeated);

/// <summary>Phase D2 Biotech: home map pollution, per-mechanitor bandwidth used, and bossgroup progress, detached at save.</summary>
public static class BiotechStateSnapshot
{
    public const string PollutionCellsPeak = "biotech.pollution.cells_peak";
    public const string BandwidthUsedPeak = "biotech.bandwidth.used_peak";
    public const string BossgroupsSummoned = "biotech.bossgroups.summoned";
    public const string BossesDefeated = "biotech.bossgroups.defeated";

    public static Dictionary<string, MetricEntry> Build(
        IEnumerable<int> homeMapPollutedCells,
        IEnumerable<int> activeFreeMechanitorBandwidth,
        bool partial = false,
        IEnumerable<BiotechBossFact>? bossgroups = null)
    {
        long cells = 0;
        int? bandwidth = null;
        foreach (var n in homeMapPollutedCells)
        {
            if (n < 0)
            {
                partial = true;
                continue;
            }
            cells += n;
        }
        foreach (var n in activeFreeMechanitorBandwidth)
        {
            if (n < 0)
            {
                partial = true;
                continue;
            }
            bandwidth = bandwidth.HasValue ? Math.Max(bandwidth.Value, n) : n;
        }
        var c = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [PollutionCellsPeak] = MetricEntry.Of(MetricKeys.Revision(PollutionCellsPeak), new MetricValue
            {
                Coverage = c, IntegerValue = cells,
            }),
            [BandwidthUsedPeak] = MetricEntry.Of(MetricKeys.Revision(BandwidthUsedPeak), new MetricValue
            {
                Coverage = bandwidth.HasValue ? c : MetricCoverage.NotApplicable, IntegerValue = bandwidth,
            }),
        };

        if (bossgroups is not null)
        {
            foreach (var (k, v) in BuildBossgroups(bossgroups, partial))
                result[k] = v;
        }
        return result;
    }

    public static Dictionary<string, MetricEntry> BuildBossgroups(IEnumerable<BiotechBossFact> bossgroups, bool partial = false)
    {
        var summonedByDef = new Dictionary<string, long>(StringComparer.Ordinal);
        var defeatedByDef = new Dictionary<string, long>(StringComparer.Ordinal);
        long totalSummoned = 0;
        long totalDefeated = 0;
        var unknown = false;
        var overflow = false;

        foreach (var b in bossgroups)
        {
            if (b.TimesSummoned < 0)
            {
                partial = true;
                continue;
            }
            var bucket = string.IsNullOrWhiteSpace(b.QualifiedDef) ? MetricKeys.UnknownBucket : b.QualifiedDef;
            if (bucket == MetricKeys.UnknownBucket && (b.TimesSummoned > 0 || b.Defeated))
                unknown = true;

            if (b.TimesSummoned > 0)
            {
                totalSummoned += b.TimesSummoned;
                if (summonedByDef.TryGetValue(bucket, out var cur))
                    summonedByDef[bucket] = cur + b.TimesSummoned;
                else if (summonedByDef.Count >= FamilyOps.KeyCap)
                    overflow = true;
                else
                    summonedByDef[bucket] = b.TimesSummoned;
            }

            if (b.Defeated)
            {
                totalDefeated++;
                if (defeatedByDef.TryGetValue(bucket, out var curDef))
                    defeatedByDef[bucket] = curDef + 1;
                else if (defeatedByDef.Count >= FamilyOps.KeyCap)
                    overflow = true;
                else
                    defeatedByDef[bucket] = 1;
            }
        }

        var cov = (partial || unknown || overflow) ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var summonedEntry = MetricEntry.Of(MetricKeys.Revision(BossgroupsSummoned), new MetricValue
        {
            Coverage = cov,
            IntegerValue = totalSummoned,
        });
        foreach (var (k, v) in summonedByDef)
            summonedEntry.Values[k] = new MetricValue { Coverage = cov, IntegerValue = v };

        var defeatedEntry = MetricEntry.Of(MetricKeys.Revision(BossesDefeated), new MetricValue
        {
            Coverage = cov,
            IntegerValue = totalDefeated,
        });
        foreach (var (k, v) in defeatedByDef)
            defeatedEntry.Values[k] = new MetricValue { Coverage = cov, IntegerValue = v };

        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [BossgroupsSummoned] = summonedEntry,
            [BossesDefeated] = defeatedEntry,
        };
    }
}
