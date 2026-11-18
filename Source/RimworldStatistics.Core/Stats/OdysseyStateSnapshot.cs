namespace RimworldStatistics.Core;

/// <summary>One detached player-owned weapon carrying <c>CompUniqueWeapon</c> at save time.</summary>
public sealed record UniqueWeaponFact(string Id, IReadOnlyList<string?> QualifiedTraitDefs);

/// <summary>
/// Odyssey DLC save-time state: gravship substructure cell peak, connected thruster peak,
/// owned unique weapon count peak, and owned unique weapon trait breakdown.
/// </summary>
public static class OdysseyStateSnapshot
{
    public const string Unit = "odyssey_state";
    public const string SubstructureCellsPeak = "odyssey.gravship.substructure_cells_peak";
    public const string ThrustersPeak = "odyssey.gravship.thrusters_peak";
    public const string UniqueWeaponsPeak = "odyssey.unique_weapons.owned_peak";
    public const string UniqueWeaponsByTrait = "odyssey.unique_weapons.by_trait";

    public static Dictionary<string, MetricEntry> Build(
        int? substructureCells,
        int? thrusters,
        IEnumerable<UniqueWeaponFact>? uniqueWeapons,
        bool partial = false)
    {
        if (substructureCells is < 0)
        {
            substructureCells = null;
            partial = true;
        }
        if (thrusters is < 0)
        {
            thrusters = null;
            partial = true;
        }

        var result = new Dictionary<string, MetricEntry>(StringComparer.Ordinal);
        var baseCoverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;

        result[SubstructureCellsPeak] = MetricEntry.Of(MetricKeys.Revision(SubstructureCellsPeak), new MetricValue
        {
            Coverage = substructureCells is { } sc ? baseCoverage : (partial ? MetricCoverage.Unknown : MetricCoverage.NotApplicable),
            IntegerValue = substructureCells,
        });

        result[ThrustersPeak] = MetricEntry.Of(MetricKeys.Revision(ThrustersPeak), new MetricValue
        {
            Coverage = thrusters is { } th ? baseCoverage : (partial ? MetricCoverage.Unknown : MetricCoverage.NotApplicable),
            IntegerValue = thrusters,
        });

        if (uniqueWeapons is null)
        {
            result[UniqueWeaponsPeak] = MetricEntry.Of(MetricKeys.Revision(UniqueWeaponsPeak), new MetricValue
            {
                Coverage = MetricCoverage.Unknown,
            });
            result[UniqueWeaponsByTrait] = MetricEntry.Of(MetricKeys.Revision(UniqueWeaponsByTrait), new MetricValue
            {
                Coverage = MetricCoverage.Unknown,
            });
            return result;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var traitCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var traitOverflow = false;
        var unknownTrait = false;
        long ownedWeapons = 0;
        long totalTraits = 0;

        foreach (var w in uniqueWeapons)
        {
            if (string.IsNullOrWhiteSpace(w.Id) || !seenIds.Add(w.Id))
            {
                partial = true;
                continue;
            }
            ownedWeapons++;
            foreach (var rawTrait in w.QualifiedTraitDefs)
            {
                var bucket = string.IsNullOrWhiteSpace(rawTrait) ? MetricKeys.UnknownBucket : rawTrait!;
                if (bucket == MetricKeys.UnknownBucket)
                    unknownTrait = true;
                totalTraits++;
                if (traitCounts.TryGetValue(bucket, out var cur))
                    traitCounts[bucket] = cur + 1;
                else if (traitCounts.Count >= FamilyOps.KeyCap)
                    traitOverflow = true;
                else
                    traitCounts[bucket] = 1;
            }
        }

        var weaponCoverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        var traitCoverage = (partial || traitOverflow || unknownTrait) ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;

        result[UniqueWeaponsPeak] = MetricEntry.Of(MetricKeys.Revision(UniqueWeaponsPeak), new MetricValue
        {
            Coverage = weaponCoverage,
            IntegerValue = ownedWeapons,
        });

        var traitEntry = MetricEntry.Of(MetricKeys.Revision(UniqueWeaponsByTrait), new MetricValue
        {
            Coverage = traitCoverage,
            IntegerValue = totalTraits,
        });
        foreach (var (traitKey, count) in traitCounts)
        {
            traitEntry.Values[traitKey] = new MetricValue
            {
                Coverage = traitCoverage,
                IntegerValue = count,
            };
        }
        result[UniqueWeaponsByTrait] = traitEntry;

        return result;
    }
}
