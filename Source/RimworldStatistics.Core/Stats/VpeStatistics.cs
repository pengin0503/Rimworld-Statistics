namespace RimworldStatistics.Core;

public static class VpeStatistics
{
    public const string PackageId = "vanillaexpanded.vpsycastse";
    public const string ImplantType = "VanillaPsycastsExpanded.Hediff_PsycastAbilities";
    public const string EventUnit = "vpe_events";
    public const string CastUnit = "vpe_casts";
    public const string SnapshotUnit = "vpe_state";
    public const string LevelsGained = "royalty.vpe.levels_gained";
    public const string Casts = "royalty.vpe.casts";
    public const string Psycasters = "royalty.vpe.psycasters";
    public const string MaxLevel = "royalty.vpe.level.max";
    public const string SumLevels = "royalty.vpe.level.sum";
    private const string Levels = "levels";
    private const string CastEvents = "casts";

    public static LiveFamily Family { get; } = new()
    {
        Name = EventUnit, Revision = 1, RequiredDlc = AcquisitionUnits.Royalty,
        Build = output => output.PutCount(Levels, LevelsGained),
    };

    /// <summary>Separate gate: inability to audit the cast hook must not disable level tracking.</summary>
    public static LiveFamily CastFamily { get; } = new()
    {
        Name = CastUnit, Revision = 1, RequiredDlc = AcquisitionUnits.Royalty,
        Build = output =>
        {
            var casts = output.State.Peek(CastEvents);
            output.PutBreakdown(Casts, output.Counts(casts),
                output.Value(integer: casts?.N ?? 0, partial: casts?.Overflow == true));
        },
    };

    /// <summary>
    /// Record only after VPE's Cast extension returns from UseAbility, never on
    /// validation or a queued ability job. Player-owned free colonists only.
    /// </summary>
    public static void RecordCast(FamilyState state, string? abilityDefName, bool playerFreeColonist, bool confirmed)
    {
        if (!confirmed || !playerFreeColonist || string.IsNullOrWhiteSpace(abilityDefName))
            return;
        state.Count(CastEvents);
        state.CountBy(CastEvents, abilityDefName);
    }

    public static void RecordLevels(FamilyState state, int before, int after, bool playerOwned)
    {
        if (playerOwned && after > before)
            state.Count(Levels, (long)after - before);
    }

    public static Dictionary<string, MetricEntry> BuildSnapshot(IReadOnlyCollection<int> levels)
    {
        var eligible = levels.Where(level => level > 0).ToArray();
        return new Dictionary<string, MetricEntry>(StringComparer.Ordinal)
        {
            [Psycasters] = Value(Psycasters, eligible.Length),
            [MaxLevel] = Value(MaxLevel, eligible.Length == 0 ? null : eligible.Max()),
            [SumLevels] = Value(SumLevels, eligible.Sum(level => (long)level)),
        };
    }

    private static MetricEntry Value(string key, long? value) => MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
    {
        IntegerValue = value,
        Coverage = value.HasValue ? MetricCoverage.SnapshotOnly : MetricCoverage.NotApplicable,
    });
}
