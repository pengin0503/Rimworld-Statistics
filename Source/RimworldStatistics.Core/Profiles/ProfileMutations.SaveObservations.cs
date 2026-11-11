namespace RimworldStatistics.Core;
public static partial class ProfileMutations
{
    /// <summary>
    /// False when the current (non-superseded) observation of this file has the same size and time, was not a failure,
    /// and was read by this reader version or newer (spec §7.1: unchanged files are not read; failed ones are retried).
    /// </summary>
    public static bool NeedsRead(Profile p, string fileName, long size, DateTime lastWriteUtc, int readerVersion)
        => !p.SaveObservations.Any(o => o.FileName == fileName && o.SameFileContent(size, lastWriteUtc) && IsCurrentRead(o, readerVersion));

    public static bool IsCurrentRead(SaveObservation o, int readerVersion)
        => !o.Superseded && o.Status != ObservationStatus.Error && o.ReaderVersion >= readerVersion;

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

}
