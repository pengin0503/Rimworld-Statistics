namespace RimworldStatistics.Core;

/// <summary>Every change made to a <see cref="Profile"/>; the rules of spec §6.2 and §7.1. Not thread-safe: run under ProfileSession's lock.</summary>
public static class ProfileMutations
{
    private static readonly StringComparer Ord = StringComparer.Ordinal;

    /// <summary>
    /// False when the current (non-superseded) observation of this file has the same size and time, was not a failure,
    /// and was read by this reader version or newer (spec §7.1: unchanged files are not read; failed ones are retried).
    /// </summary>
    public static bool NeedsRead(Profile p, string fileName, long size, DateTime lastWriteUtc, int readerVersion)
        => !p.SaveObservations.Any(o =>
            !o.Superseded
            && o.FileName == fileName
            && o.Status != ObservationStatus.Error
            && o.SameFileContent(size, lastWriteUtc)
            && o.ReaderVersion >= readerVersion);

    /// <summary>Appends an observation; earlier ones are kept as superseded history unless provably redundant (spec §7.1).</summary>
    public static void AddSaveObservation(Profile p, SaveObservation obs)
    {
        var current = p.SaveObservations.Where(o => !o.Superseded && o.FileName == obs.FileName).ToList();
        if (current.Count == 0)
        {
            p.SaveObservations.Add(obs);
            return;
        }
        foreach (var old in current)
        {
            if (old.Status == ObservationStatus.Error)
            {
                p.SaveObservations.Remove(old);        // a failure carries no data: any new read replaces it
                continue;
            }
            if (old.SameFileContent(obs.FileSize, obs.LastWriteUtc))
            {
                if (obs.ReaderVersion <= old.ReaderVersion)
                    return;                            // same file read again by the same reader: nothing new
                if (obs.Status == ObservationStatus.Error)
                    return;                            // a failed re-read never hides good data; the old version keeps it retried
                if (old.Status == ObservationStatus.Ok && obs.Status == ObservationStatus.Unidentifiable)
                    old.Superseded = true;             // retain data and show the latest identification failure
                else
                    p.SaveObservations.Remove(old);
                continue;
            }
            old.Superseded = true;                     // the name now holds other content; keep what it held
            if (Dominates(obs, old) && CanPrune(p, old, obs))
                p.SaveObservations.Remove(old);        // removing it changes neither totals nor grouping
        }
        p.SaveObservations.Add(obs);
    }

    /// <summary>
    /// True when dropping <paramref name="older"/> cannot change any statistic, grouping or listed value: same game version,
    /// a modification time at least as new, every value at least as high, traces a superset, and the newer starting pawns
    /// include all the older ones (branch evidence, spec §7.1).
    /// </summary>
    public static bool Dominates(SaveObservation newer, SaveObservation older)
    {
        if (older.Status != ObservationStatus.Ok || newer.Status != ObservationStatus.Ok)
            return false;
        if (older.GameVersion != null && older.GameVersion != newer.GameVersion)
            return false;   // the only record of a version the colony was played on (Colonies tab, spec §9.2)
        if (newer.LastWriteUtc < older.LastWriteUtc)
            return false;   // "last played" and the equal-tick tie-break (spec §4.2) depend on the older time
        ObservedValues n = newer.Values, o = older.Values;
        return n.TicksGame >= o.TicksGame
            && n.RealPlayTime >= o.RealPlayTime
            && n.NumRaidsEnemy >= o.NumRaidsEnemy
            && n.NumThreatsQueued >= o.NumThreatsQueued
            && n.ColonistsKilled >= o.ColonistsKilled
            && n.ColonistsLaunched >= o.ColonistsLaunched
            && n.GreatestPopulation >= o.GreatestPopulation
            && n.WealthMax >= o.WealthMax
            && older.EndingTraces.All(newer.EndingTraces.Contains)
            && older.Lineage.All(newer.Lineage.Contains)
            && older.ExplainedTraces.All(t => newer.EndingTraces.Contains(t) || newer.ExplainedTraces.Contains(t))
            && older.Identity.StartingPawnIds.All(id => newer.Identity.StartingPawnIds.Contains(id, Ord));
    }

    private static bool CanPrune(Profile p, SaveObservation older, SaveObservation newer)
    {
        var id = older.Identity;
        var hash = id.FingerprintHash();
        if (id.ModCampaignId != newer.Identity.ModCampaignId || hash != newer.Identity.FingerprintHash()
            || (id.ModCampaignId == null && hash == null))
            return false;

        var pawns = id.StartingPawnIds.ToHashSet(Ord);
        if (!pawns.SetEquals(newer.Identity.StartingPawnIds))
            return false;

        // An older timestamp can decide which branch receives an ambiguous subset.
        foreach (var other in p.SaveObservations.Select(o => o.Identity).Concat(p.LiveCampaigns.Select(l => l.Identity)))
        {
            var sameCampaign = id.ModCampaignId != null && id.ModCampaignId == other.ModCampaignId;
            var otherHash = other.FingerprintHash();
            if ((sameCampaign && otherHash != hash) ||
                (hash != null && otherHash == hash && !pawns.SetEquals(other.StartingPawnIds)))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Adds a numeric observation, or merges a re-read of the same content (same source and key): file aliases are
    /// unioned, the time is the earliest copy's, and per metric a newer definition revision replaces the old one while,
    /// for the same revision, a worse value never hides a good one (numeric expansion design §2 "同じ入力の再解析").
    /// </summary>
    public static void UpsertMetricObservation(Profile p, MetricObservation obs)
    {
        var existing = p.MetricObservations.FirstOrDefault(o => !o.IsQuarantined && o.Source == obs.Source && o.Key == obs.Key);
        if (existing is null)
        {
            if (p.MetricObservations.Any(o => Supersedes(o, obs)))
                return;
            p.MetricObservations.RemoveAll(o => Supersedes(obs, o));
            p.MetricObservations.Add(obs);
            return;
        }
        foreach (var alias in obs.FileAliases)
            if (!existing.FileAliases.Any(a => a.Same(alias.FileName, alias.FileSize, alias.LastWriteUtc)))
                existing.FileAliases.Add(alias);
        if (obs.ObservedUtc < existing.ObservedUtc)
            existing.ObservedUtc = obs.ObservedUtc;
        existing.FormatVersion = Math.Max(existing.FormatVersion, obs.FormatVersion);
        foreach (var (key, entry) in obs.Metrics)
        {
            if (entry.IsQuarantined)
                continue;
            if (!existing.Metrics.TryGetValue(key, out var old) || old.IsQuarantined || entry.Revision > old.Revision
                || (entry.Revision == old.Revision && Quality(entry) > Quality(old)))
                existing.Metrics[key] = entry;
        }
    }

    /// <summary>
    /// True when <paramref name="older"/> can no longer change any shown value, so keeping it only grows the record file
    /// (each changed autosave is a new observation). Both are save-file observations with the same identity (fingerprint,
    /// campaign id and starting pawn set, so they are always attached to the same colony); <paramref name="newer"/> has a
    /// strictly later tick, so it is always chosen as the representative; and the only value taken across observations,
    /// the mod count, is the same in both.
    /// </summary>
    private static bool Supersedes(MetricObservation newer, MetricObservation older)
    {
        if (newer.IsQuarantined || older.IsQuarantined || ReferenceEquals(newer, older)
            || newer.Source != MetricObservationSource.SaveFile || older.Source != MetricObservationSource.SaveFile
            || newer.SaveTick <= older.SaveTick)
            return false;
        ColonyIdentity a = newer.Identity, b = older.Identity;
        if (a.FingerprintHash() is not { } hash || hash != b.FingerprintHash() || a.ModCampaignId != b.ModCampaignId
            || !a.StartingPawnIds.ToHashSet(Ord).SetEquals(b.StartingPawnIds))
            return false;
        return ModCount(newer) == ModCount(older);
    }

    /// <summary>The stored mod count and its revision; a missing or unreadable entry is its own state.</summary>
    private static (bool Readable, long? Count, int Revision) ModCount(MetricObservation o)
        => o.Metrics.TryGetValue(MetricKeys.ModsLatest, out var e)
            ? (!e.IsQuarantined, e.IsQuarantined ? null : e.Values.GetValueOrDefault(MetricEntry.Scalar)?.IntegerValue, e.Revision)
            : (true, null, 0);

    /// <summary>Good (complete, snapshot or not applicable) above partial above unknown; the worst value of the entry counts.</summary>
    private static int Quality(MetricEntry e)
        => e.Values.Count == 0 ? 2 : e.Values.Values.Min(v => v.Coverage switch
        {
            MetricCoverage.Unknown => 0,
            MetricCoverage.Partial => 1,
            _ => 2,
        });

    /// <summary>Creates the live record for a campaign; if it exists, only fills in its identity (idempotent).</summary>
    public static void RegisterLiveCampaign(Profile p, string campaignId, ColonyIdentity identity, DisplayInfo display)
    {
        var live = p.LiveCampaigns.FirstOrDefault(c => c.CampaignId == campaignId);
        if (live is null)
        {
            p.LiveCampaigns.Add(new LiveCampaign
            {
                CampaignId = campaignId,
                Identity = identity.Clone(),
                Display = display.Clone(),
            });
            return;
        }
        MergeIdentity(live.Identity, identity);   // display is owned by the save-time observation
    }

    /// <summary>Records the values read while the game saved (spec §8.3): per-field maxima, the latest values and the identity.</summary>
    public static void ApplyLiveObservation(Profile p, string campaignId, ColonyIdentity identity, ObservedValues values, DisplayInfo display,
        DateTime savedUtc, string? gameVersion = null, bool isSave = true)
    {
        var live = p.LiveCampaigns.FirstOrDefault(c => c.CampaignId == campaignId);
        if (live is null)
        {
            RegisterLiveCampaign(p, campaignId, identity, display);
            live = p.LiveCampaigns.Single(c => c.CampaignId == campaignId);
        }
        MergeIdentity(live.Identity, identity);

        var m = live.Max;
        var furthest = values.TicksGame >= m.TicksGame;   // ties go to the newer observation (spec §4.2)
        if (furthest)
        {
            m.WealthLast = StatsCalculator.ValidMetricOrZero(values.WealthLast);
            live.WealthAtFurthestKnown = StatsCalculator.HasWealth(values.WealthLast, values.WealthPoints);
            live.Display = display.Clone();
            live.MaxObservedUtc = savedUtc;
        }
        m.TicksGame = Math.Max(m.TicksGame, values.TicksGame);
        m.RealPlayTime = Math.Max(StatsCalculator.ValidMetricOrZero(m.RealPlayTime), StatsCalculator.ValidMetricOrZero(values.RealPlayTime));
        m.NumRaidsEnemy = Math.Max(m.NumRaidsEnemy, values.NumRaidsEnemy);
        m.NumThreatsQueued = Math.Max(m.NumThreatsQueued, values.NumThreatsQueued);
        m.ColonistsKilled = Math.Max(m.ColonistsKilled, values.ColonistsKilled);
        m.ColonistsLaunched = Math.Max(m.ColonistsLaunched, values.ColonistsLaunched);
        m.GreatestPopulation = Math.Max(m.GreatestPopulation, values.GreatestPopulation);
        m.WealthPoints = Math.Max(
            StatsCalculator.ValidMetric(m.WealthMax) ? m.WealthPoints : 0,
            StatsCalculator.ValidMetric(values.WealthMax) ? values.WealthPoints : 0);
        m.WealthMax = Math.Max(StatsCalculator.ValidMetricOrZero(m.WealthMax), StatsCalculator.ValidMetricOrZero(values.WealthMax));
        live.Latest = values.Clone();
        if (isSave)
            live.LastSavedUtc = savedUtc;
        if (!string.IsNullOrEmpty(gameVersion) && !live.GameVersions.Contains(gameVersion, Ord))
            live.GameVersions.Add(gameVersion);
    }

    /// <summary>Fills fingerprint parts that were not known yet (e.g. the first wealth record of a new game) and unions starting pawns.</summary>
    private static void MergeIdentity(ColonyIdentity target, ColonyIdentity source)
    {
        if (target.SeedString is null && source.SeedString is not null)
        {
            // Nothing was known about the world yet; the ints have no "missing" value of their own.
            target.PersistentRandomValue = source.PersistentRandomValue;
            target.GameStartAbsTick = source.GameStartAbsTick;
        }
        target.SeedString ??= source.SeedString;
        target.StartingTile ??= source.StartingTile;
        target.FirstWealthBits ??= source.FirstWealthBits;
        foreach (var pawn in source.StartingPawnIds)
            if (!target.StartingPawnIds.Contains(pawn, Ord))
                target.StartingPawnIds.Add(pawn);
    }

    /// <summary>
    /// Adds an ending unless (CampaignId, Type, Ordinal, DevMode) is already recorded; then keeps the larger
    /// departed / launchedTotalAfter (spec §6.2). Returns true when added.
    /// </summary>
    public static bool UpsertEnding(Profile p, EndingEvent ev)
    {
        var existing = p.EndingEvents.FirstOrDefault(e =>
            e.CampaignId == ev.CampaignId && e.Type == ev.Type && e.Ordinal == ev.Ordinal && e.DevMode == ev.DevMode);
        if (existing is null)
        {
            p.EndingEvents.Add(ev);
            return true;
        }
        if (existing.Source == EndingSource.SeededTrace && ev.Source == EndingSource.Live)
        {
            existing.Source = ev.Source;
            existing.RealTimeUtc = ev.RealTimeUtc;
            existing.TicksGame = ev.TicksGame;
            existing.GameVersion = ev.GameVersion;
            existing.CreditsExcerpt = ev.CreditsExcerpt;
        }
        existing.ColonistsKilledAtEnding ??= ev.ColonistsKilledAtEnding;   // the first recorded value stays
        existing.Departed = Math.Max(existing.Departed, ev.Departed);
        existing.LaunchedTotalAfter = Math.Max(existing.LaunchedTotalAfter, ev.LaunchedTotalAfter);
        return false;
    }

    /// <summary><paramref name="colonyKeys"/> is a colony's Key followed by its aliases.</summary>
    public static void SetExcluded(Profile p, IReadOnlyCollection<string> colonyKeys, bool excluded)
    {
        var list = p.UserDecisions.ExcludedCampaignKeys;
        if (excluded)
        {
            var primary = colonyKeys.First();
            if (!list.Contains(primary, Ord))
                list.Add(primary);
        }
        else
        {
            list.RemoveAll(k => colonyKeys.Contains(k, Ord));
        }
    }
}
