namespace RimworldStatistics.Core;

/// <summary>
/// Phase D2 Ideology expansion events: confirmed prisoner enslavements (paired opposite to
/// <see cref="MembershipFamily.SlavesFreed"/>) and completed player biosculpter pod cycles by cycle key.
/// </summary>
public static class IdeologyExpansionFamily
{
    public const string Name = "ideology_expansion";

    public const string PrisonersEnslaved = "ideology.prisoners_enslaved";
    public const string BiosculpterCompletions = "ideology.biosculpter.completions";

    private const string Enslaved = "prisoners_enslaved";
    private const string Biosculpter = "biosculpter_cycles";

    public static LiveFamily Family { get; } = new()
    {
        Name = Name,
        Revision = 1,
        RequiredDlc = AcquisitionUnits.Ideology,
        Build = Build,
    };

    public static void PrisonerEnslaved(FamilyState s, bool wasColonyPrisoner, bool nowColonySlave)
    {
        if (!wasColonyPrisoner || !nowColonySlave)
            return;
        s.Count(Enslaved);
    }

    public static void BiosculpterCycleCompleted(FamilyState s, bool playerPod, bool colonyOccupant, string? cycleKey)
    {
        if (!playerPod || !colonyOccupant)
            return;
        var bucket = string.IsNullOrWhiteSpace(cycleKey) ? MetricKeys.UnknownBucket : cycleKey.Trim();
        s.Count(Biosculpter);
        s.CountBy(Biosculpter, bucket);
    }

    private static void Build(FamilyOutputContext c)
    {
        c.PutCount(Enslaved, PrisonersEnslaved);
        var bio = c.State.Peek(Biosculpter);
        var partial = bio?.Overflow == true || bio?.By?.ContainsKey(MetricKeys.UnknownBucket) == true;
        c.PutBreakdown(BiosculpterCompletions, c.Counts(bio), c.Value(integer: bio?.N ?? 0, partial: partial));
        if (partial)
            c.MarkPartial(BiosculpterCompletions);
    }
}
