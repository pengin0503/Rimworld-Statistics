namespace RimworldStatistics.Core;

/// <summary>Royalty (D2). Confirmed increases only; title generation and spending other than imperial purchases are excluded by adapters.</summary>
public static class RoyaltyFamily
{
    public const string Name = "royalty";
    public const string TitleGrants = "royalty.title_grants";
    public const string HighestTitle = "royalty.highest_title";
    public const string Honor = "royalty.honor";
    private const string Grants = "grants";
    private const string Titles = "title";
    private const string HonorGained = "honor_gained";
    private const string HonorSpent = "honor_spent";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name, Revision = 1, RequiredDlc = AcquisitionUnits.Royalty, Build = Build,
    };

    /// <summary>Only an observed upward change in a valid empire title is a grant, not a reset, demotion or duplicate assignment.</summary>
    public static void TitleChanged(FamilyState s, string titleDef, int? beforeSeniority, int? afterSeniority, bool eligible)
    {
        if (!eligible || string.IsNullOrEmpty(titleDef) || afterSeniority is not { } after || after < 0)
            return;
        if (beforeSeniority is { } before && after <= before)
            return;
        // A null previous rank means a grant from no title. A setter that merely wrote the same
        // rank is filtered by the adapter's confirmed before/after comparison.
        s.Count(Grants);
        s.Observe(Titles, after, titleDef);
    }

    public static void HonorChanged(FamilyState s, int before, int after, bool eligible, bool confirmedSpend)
    {
        if (!eligible || before == after)
            return;
        if (after > before && !confirmedSpend)
            s.Count(HonorGained, (long)after - before);
        else if (after < before && confirmedSpend)
            s.Count(HonorSpent, (long)before - after);
        // Other decreases are not assumed to be purchases.
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Grants, TitleGrants);
        var title = c.State.Peek(Titles);
        if (title?.Max is { } rank)
        {
            var v = c.Value(integer: (long)rank, samples: (int)(title.N ?? 0));
            var bucket = title.MaxKey;
            if (!string.IsNullOrWhiteSpace(bucket))
                c.PutBreakdown(HighestTitle, new Dictionary<string, MetricValue>(StringComparer.Ordinal) { [bucket] = v }, v);
            else
                c.Put(HighestTitle, v);
        }
        else c.Put(HighestTitle, c.Value(coverage: MetricCoverage.NotApplicable));
        var got = c.State.Peek(HonorGained)?.N ?? 0;
        var spent = c.State.Peek(HonorSpent)?.N ?? 0;
        var values = new Dictionary<string, MetricValue>(StringComparer.Ordinal)
        {
            ["gained"] = c.Value(integer: got),
            ["spent"] = c.Value(integer: spent),
        };
        c.PutBreakdown(Honor, values);
    }
}
