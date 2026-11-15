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
                UpdateAncestorCompression(p, existing.Identity.ModCampaignId, recover: true);
                if (existing.ParentKey is { } conflictedParentKey
                    && existing.Identity.ModCampaignId is { Length: > 0 } conflictCampaign)
                {
                    var parent = p.MetricObservations.FirstOrDefault(o => !o.IsQuarantined
                        && o.Source == MetricObservationSource.Checkpoint
                        && o.Identity.ModCampaignId == conflictCampaign && o.Key == conflictedParentKey);
                    if (parent is not null && parent.ArchivedMetrics)
                        p.PendingCheckpointRestorations.Add(parent);
                }
                return;
            }
            foreach (var alias in obs.FileAliases)
                if (!existing.FileAliases.Any(a => a.Same(alias.FileName, alias.FileSize, alias.LastWriteUtc)))
                    existing.FileAliases.Add(alias);
            if (!existing.Conflicted)
            {
                MergeCheckpointMetadata(existing, obs);
                if (existing.ArchivedMetrics)
                    p.PendingCheckpointArchive.Add(obs);
                else
                    MergeMetrics(existing, obs);
            }
            return;
        }
        p.MetricObservations.Add(obs);
        UpdateAncestorCompression(p, obs.Identity.ModCampaignId, recover: false);
        QueueParentRestorationIfBranchOmitsLastCapture(p, obs);
    }

    private static void QueueParentRestorationIfBranchOmitsLastCapture(Profile p, MetricObservation obs)
    {
        if (obs.Conflicted || obs.IsQuarantined || obs.ParentKey is not { } parentKey
            || obs.Identity.ModCampaignId is not { Length: > 0 } campaign)
            return;

        MetricObservation? parent = null;
        List<MetricObservation>? otherChildren = null;
        foreach (var o in p.MetricObservations)
        {
            if (ReferenceEquals(o, obs) || o.IsQuarantined || o.Source != MetricObservationSource.Checkpoint
                || o.Identity.ModCampaignId != campaign)
                continue;
            if (o.Key == parentKey && parent is null)
                parent = o;
            else if (!o.Conflicted && o.ParentKey == parentKey)
                (otherChildren ??= new List<MetricObservation>()).Add(o);
        }

        if (parent is null || !parent.ArchivedMetrics || parent.Conflicted
            || obs.SaveTick <= parent.SaveTick || otherChildren is null)
            return;

        HashSet<string>? lastCaptureKeys = null;
        foreach (var other in otherChildren)
        {
            if (other.SaveTick <= parent.SaveTick)
                continue;
            lastCaptureKeys ??= CheckpointArchivePlanner.LastCaptureCheckpointKeys(p);
            if (lastCaptureKeys.Count == 0)
                return;
            foreach (var (key, entry) in other.Metrics)
            {
                if (entry.IsQuarantined || !lastCaptureKeys.Contains(key))
                    continue;
                if (!obs.Metrics.TryGetValue(key, out var incomingEntry)
                    || incomingEntry.IsQuarantined || incomingEntry.Revision != entry.Revision)
                {
                    p.PendingCheckpointRestorations.Add(parent);
                    return;
                }
            }
        }
    }

    public static void RestoreArchivedMetrics(MetricObservation target, MetricObservation archived)
        => MergeMetrics(target, archived);

    private static void MergeCheckpointMetadata(MetricObservation target, MetricObservation incoming)
    {
        if (target.CheckpointMetadata == CheckpointMetadataEvidence.Conflicting
            || incoming.CheckpointMetadata == CheckpointMetadataEvidence.Provisional)
            return;

        if (incoming.CheckpointMetadata == CheckpointMetadataEvidence.Conflicting
            || (target.CheckpointMetadata == CheckpointMetadataEvidence.Confirmed
                && (target.ModSetHash != incoming.ModSetHash || target.GameVersion != incoming.GameVersion
                    || !HistoricalCounterIntervals.SameSavedDlc(target.ActiveDlcIds, incoming.ActiveDlcIds))))
        {
            target.CheckpointMetadata = CheckpointMetadataEvidence.Conflicting;
            target.ModSetHash = null;
            target.ActiveDlcIds = null;
            target.GameVersion = null;
            return;
        }

        target.CheckpointMetadata = CheckpointMetadataEvidence.Confirmed;
        target.ModSetHash = incoming.ModSetHash;
        target.ActiveDlcIds = incoming.ActiveDlcIds?.ToList();
        target.GameVersion = incoming.GameVersion;
    }

    // A direct, valid later child is sufficient to hide the parent. On a
    // conflict, restore only compressed nodes that lost their last such child.
    private static void UpdateAncestorCompression(Profile p, string? campaignId, bool recover)
    {
        var lineage = p.MetricObservations.Where(o => !o.IsQuarantined && o.Source == MetricObservationSource.Checkpoint
            && o.Identity.ModCampaignId == campaignId).ToList();
        var latestChildTick = new Dictionary<string, int>(Ord);
        foreach (var child in lineage)
            if (!child.Conflicted && child.ParentKey is { } parent
                && (!latestChildTick.TryGetValue(parent, out var tick) || child.SaveTick > tick))
                latestChildTick[parent] = child.SaveTick;

        foreach (var node in lineage)
        {
            if (recover ? !node.Compressed : node.Conflicted || node.Compressed)
                continue;
            var hasLaterChild = latestChildTick.TryGetValue(node.Key, out var tick) && tick > node.SaveTick;
            if (recover != hasLaterChild)
                node.Compressed = hasLaterChild;
        }
    }
}
