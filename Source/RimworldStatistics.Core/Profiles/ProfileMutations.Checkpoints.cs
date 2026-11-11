namespace RimworldStatistics.Core;
public static partial class ProfileMutations
{
    /// <summary>
    /// A checkpoint (numeric expansion design §2 "Cのプロファイル"). The same id with the same content is the same checkpoint
    /// (re-imported, copied, or recovered at load) and only adds file aliases; the same id with other content is a conflict and
    /// that checkpoint is set aside. An ancestor with a confirmed child saved at a later tick can never be the representative
    /// again (the child holds its totals and more). Keep its metrics internally: a later conflict can invalidate the child, and the original ancestor file may be gone.
    /// </summary>
    private static void UpsertCheckpoint(Profile p, MetricObservation obs)
    {
        var existing = p.MetricObservations.FirstOrDefault(o => !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint && o.Key == obs.Key);
        if (existing is not null)
        {
            if (existing.PayloadHash != obs.PayloadHash || existing.ParentKey != obs.ParentKey || existing.SaveTick != obs.SaveTick)
            {
                existing.Conflicted = true;
                UncompressOrphanedAncestors(p, existing);
                return;
            }
            foreach (var alias in obs.FileAliases)
                if (!existing.FileAliases.Any(a => a.Same(alias.FileName, alias.FileSize, alias.LastWriteUtc)))
                    existing.FileAliases.Add(alias);
            if (!existing.Conflicted)
            {
                if (existing.ArchivedMetrics)
                    p.PendingCheckpointArchive.Add(obs);
                else
                    MergeMetrics(existing, obs);
            }
            return;
        }
        p.MetricObservations.Add(obs);
        CompressAncestors(p, obs);
    }

    public static void RestoreArchivedMetrics(MetricObservation target, MetricObservation archived)
        => MergeMetrics(target, archived);

    /// <summary>
    /// A checkpoint was set aside: an ancestor compressed because of it is the representative candidate again. Its values come
    /// back from the retained metrics, even when the original save file is no longer accessible.
    /// </summary>
    private static void UncompressOrphanedAncestors(Profile p, MetricObservation conflicted)
    {
        var lineage = p.MetricObservations.Where(o => !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint
            && o.Identity.ModCampaignId == conflicted.Identity.ModCampaignId).ToList();
        foreach (var node in lineage)
            if (node.Compressed && !lineage.Any(c => !c.Conflicted && c.ParentKey == node.Key && c.SaveTick > node.SaveTick))
                node.Compressed = false;
    }

    private static void CompressAncestors(Profile p, MetricObservation added)
    {
        var lineage = p.MetricObservations.Where(o => !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint && !o.Conflicted
            && o.Identity.ModCampaignId == added.Identity.ModCampaignId).ToList();
        foreach (var node in lineage)
        {
            if (node.Compressed || !lineage.Any(c => c.ParentKey == node.Key && c.SaveTick > node.SaveTick))
                continue;
            node.Compressed = true;
            // A later child may be disqualified by a conflicting checkpoint ID.
            // Dropping this payload would make the ancestor irrecoverable without its save file.
        }
    }

}
