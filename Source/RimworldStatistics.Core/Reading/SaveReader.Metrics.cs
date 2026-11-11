using System.Security.Cryptography;
using System.Text;
namespace RimworldStatistics.Core;
public sealed partial class SaveReader
{
    /// <summary>
    /// The numeric observation once a complete read is confirmed; optional numeric failures do not invalidate the v1 observation.
    /// </summary>
    private static MetricObservation? TryBuildMetrics(ReadState state, HashingStream hashing)
    {
        if (state.Obs.Status != ObservationStatus.Ok)
            return null;
        try
        {
            return BuildMetrics(state, Convert.ToHexStringLower(hashing.Hash()));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The checkpoint of a save whose numeric read was confirmed (every byte read, the file unchanged): only a readable
    /// checkpoint of the known format; anything else is left in the save untouched and adds nothing.
    /// </summary>
    private static MetricObservation? TryBuildCheckpoint(ReadState s, MetricObservation metrics)
    {
        if (s.CheckpointText is not { } text)
            return null;
        try
        {
            var parsed = CheckpointJson.Parse(text);
            if (parsed.Status != CheckpointParseStatus.Ok)
                return null;
            var exactBoundary = parsed.Payload!.SaveTick == metrics.SaveTick && metrics.ModSetHash is not null;
            var c = CheckpointMetrics.ToObservation(parsed.Payload, text, s.Obs.Identity, s.Obs.GameVersion,
                exactBoundary ? metrics.ActiveDlcIds : null);
            if (exactBoundary)
            {
                c.ModSetHash = metrics.ModSetHash;
                c.CheckpointMetadata = CheckpointMetadataEvidence.Confirmed;
            }
            c.FileAliases.AddRange(metrics.FileAliases.Select(a => new MetricFileAlias { FileName = a.FileName, FileSize = a.FileSize, LastWriteUtc = a.LastWriteUtc }));
            return c;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The Phase A numeric observation of a save whose v1 read succeeded.</summary>
    private static MetricObservation BuildMetrics(ReadState s, string hash)
    {
        var obs = s.Obs;
        var m = new MetricObservation
        {
            Source = MetricObservationSource.SaveFile,
            FormatVersion = Version,
            Key = hash,
            Identity = obs.Identity.Clone(),
            FileAliases = { new MetricFileAlias { FileName = obs.FileName, FileSize = obs.FileSize, LastWriteUtc = obs.LastWriteUtc } },
            GameVersion = obs.GameVersion,
            SaveTick = obs.Values.TicksGame,
            ObservedUtc = obs.LastWriteUtc,
            ActiveDlcIds = s.ModIds?
                .Where(id => id.StartsWith("ludeon.rimworld.", StringComparison.OrdinalIgnoreCase))
                .Select(id => id.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal).ToList(),
            // The full saved MOD list (including order) is hashed, not persisted.
            // Different MOD layouts must never be treated as comparable intervals.
            ModSetHash = s.ModIds is null ? null : Convert.ToHexStringLower(
                SHA256.HashData(Encoding.UTF8.GetBytes(
                    System.Text.Json.JsonSerializer.Serialize(s.ModIds)))),
        };

        // History: a recorder def saved twice is ambiguous and left out, as is a series beyond the extension limits.
        var series = s.Series;
        if (s.WealthTotalCount > 0)
            series[HistoryMetricReader.WealthTotal] = s.WealthTotalCount == 1 && s.Wealth is { } w && w.Length <= HistoryMetricReader.MaxPoints
                && s.WealthTotalPayload is { } p && CountNonWhitespace(p) <= HistoryMetricReader.MaxBase64Chars
                    ? w
                    : null;
        HistoryMetricReader.Summarize(series, obs.GameVersion, obs.Values.TicksGame, m.Metrics);

        // Vanilla counters: the same save's counter and ticks. An omitted counter is 0 only where that default is verified.
        if (s.StatsRecordPresent && obs.Values.TicksGame > 0)
        {
            var years = obs.Values.TicksGame / (double)(StatFormat.TicksPerDay * StatFormat.DaysPerYear);
            var defaultKnown = HistoryMetricReader.MajorMinor(obs.GameVersion) is { } v && CounterDefaultVersions.Contains(v);
            void PerYear(string key, bool written, int counter)
            {
                if (!written && !defaultKnown)
                    return;
                m.Metrics[key] = MetricEntry.Of(MetricKeys.Revision(key), new MetricValue
                {
                    Coverage = MetricCoverage.Complete, RealValue = counter / years, Numerator = counter, Denominator = years,
                });
            }
            PerYear(MetricKeys.RaidsPerYear, s.RaidsWritten, obs.Values.NumRaidsEnemy);
            PerYear(MetricKeys.DeathsPerYear, s.KilledWritten, obs.Values.ColonistsKilled);
        }
        if (s.ModIds is { } mods)
            m.Metrics[MetricKeys.ModsLatest] = MetricEntry.Of(MetricKeys.Revision(MetricKeys.ModsLatest),
                new MetricValue { Coverage = MetricCoverage.SnapshotOnly, IntegerValue = mods.Count });

        RetainedQuestMetricReader.Summarize(s.Quests, s.UnreadableQuests, obs.Values.TicksGame, m.Metrics);

        // Historical salvage is intentionally a partial lower bound. Unsupported
        // versions, missing player identity and ambiguous faction references remain
        // Unknown, not zero. It is independent of the game-side acquisition toggle.
        if (HistoryMetricReader.MajorMinor(obs.GameVersion) == "1.7"
            && HistoricalFactionSnapshotReader.Build(s.HistoricalFactions, s.ScenarioPlayerFactionDef,
                s.HistoricalFactionsOverflowed, m.ActiveDlcIds) is { } historical)
        {
            foreach (var (key, value) in historical)
                m.Metrics[key] = value;
            m.EvidenceByAcquisition = new Dictionary<string, MetricEvidence>(StringComparer.Ordinal)
            {
                ["factions"] = MetricEvidence.Experimental,
            };
        }
        // At most a lower bound of mapped, explicitly player-owned vanilla
        // settlements at this saved tick. Never invent a lifetime maximum.
        if (HistoryMetricReader.MajorMinor(obs.GameVersion) == "1.7"
            && HistoricalHomeMapReader.Build(s.HistoricalMaps, s.HistoricalMapObjects,
                s.HistoricalFactions, s.ScenarioPlayerFactionDef,
                s.HistoricalMapsOverflowed, s.HistoricalMapObjectsOverflowed,
                s.HistoricalFactionsOverflowed,
                s.HistoricalTurrets, s.HistoricalTurretsOverflowed,
                s.HistoricalStock, s.HistoricalStockOverflowed,
                s.HistoricalPawns, s.HistoricalPawnsOverflowed) is { } homes)
        {
            foreach (var (key, value) in homes)
                m.Metrics[key] = value;
            m.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(StringComparer.Ordinal);
            m.EvidenceByAcquisition["facilities"] = MetricEvidence.Experimental;
            if (homes.ContainsKey(StockSnapshot.SilverLatest) || homes.ContainsKey(StockSnapshot.MedicinePeak))
                m.EvidenceByAcquisition["stock"] = MetricEvidence.Experimental;
            if (homes.ContainsKey(RosterSnapshot.Free) || homes.ContainsKey(RosterSnapshot.Prisoners)
                || homes.ContainsKey(RosterSnapshot.Slaves))
                m.EvidenceByAcquisition["roster"] = MetricEvidence.Experimental;
        }
        // P0-C: all reconstructed values remain Partial and Experimental:
        // official 1.7.4985 reference costs are assumed compatible across 1.7,
        // but are not proof of the saved MOD-resolved runtime DefDatabase.
        if (HistoryMetricReader.MajorMinor(obs.GameVersion) == "1.7"
            && HistoricalResearchStateReader.Build(s.SavedResearchKeys, s.SavedResearchValues,
                s.ModIds, s.SavedResearchMalformed) is { } research)
        {
            foreach (var (key, value) in research)
                m.Metrics[key] = value;
            m.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(StringComparer.Ordinal);
            m.EvidenceByAcquisition["research_state"] = MetricEvidence.Experimental;
        }
        return m;
    }

}
