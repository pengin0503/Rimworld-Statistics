namespace RimworldStatistics.Core;

/// <summary>Royalty (D2). Confirmed increases only; title generation and spending other than imperial purchases are excluded by adapters.</summary>
public static class RoyaltyFamily
{
    public const string Name = "royalty";
    public const string TitleGrants = "royalty.title_grants";
    public const string HighestTitle = "royalty.highest_title";
    public const string Honor = "royalty.honor";
    public const string PermitUses = "royalty.permit_uses";
    public const string PermitUsesByDef = "royalty.permit_uses_by_def";
    public const string Psycasts = "royalty.psycasts";
    public const string PsycastsByDef = "royalty.psycasts_by_def";
    private const string Grants = "grants";
    private const string UsedPermits = "permits_used";
    private const string UsedPermitsByDef = "permits_used_by_def";
    private const string CastPsycasts = "psycasts";
    private const string CastPsycastsByDef = "psycasts_by_def";
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

    /// <summary>Count only a permit belonging to a free player colonist once it was actually used.</summary>
    public static void PermitUsed(FamilyState s, bool playerOwned, bool confirmed, string? permitDefKey = null)
    {
        if (!playerOwned || !confirmed)
            return;
        s.Count(UsedPermits);
        if (permitDefKey is not null)
            OptionalTransitionRecording.RecordCount(s, UsedPermitsByDef, key: permitDefKey, withBreakdown: true);
    }

    /// <summary>Count a successful activation by a free player colonist, never UI targeting or failed casts.</summary>
    public static void PsycastSucceeded(FamilyState s, bool playerFreeColonist, bool confirmed, string? psycastDefKey = null)
    {
        if (!playerFreeColonist || !confirmed)
            return;
        s.Count(CastPsycasts);
        if (psycastDefKey is not null)
            OptionalTransitionRecording.RecordCount(s, CastPsycastsByDef, key: psycastDefKey, withBreakdown: true);
    }

    private static void Build(FamilyOutputContext c)
    {
        var s = c.State;
        c.PutCount(Grants, TitleGrants);
        c.PutCount(UsedPermits, PermitUses);
        OptionalTransitionRecording.PutCount(c, UsedPermitsByDef, PermitUsesByDef, withBreakdown: true);
        if ((s.Peek(UsedPermitsByDef)?.N ?? 0) != (s.Peek(UsedPermits)?.N ?? 0))
            c.MarkPartial(PermitUsesByDef);
        c.PutCount(CastPsycasts, Psycasts);
        OptionalTransitionRecording.PutCount(c, CastPsycastsByDef, PsycastsByDef, withBreakdown: true);
        if ((s.Peek(CastPsycastsByDef)?.N ?? 0) != (s.Peek(CastPsycasts)?.N ?? 0))
            c.MarkPartial(PsycastsByDef);
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
        // The scalar is the honor moved (gained + spent), the total the parent row has always shown; the signed amounts stay in
        // the breakdown and are never netted. It carries the family's range and coverage (#330); it has no per-day rate.
        c.PutBreakdown(Honor, values, c.Value(integer: got + spent));
    }
}
