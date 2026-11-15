namespace RimworldStatistics.Core;
public static partial class ProfileMutations
{
    /// <summary>
    /// False when the current (non-superseded) observation of this file has the same size and time, was not a failure,
    /// and was read by this reader version or newer (spec §7.1: unchanged files are not read; failed ones are retried).
    /// </summary>
    public static bool NeedsRead(Profile p, string fileName, long size, DateTime lastWriteUtc, int readerVersion)
    {
        var observations = p.SaveObservations;
        for (var i = 0; i < observations.Count; i++)
        {
            var o = observations[i];
            if (o.FileName == fileName && o.SameFileContent(size, lastWriteUtc) && IsCurrentRead(o, readerVersion))
                return false;
        }
        return true;
    }

    public static bool IsCurrentRead(SaveObservation o, int readerVersion)
        => !o.Superseded && o.Status != ObservationStatus.Error && o.ReaderVersion >= readerVersion;

    /// <summary>Appends an observation; earlier ones are kept as superseded history unless provably redundant (spec §7.1).</summary>
    public static void AddSaveObservation(Profile p, SaveObservation obs)
    {
        var observations = p.SaveObservations;
        SaveObservation? singleCurrent = null;
        List<SaveObservation>? multipleCurrent = null;
        for (var i = 0; i < observations.Count; i++)
        {
            var o = observations[i];
            if (!o.Superseded && o.FileName == obs.FileName)
            {
                if (singleCurrent is null)
                    singleCurrent = o;
                else
                {
                    multipleCurrent ??= new List<SaveObservation>(2) { singleCurrent };
                    multipleCurrent.Add(o);
                }
            }
        }
        if (singleCurrent is null)
        {
            observations.Add(obs);
            return;
        }
        if (multipleCurrent is null)
        {
            if (ProcessCurrentSaveObservation(p, observations, singleCurrent, obs))
                return;
            observations.Add(obs);
            return;
        }
        for (var i = 0; i < multipleCurrent.Count; i++)
        {
            if (ProcessCurrentSaveObservation(p, observations, multipleCurrent[i], obs))
                return;
        }
        observations.Add(obs);
    }

    private static bool ProcessCurrentSaveObservation(Profile p, List<SaveObservation> observations, SaveObservation old, SaveObservation obs)
    {
        if (old.Status == ObservationStatus.Error)
        {
            observations.Remove(old);        // a failure carries no data: any new read replaces it
            return false;
        }
        if (old.SameFileContent(obs.FileSize, obs.LastWriteUtc))
        {
            if (obs.ReaderVersion <= old.ReaderVersion)
                return true;                 // same file read again by the same reader: nothing new
            if (obs.Status == ObservationStatus.Error)
                return true;                 // a failed re-read never hides good data; the old version keeps it retried
            if (old.Status == ObservationStatus.Ok && obs.Status == ObservationStatus.Unidentifiable)
                old.Superseded = true;       // retain data and show the latest identification failure
            else
                observations.Remove(old);
            return false;
        }
        old.Superseded = true;               // the name now holds other content; keep what it held
        if (Dominates(obs, old) && CanPrune(p, old, obs))
            observations.Remove(old);        // removing it changes neither totals nor grouping
        return false;
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
            && ContainsAllDefault(older.EndingTraces, newer.EndingTraces)
            && ContainsAllDefault(older.Lineage, newer.Lineage)
            && ContainsAllExplainedTraces(older.ExplainedTraces, newer.EndingTraces, newer.ExplainedTraces)
            && ContainsAllOrdinal(older.Identity.StartingPawnIds, newer.Identity.StartingPawnIds);
    }

    private static bool ContainsAllDefault<T>(List<T> required, List<T> container)
    {
        for (var i = 0; i < required.Count; i++)
            if (!container.Contains(required[i]))
                return false;
        return true;
    }

    private static bool ContainsAllExplainedTraces(List<EndingType> required, List<EndingType> endingTraces, List<EndingType> explainedTraces)
    {
        for (var i = 0; i < required.Count; i++)
        {
            var t = required[i];
            if (!endingTraces.Contains(t) && !explainedTraces.Contains(t))
                return false;
        }
        return true;
    }

    private static bool CanPrune(Profile p, SaveObservation older, SaveObservation newer)
    {
        var id = older.Identity;
        var hash = id.FingerprintHash();
        if (id.ModCampaignId != newer.Identity.ModCampaignId || hash != newer.Identity.FingerprintHash()
            || (id.ModCampaignId == null && hash == null))
            return false;

        if (!SameStringSet(id.StartingPawnIds, newer.Identity.StartingPawnIds))
            return false;

        // An older timestamp can decide which branch receives an ambiguous subset.
        var saveObservations = p.SaveObservations;
        for (var i = 0; i < saveObservations.Count; i++)
        {
            if (BlocksPrune(id, hash, saveObservations[i].Identity))
                return false;
        }
        var liveCampaigns = p.LiveCampaigns;
        for (var i = 0; i < liveCampaigns.Count; i++)
        {
            if (BlocksPrune(id, hash, liveCampaigns[i].Identity))
                return false;
        }
        return true;
    }

    private static bool BlocksPrune(ColonyIdentity id, string? hash, ColonyIdentity other)
    {
        var sameCampaign = id.ModCampaignId != null && id.ModCampaignId == other.ModCampaignId;
        var otherHash = other.FingerprintHash();
        return (sameCampaign && otherHash != hash)
            || (hash != null && otherHash == hash && !SameStringSet(id.StartingPawnIds, other.StartingPawnIds));
    }
}
