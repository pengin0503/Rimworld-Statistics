using System.Text.Json;
using System.Text.Json.Nodes;

namespace RimworldStatistics.Core;

/// <summary>Which saves a scan reads (#310).</summary>
public enum ImportMode
{
    /// <summary>New and changed saves (another name, size or time), failed reads and reads of an older reader.</summary>
    Incremental,

    /// <summary>
    /// Every save, read again in full and compared with its record. A save that holds what its record was read from is not
    /// written at all; only a save whose bytes differ from its record (although its name, size and time are the same) or
    /// that has no good record is committed, by the rules of an ordinary read.
    /// </summary>
    ForcedRecheck,
}

/// <summary>What a forced re-check found for one save.</summary>
public enum RecheckOutcome
{
    /// <summary>No good record of this file (name, size, time) by this reader: committed as an ordinary read.</summary>
    New,

    /// <summary>The file holds the bytes its record was read from: nothing is written.</summary>
    Unchanged,

    /// <summary>
    /// The same name, size and time but other bytes: committed as a changed file. The earlier record stays as history
    /// (superseded) unless the new one provably makes it redundant, as when a save is overwritten.
    /// </summary>
    Changed,

    /// <summary>The re-read failed while a good record exists: the record is kept and nothing is written.</summary>
    FailedKept,

    /// <summary>A newer version of this MOD read the file: its record is left as it is.</summary>
    NewerRecord,
}

/// <summary>Compares a forced re-read of a save with the profile's record of that file (#310).</summary>
public static class ForcedRecheck
{
    public static RecheckOutcome Classify(Profile p, SaveReadResult result)
    {
        var obs = result.Observation;
        List<SaveObservation>? records = null;
        foreach (var o in p.SaveObservations)
        {
            if (!o.Superseded && o.Status != ObservationStatus.Error && o.FileName == obs.FileName
                && o.SameFileContent(obs.FileSize, obs.LastWriteUtc))
                (records ??= new List<SaveObservation>(1)).Add(o);
        }
        if (records is null)
            return RecheckOutcome.New;
        if (obs.Status == ObservationStatus.Error)
            return RecheckOutcome.FailedKept;   // never hide a good record behind a failure
        if (records.Exists(r => r.ReaderVersion > obs.ReaderVersion))
            return RecheckOutcome.NewerRecord;
        var sameReader = records.FindAll(r => r.ReaderVersion == obs.ReaderVersion);
        if (sameReader.Count == 0)
            return RecheckOutcome.New;          // an older reader's record: an ordinary scan reads this file again too
        return sameReader.Exists(r => SameBytes(p, r, result)) ? RecheckOutcome.Unchanged : RecheckOutcome.Changed;
    }

    /// <summary>
    /// Whether <paramref name="record"/> was read from the bytes just read, proven by (in this order) the record's content
    /// hash, the keys (content hashes) of the numeric observations read from this exact file, or, where neither exists (a
    /// record made before content hashes whose numeric observation was compacted away, or one that is not Ok), by the record
    /// itself: the reader is deterministic, so the same bytes give the same record.
    /// </summary>
    internal static bool SameBytes(Profile p, SaveObservation record, SaveReadResult result)
    {
        var hash = result.Observation.ContentHash;
        if (record.ContentHash is { } recorded && hash is not null)
            return recorded == hash;
        if (hash is not null)
        {
            var anyNumeric = false;
            foreach (var m in p.MetricObservations)
            {
                if (m.IsQuarantined || m.Source != MetricObservationSource.SaveFile || !HasAlias(m, record))
                    continue;
                if (m.Key == hash)
                    return true;
                anyNumeric = true;
            }
            if (anyNumeric)
                return false;
        }
        return SameRecord(record, result.Observation);
    }

    private static bool HasAlias(MetricObservation m, SaveObservation record)
    {
        foreach (var a in m.FileAliases)
            if (a.Same(record.FileName, record.FileSize, record.LastWriteUtc))
                return true;
        return false;
    }

    private static readonly string[] NotRead = ["superseded", "contentHash"];

    /// <summary>The two records hold the same values (everything but the superseded mark, the hash and unknown fields).</summary>
    internal static bool SameRecord(SaveObservation a, SaveObservation b)
        => JsonNode.DeepEquals(Comparable(a), Comparable(b));

    private static JsonNode? Comparable(SaveObservation o)
    {
        var node = JsonSerializer.SerializeToNode(o, ProfileJson.CompactOptions);
        if (node is JsonObject obj)
        {
            foreach (var name in NotRead)
                obj.Remove(name);
            if (o.ExtensionData is { } unknown)
                foreach (var name in unknown.Keys)
                    obj.Remove(name);
        }
        return node;
    }
}
