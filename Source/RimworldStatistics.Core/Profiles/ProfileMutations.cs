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
                p.SaveObservations.Remove(old);        // newer reader re-read the same content
                continue;
            }
            old.Superseded = true;                     // the name now holds other content; keep what it held
            if (SameColony(old, obs) && Dominates(obs, old))
                p.SaveObservations.Remove(old);        // removing it changes neither totals nor grouping
        }
        p.SaveObservations.Add(obs);
    }

    /// <summary>
    /// True when dropping <paramref name="older"/> cannot change any statistic, grouping or listed version: same game version,
    /// every value at least as high, traces a superset, and the newer starting pawns include all the older ones (branch
    /// evidence, spec §7.1).
    /// </summary>
    public static bool Dominates(SaveObservation newer, SaveObservation older)
    {
        if (older.Status != ObservationStatus.Ok || newer.Status != ObservationStatus.Ok)
            return false;
        if (older.GameVersion != null && older.GameVersion != newer.GameVersion)
            return false;   // the only record of a version the colony was played on (Colonies tab, spec §9.2)
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
            && older.ExplainedTraces.All(t => newer.EndingTraces.Contains(t) || newer.ExplainedTraces.Contains(t))
            && older.Identity.StartingPawnIds.All(id => newer.Identity.StartingPawnIds.Contains(id, Ord));
    }

    /// <summary>Spec §7.1: the same colony id, or the same fingerprint with starting pawns in a subset relation.</summary>
    private static bool SameColony(SaveObservation a, SaveObservation b)
        => (a.Identity.ModCampaignId is { } id && id == b.Identity.ModCampaignId)
            || CampaignMatcher.SameColony(a.Identity, b.Identity);

    /// <summary>Creates the live record for a campaign; if it exists, only fills in its identity (idempotent).</summary>
    public static void RegisterLiveCampaign(Profile p, string campaignId, ColonyIdentity identity, DisplayInfo display, DateTime utcNow)
    {
        var live = p.LiveCampaigns.FirstOrDefault(c => c.CampaignId == campaignId);
        if (live is null)
        {
            p.LiveCampaigns.Add(new LiveCampaign
            {
                CampaignId = campaignId,
                Identity = identity.Clone(),
                Display = display.Clone(),
                LastSavedUtc = utcNow,
            });
            return;
        }
        MergeIdentity(live.Identity, identity);   // display is owned by the save-time observation
    }

    /// <summary>Records the values read while the game saved (spec §8.3): per-field maxima, the latest values and the identity.</summary>
    public static void ApplyLiveObservation(Profile p, string campaignId, ColonyIdentity identity, ObservedValues values, DisplayInfo display, DateTime savedUtc)
    {
        var live = p.LiveCampaigns.FirstOrDefault(c => c.CampaignId == campaignId);
        if (live is null)
        {
            RegisterLiveCampaign(p, campaignId, identity, display, savedUtc);
            live = p.LiveCampaigns.Single(c => c.CampaignId == campaignId);
        }
        MergeIdentity(live.Identity, identity);

        var m = live.Max;
        var furthest = values.TicksGame >= m.TicksGame;   // ties go to the newer observation (spec §4.2)
        if (furthest)
        {
            m.WealthLast = values.WealthLast;
            live.Display = display.Clone();
            live.MaxObservedUtc = savedUtc;
        }
        m.TicksGame = Math.Max(m.TicksGame, values.TicksGame);
        m.RealPlayTime = Math.Max(m.RealPlayTime, values.RealPlayTime);
        m.NumRaidsEnemy = Math.Max(m.NumRaidsEnemy, values.NumRaidsEnemy);
        m.NumThreatsQueued = Math.Max(m.NumThreatsQueued, values.NumThreatsQueued);
        m.ColonistsKilled = Math.Max(m.ColonistsKilled, values.ColonistsKilled);
        m.ColonistsLaunched = Math.Max(m.ColonistsLaunched, values.ColonistsLaunched);
        m.GreatestPopulation = Math.Max(m.GreatestPopulation, values.GreatestPopulation);
        m.WealthMax = Math.Max(m.WealthMax, values.WealthMax);
        m.WealthPoints = Math.Max(m.WealthPoints, values.WealthPoints);
        live.Latest = values.Clone();
        live.LastSavedUtc = savedUtc;
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
