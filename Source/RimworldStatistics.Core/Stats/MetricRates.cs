namespace RimworldStatistics.Core;

/// <summary>A count per game day over the interval the value itself was recorded in.</summary>
public readonly record struct DailyRate(double PerDay, double Days);

/// <summary>
/// Audit F04: a cumulative total compared per day. Only an additive live total that was recorded without gaps over a
/// known interval of at least one day is divided, by that interval's own days: not the colony's age (recording may have
/// started later), and never for extremes, rates, means, saved states, signed changes or partial records. Colonies are
/// compared one by one; an all-colony rate is not averaged from these.
/// </summary>
public static class MetricRates
{
    /// <summary>
    /// Summed live outputs that are not amounts of events: sizes of sets that stop growing (kinds, tiles or pawns seen at
    /// least once, first-time milestones) and a total that adds gained and spent honor together.
    /// </summary>
    private static readonly HashSet<string> NotAdditive = new(StringComparer.Ordinal)
    {
        WorkFamily.UniqueCraftedDefs, WorkFamily.SownDefs, TradeFamily.SoldDefs, TradeFamily.BoughtDefs,
        TradeFamily.PartnerFactionDefs, CaravanFamily.UniqueTiles, OdysseyFamily.UniqueTiles, BiotechFamily.BirthMothers,
        LifetimeFamily.FirstSkill20, RoyaltyFamily.Honor,
    };

    public static DailyRate? PerDay(MetricDefinition definition, NumericValue value)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(value);
        if (definition.Source != MetricObservationSource.Checkpoint || definition.Overall != OverallAggregation.Sum
            || NotAdditive.Contains(definition.Key) || NotAdditive.Contains(definition.StoredKey)
            || definition.Selection != ColonySelection.Representative || definition.Signed
            || definition.Unit is not (MetricUnit.Count or MetricUnit.Silver or MetricUnit.Nutrition)
            || value.Coverage != MetricCoverage.Complete || value.Number is not { } total || !double.IsFinite(total)
            || total < 0 || value.SinceTick is not { } since || value.ThroughTick is not { } through
            || since < 0 || through - (long)since < StatFormat.TicksPerDay)
            return null;
        var days = (through - (double)since) / StatFormat.TicksPerDay;
        return new DailyRate(total / days, days);
    }
}
