namespace RimworldStatistics.Core;

/// <summary>One saved quest as Quest.ExposeData writes it (1.7.4985); omitted fields take the Scribe defaults.</summary>
public readonly record struct RetainedQuest(
    int Id,
    string? Root,
    int AcceptanceTick = -1,
    int AcceptanceExpireTick = -1,
    bool InitiallyAccepted = false,
    bool Ended = false,
    string Outcome = "Unknown",
    int CleanupTick = -1)
{
    /// <summary>Quest.EverAccepted.</summary>
    public bool Accepted => AcceptanceTick >= 0 || InitiallyAccepted;
}

/// <summary>
/// Phase A quest metrics from the quests still saved in one save (ledger section quests). These are counts of what the
/// save retains, not lifetime totals: the game can remove quests (QuestManager.Remove, subquest generators).
/// </summary>
public static class RetainedQuestMetricReader
{
    /// <param name="quests">The saved quests; null when the save has no quest manager.</param>
    /// <param name="unreadable">Saved quests left out because a field could not be read.</param>
    public static void Summarize(IReadOnlyList<RetainedQuest>? quests, int unreadable, int saveTick, IDictionary<string, MetricEntry> into)
    {
        if (quests is null)
            return;

        // The same id with the same content is one quest (a copied row); the same id with different content cannot be trusted.
        var kept = new List<RetainedQuest>();
        var partial = unreadable > 0;
        foreach (var group in quests.GroupBy(q => q.Id))
        {
            var distinct = group.Distinct().ToList();
            if (group.Key < 0 || distinct.Count > 1)
                partial = true;
            else
                kept.Add(distinct[0]);
        }

        var coverage = partial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        MetricValue Count(long n) => new() { Coverage = coverage, IntegerValue = n };
        void Put(string key, MetricValue? v)
        {
            if (v is not null)
                into[key] = MetricEntry.Of(MetricKeys.Revision(key), v);
        }

        var successes = kept.Where(q => q.Accepted && q.Ended && q.Outcome == "Success").ToList();
        var failures = kept.Count(q => q.Accepted && q.Ended && q.Outcome == "Fail");
        Put(MetricKeys.QuestSuccess, Count(successes.Count));
        Put(MetricKeys.QuestFailure, Count(failures));
        Put(MetricKeys.QuestAccepted, Count(kept.Count(q => q.Accepted)));
        Put(MetricKeys.QuestExpired, Count(kept.Count(q =>
            !q.InitiallyAccepted && q.AcceptanceTick < 0 && q.AcceptanceExpireTick >= 0 && q.AcceptanceExpireTick <= saveTick)));

        // 0 ÷ 0 is kept: the colony is known to have no decided quest (shown as no value, adds nothing to a total).
        var decided = successes.Count + failures;
        Put(MetricKeys.QuestSuccessRate, new MetricValue
        {
            Coverage = coverage, RealValue = decided > 0 ? successes.Count / (double)decided : null, Numerator = successes.Count, Denominator = decided,
        });

        // The scalar carries the total and its coverage, so quests left out keep the split Partial even when it is empty.
        var byRoot = new MetricEntry { Revision = MetricKeys.Revision(MetricKeys.QuestSuccessByRoot), Values = { [MetricEntry.Scalar] = Count(successes.Count) } };
        foreach (var g in successes.GroupBy(q => string.IsNullOrEmpty(q.Root) ? MetricKeys.UnknownBucket : q.Root!, StringComparer.Ordinal))
            byRoot.Values[g.Key] = Count(g.Count());
        into[MetricKeys.QuestSuccessByRoot] = byRoot;

        // Time from acceptance to the start of cleanup, for successes with a real acceptance tick.
        var days = new List<double>();
        var durationPartial = partial;
        foreach (var q in successes)
        {
            if (q.AcceptanceTick < 0 || q.CleanupTick < 0 || q.CleanupTick < q.AcceptanceTick)
            {
                durationPartial = true;
                continue;
            }
            days.Add((q.CleanupTick - q.AcceptanceTick) / (double)StatFormat.TicksPerDay);
        }
        if (successes.Count == 0)
        {
            Put(MetricKeys.QuestSuccessDurationMean, new MetricValue { Coverage = coverage, Numerator = 0, Denominator = 0 });
            Put(MetricKeys.QuestSuccessDurationMin, new MetricValue { Coverage = MetricCoverage.NotApplicable });
            Put(MetricKeys.QuestSuccessDurationMax, new MetricValue { Coverage = MetricCoverage.NotApplicable });
            return;
        }
        if (days.Count == 0)
            return;   // successes, but none with a usable time: unknown
        var durationCoverage = durationPartial ? MetricCoverage.Partial : MetricCoverage.SnapshotOnly;
        Put(MetricKeys.QuestSuccessDurationMean, new MetricValue
        {
            Coverage = durationCoverage, RealValue = days.Sum() / days.Count, Numerator = days.Sum(), Denominator = days.Count,
        });
        Put(MetricKeys.QuestSuccessDurationMin, new MetricValue { Coverage = durationCoverage, RealValue = days.Min() });
        Put(MetricKeys.QuestSuccessDurationMax, new MetricValue { Coverage = durationCoverage, RealValue = days.Max() });
    }
}
