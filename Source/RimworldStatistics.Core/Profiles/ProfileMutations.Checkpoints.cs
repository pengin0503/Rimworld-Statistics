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
        var observations = p.MetricObservations;
        var campaign = obs.Identity.ModCampaignId;
        var parentKey = obs.ParentKey;
        MetricObservation? existing = null;
        MetricObservation? parent = null;
        var obsHasLaterChild = false;

        for (var i = 0; i < observations.Count; i++)
        {
            var o = observations[i];
            if (o.IsQuarantined || o.Source != MetricObservationSource.Checkpoint)
                continue;
            if (o.Key == obs.Key)
            {
                existing = o;
                break;
            }
            if (o.Identity.ModCampaignId != campaign || o.Conflicted)
                continue;
            if (parentKey is not null && o.Key == parentKey && parent is null)
                parent = o;
            if (o.ParentKey == obs.Key && o.SaveTick > obs.SaveTick)
                obsHasLaterChild = true;
        }

        if (existing is not null)
        {
            if (existing.PayloadHash != obs.PayloadHash || existing.ParentKey != obs.ParentKey || existing.SaveTick != obs.SaveTick)
            {
                existing.Conflicted = true;
                UpdateAncestorCompression(p, existing.Identity.ModCampaignId, recover: true);
                if (existing.ParentKey is { } conflictedParentKey
                    && existing.Identity.ModCampaignId is { Length: > 0 } conflictCampaign)
                {
                    for (var i = 0; i < observations.Count; i++)
                    {
                        var candidate = observations[i];
                        if (!candidate.IsQuarantined && candidate.Source == MetricObservationSource.Checkpoint
                            && candidate.Identity.ModCampaignId == conflictCampaign && candidate.Key == conflictedParentKey)
                        {
                            if (candidate.ArchivedMetrics)
                                p.PendingCheckpointRestorations.Add(candidate);
                            break;
                        }
                    }
                }
                return;
            }
            foreach (var alias in obs.FileAliases)
                if (!ContainsFileAlias(existing.FileAliases, alias))
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

        observations.Add(obs);
        if (!obs.IsQuarantined && !obs.Conflicted)
        {
            if (obsHasLaterChild && !obs.Compressed)
                obs.Compressed = true;
            if (parent is not null && !parent.Compressed && obs.SaveTick > parent.SaveTick)
                parent.Compressed = true;
            if (parent is { ArchivedMetrics: true } && campaign is { Length: > 0 } && obs.SaveTick > parent.SaveTick)
                QueueParentRestorationIfBranchOmitsLastCapture(p, obs, parent, campaign, parentKey!);
        }
    }

    private static void QueueParentRestorationIfBranchOmitsLastCapture(Profile p, MetricObservation obs,
        MetricObservation parent, string campaign, string parentKey)
    {
        HashSet<string>? lastCaptureKeys = null;
        var observations = p.MetricObservations;
        for (var i = 0; i < observations.Count; i++)
        {
            var other = observations[i];
            if (ReferenceEquals(other, obs) || other.IsQuarantined || other.Source != MetricObservationSource.Checkpoint
                || other.Conflicted || other.Identity.ModCampaignId != campaign
                || other.ParentKey != parentKey || other.SaveTick <= parent.SaveTick)
                continue;
            lastCaptureKeys ??= CheckpointArchivePlanner.LastCaptureCheckpointKeys(p);
            if (lastCaptureKeys.Count == 0)
                return;
            foreach (var key in lastCaptureKeys)
            {
                if (parent.Metrics.TryGetValue(key, out var parentEntry) && !parentEntry.IsQuarantined)
                    continue;
                var otherHas = other.Metrics.TryGetValue(key, out var entry) && !entry.IsQuarantined;
                var incomingHas = obs.Metrics.TryGetValue(key, out var incomingEntry) && !incomingEntry.IsQuarantined;
                if (otherHas != incomingHas || (otherHas && entry!.Revision != incomingEntry!.Revision))
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
            {
                node.Compressed = hasLaterChild;
                if (!hasLaterChild && node.ArchivedMetrics)
                    p.PendingCheckpointRestorations.Add(node);
            }
        }
    }
}
