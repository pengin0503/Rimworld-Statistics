using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace RimworldStatistics.Core;

/// <summary>The v1 observation of one save and, when the read is confirmed, its Phase A numeric observation.</summary>
public sealed record SaveReadResult(SaveObservation Observation, MetricObservation? Metrics)
{
    /// <summary>The Phase C checkpoint embedded by this mod's component, when it is readable and the read is confirmed.</summary>
    public MetricObservation? Checkpoint { get; init; }
}

/// <summary>
/// Reads one save (.rws) front to back with <see cref="XmlReader"/>, keeping only what the statistics need
/// (research §3.1) and skipping everything else (world grid, maps, mod data) without interpreting it.
/// Never writes to the file and never throws: any failure becomes an observation with <see cref="ObservationStatus.Error"/>.
/// </summary>
public sealed partial class SaveReader
{
    /// <summary>
    /// Bumped whenever the reader's output changes, so stored observations are re-read (spec §7.1). 3: lineage recovery and
    /// numeric validation. 4: Phase A numeric observations (history series, counters, retained quests, content hash).
    /// 5: the Phase C checkpoint embedded in this mod's component (numericStatistics).
    /// 6: bounded, partial legacy faction-relationship evidence (no new live hooks).
    /// 7: real 1.7 omitted default faction ID 0 and per-relation partial salvage.
    /// 8: recover Scribe-omitted neutral relation kinds in historical faction snapshots.
    /// 9: bounded, partial saved player Settlement map observations (not lifetime maxima).
    /// 10: new breakdown outputs rebuilt from the original checkpoint (no counter revision changes).
    /// 11: provisional 1.7 history research completion via audited official 1.7 Def costs (Partial).
    /// 12: evidence-gated positive historical home-map turret counts (Partial; no unknown-as-zero).
    /// 13: conservative 1.7 saved home silver/medicine stacks (Partial; Core Def-only).
    /// 14: saved 1.7 direct-map human guest-status historical roster; retired running generators and nested storage inventory.
    /// 15: drop canceled medical-bed and indoor-room snapshot definitions and prevent legacy values from reimporting.
    /// 16: retry unstable reads and isolate invalid wealth history when the campaign ID is available.
    /// 17: distinguish exact-boundary checkpoint metadata from later files and live load context.
    /// 18: ThreatPoints (10x rescaled) and Adaptation (60000-tick) save history recorders, plus conservative Biotech saved pawn xenotypes.
    /// 19: Anomaly DLC historical entityCodex discoveredEntities, monolith highestLevelReached, and player holding-platform entities.
    /// 20: Biotech DLC historical GameComponent_Bossgroup summon counts and defeated boss types.
    /// 21: World environment info, Ideology fluid reformCount salvage, and auxiliary permanent Tale timestamps.
    /// </summary>
    public const int Version = 21;
    public const string ComponentClassName = "RimworldStatistics.GameComponent_RimworldStatistics";
    /// <summary>
    /// The most permanent tales one save observation keeps: the newest by date, so that a later save of a long colony still
    /// shows its recent tales; older ones stay in the observations of older saves (#393).
    /// </summary>
    public const int MaxPermanentTales = 256;

    /// <summary>A bound on the tales gathered while reading, before the newest <see cref="MaxPermanentTales"/> are chosen.</summary>
    internal const int MaxRawPermanentTales = 65_536;

    private const int MaxReasonLength = 200;

    /// <summary>
    /// Versions where an omitted statsRecord counter is known to mean 0: 1.7 by StatsRecord.ExposeData (1.7.4985), 1.5 and
    /// 1.6 by real saves (an explicitly written raid or death counter is never 0 there, and greatestPopulation is never 3).
    /// </summary>
    private static readonly HashSet<string> CounterDefaultVersions = new(StringComparer.Ordinal) { "1.5", "1.6", "1.7" };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        IgnoreWhitespace = true,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
        CheckCharacters = false,
        CloseInput = false,
    };

    public SaveObservation Read(string path) => ReadDetailed(path).Observation;

    public SaveObservation Read(Stream xml, string fileName, long fileSize, DateTime lastWriteUtc)
        => ReadDetailed(xml, fileName, fileSize, lastWriteUtc).Observation;

    /// <summary>
    /// Reads the file once. The numeric observation is kept only when every byte was read, the byte count equals the size
    /// taken before reading, and the size and time are unchanged afterwards (a file replaced mid-read confirms nothing).
    /// </summary>
    public SaveReadResult ReadDetailed(string path) => ReadUnlessCancelled(path, CancellationToken.None)!;

    /// <summary>
    /// <see cref="ReadDetailed(string)"/> that stops within one 64 KiB block once <paramref name="ct"/> is cancelled (an import
    /// interrupted by a game start, or cancelled by the player). A stopped read returns null: it tells nothing about the
    /// file, so it must not be recorded, neither as read nor as failed. (Not an overload of ReadDetailed: tests see internal
    /// members, and an overload taking a token would make the xUnit analyzer ask every existing call for one.)
    /// </summary>
    internal SaveReadResult? ReadUnlessCancelled(string path, CancellationToken ct)
    {
        var fileName = Path.GetFileName(path);
        long size = 0;
        DateTime lastWrite = default;
        try
        {
            ct.ThrowIfCancellationRequested();
            // Metadata first: if the file changes while we read it, the next scan sees a different size/time and reads it again.
            var info = new FileInfo(path);
            size = info.Length;
            lastWrite = info.LastWriteTimeUtc;
            SaveReadResult result;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
                result = ReadStream(fs, fileName, size, lastWrite, ct);
            if (result.Observation.Status == ObservationStatus.Error)
                return result;
            var after = new FileInfo(path);
            return after.Exists && after.Length == size && after.LastWriteTimeUtc == lastWrite
                ? result
                : new SaveReadResult(Failed(fileName, size, lastWrite, new IOException("save changed during read")), null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception e)
        {
            return new SaveReadResult(Failed(fileName, size, lastWrite, e), null);
        }
    }

    public SaveReadResult ReadDetailed(Stream xml, string fileName, long fileSize, DateTime lastWriteUtc)
        => ReadStream(xml, fileName, fileSize, lastWriteUtc, CancellationToken.None);

    /// <summary>The stream form of <see cref="ReadUnlessCancelled(string, CancellationToken)"/>: null once stopped.</summary>
    internal SaveReadResult? ReadUnlessCancelled(Stream xml, string fileName, long fileSize, DateTime lastWriteUtc, CancellationToken ct)
    {
        try
        {
            return ReadStream(xml, fileName, fileSize, lastWriteUtc, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>Never throws, except <see cref="OperationCanceledException"/> once <paramref name="ct"/> is cancelled.</summary>
    private SaveReadResult ReadStream(Stream xml, string fileName, long fileSize, DateTime lastWriteUtc, CancellationToken ct)
    {
        try
        {
            var obs = NewObservation(fileName, fileSize, lastWriteUtc);
            using var hashing = new HashingStream(xml, fileSize, ct);
            var state = new ReadState(obs);
            using (var reader = XmlReader.Create(hashing, Settings))
            {
                if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "savegame")
                {
                    obs.Status = ObservationStatus.Error;
                    obs.ErrorReason = "not a savegame";
                    return new SaveReadResult(obs, null);
                }
                ForEachChild(reader, name =>
                {
                    switch (name)
                    {
                        case "meta": ReadMeta(Load(reader), state); break;
                        case "game": ReadGame(reader, state); break;
                        default: reader.Skip(); break;
                    }
                });
            }
            hashing.ReadToEnd();
            // A stop that something above swallowed still makes the read worthless: nothing past it was seen.
            if (hashing.Cancelled)
                throw new OperationCanceledException(ct);
            if (hashing.BytesRead != fileSize)
                throw new IOException("save length changed during read");
            Finish(state);
            var hash = TryHash(hashing);
            obs.ContentHash = hash;
            var metrics = hash is null ? null : TryBuildMetrics(state, hash);
            return new SaveReadResult(obs, metrics) { Checkpoint = metrics is null ? null : TryBuildCheckpoint(state, metrics) };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return new SaveReadResult(Failed(fileName, fileSize, lastWriteUtc, e), null);
        }
    }

    private readonly record struct RawSavedTale(PermanentTaleKind Kind, int Id, int DateAbs);

    private sealed class ReadState(SaveObservation obs)
    {
        public SaveObservation Obs { get; } = obs;
        public float[]? Wealth { get; set; }
        public Exception? WealthReadError { get; set; }
        public string? ScenarioPlayerFactionDef { get; set; }
        public List<(string? Def, string? Name)> Factions { get; } = new();
        public char[]? HistoricalAtomValue { get; set; }
        public char[]? HistoricalAtomChunk { get; set; }
        public List<SavedFaction> HistoricalFactions { get; } = new();
        public bool HistoricalFactionsOverflowed { get; set; }
        public List<SavedWorldMapObject> HistoricalMapObjects { get; } = new();
        public List<SavedMapParent> HistoricalMaps { get; } = new();
        public bool HistoricalMapObjectsOverflowed { get; set; }
        public bool HistoricalMapsOverflowed { get; set; }
        public List<SavedMapTurret> HistoricalTurrets { get; } = new();
        public bool HistoricalTurretsOverflowed { get; set; }
        public List<SavedMapStockItem> HistoricalStock { get; } = new();
        public bool HistoricalStockOverflowed { get; set; }
        public List<SavedMapPawn> HistoricalPawns { get; } = new();
        public bool HistoricalPawnsOverflowed { get; set; }
        public List<SavedHeldPlatformPawn> HistoricalHeldEntities { get; } = new();
        public bool HistoricalHeldEntitiesOverflowed { get; set; }
        public bool MechanoidsDeactivated { get; set; }
        public bool RoyalAscentSucceeded { get; set; }
        public bool MechhiveSucceeded { get; set; }
        public string? AnomalyLevelDef { get; set; }
        public int AnomalyComponentCount { get; set; }
        public bool AnomalyComponentMalformed { get; set; }
        public int? AnomalyHighestLevel { get; set; }
        public string? AnomalyMonolithRef { get; set; }
        public bool EntityCodexPresent { get; set; }
        public bool EntityCodexMalformed { get; set; }
        public HashSet<string>? SavedDiscoveredEntities { get; set; }
        public int BossgroupComponentCount { get; set; }
        public bool BossgroupComponentMalformed { get; set; }
        public List<string>? BossgroupKilledDefs { get; set; }
        public List<string>? BossgroupCalledKeys { get; set; }
        public List<int>? BossgroupCalledValues { get; set; }
        public List<SavedIdeo>? SavedIdeos { get; set; }
        public bool SavedIdeosMalformed { get; set; }
        public bool WorldInfoHasMapSize { get; set; }
        public string? WorldOverallTemperature { get; set; }
        public string? WorldOverallRainfall { get; set; }
        public double? WorldPollution { get; set; }
        public bool WorldPollutionMalformed { get; set; }
        public int? WorldInitialMapWidth { get; set; }
        public int? WorldInitialMapHeight { get; set; }
        public bool TickManagerRead { get; set; }
        public List<RawSavedTale>? RawTales { get; set; }
        public bool RawTalesOverflow { get; set; }

        // Numeric expansion inputs (never used by the v1 values above).
        public List<string>? ModIds { get; set; }
        public bool StatsRecordPresent { get; set; }
        public bool RaidsWritten { get; set; }
        public bool KilledWritten { get; set; }
        public bool WealthTotalWithinTextLimit { get; set; }
        public int WealthTotalCount { get; set; }
        public Dictionary<string, float[]?> Series { get; } = new(StringComparer.Ordinal);
        public char[]? HistoryTextChunk { get; set; }
        public List<RetainedQuest>? Quests { get; set; }
        public string? CheckpointText { get; set; }
        public List<string>? SavedResearchKeys { get; set; }
        public List<string>? SavedResearchValues { get; set; }
        public bool SavedResearchMalformed { get; set; }
        public int UnreadableQuests { get; set; }
    }

    private const string AnomalyComponentName = "GameComponent_Anomaly";
    private const string BossgroupComponentName = "GameComponent_Bossgroup";

    private static SaveObservation NewObservation(string fileName, long fileSize, DateTime lastWriteUtc)
        => new() { FileName = fileName, FileSize = fileSize, LastWriteUtc = lastWriteUtc, ReaderVersion = Version, Status = ObservationStatus.Ok };

    private static SaveObservation Failed(string fileName, long fileSize, DateTime lastWriteUtc, Exception e)
    {
        var obs = NewObservation(fileName, fileSize, lastWriteUtc);
        obs.Status = ObservationStatus.Error;
        var reason = $"{e.GetType().Name}: {e.Message}";
        obs.ErrorReason = reason.Length <= MaxReasonLength ? reason : reason[..MaxReasonLength];
        return obs;
    }

    private static void ReadGame(XmlReader r, ReadState s)
    {
        var obs = s.Obs;
        ForEachChild(r, name =>
        {
            switch (name)
            {
                case "info": ReadInfo(Load(r), obs); break;
                case "tickManager":
                    ReadTickManager(Load(r), obs);
                    s.TickManagerRead = true;
                    break;
                case "scenario": ReadScenario(Load(r), s); break;
                case "storyteller": ReadStoryteller(Load(r), obs); break;
                case "storyWatcher":
                    var statsRecord = Load(r).Element("statsRecord");
                    ReadStats(statsRecord, obs.Values);
                    s.StatsRecordPresent = statsRecord != null;
                    s.RaidsWritten = statsRecord?.Element("numRaidsEnemy") != null;
                    s.KilledWritten = statsRecord?.Element("colonistsKilled") != null;
                    break;
                case "researchManager":
                    if (HistoryMetricReader.MajorMinor(s.Obs.GameVersion) == "1.7")
                        ReadSavedResearch(Load(r), s);
                    else r.Skip();
                    break;
                case "entityCodex":
                    if (HistoryMetricReader.MajorMinor(s.Obs.GameVersion) == "1.7")
                        ReadEntityCodex(r, s);
                    else r.Skip();
                    break;
                case "taleManager":
                    if (HistoryMetricReader.MajorMinor(s.Obs.GameVersion) == "1.7")
                        ReadTales(r, s);
                    else r.Skip();
                    break;
                case "world": ReadWorld(r, s); break;
                case "maps":
                    if (HistoryMetricReader.MajorMinor(s.Obs.GameVersion) == "1.7") ReadMaps(r, s);
                    else r.Skip();
                    break;
                case "history": ReadHistory(r, s); break;
                case "questManager": ReadQuests(r, s); break;
                case "components": ReadComponents(r, s); break;
                default: r.Skip(); break;
            }
        });
    }

    private static void ReadTales(XmlReader r, ReadState s)
    {
        ForEachChild(r, child =>
        {
            if (child != "tales") { r.Skip(); return; }
            if (r.AttributeCount > 0 && r.GetAttribute("IsNull") == "True") { r.Skip(); return; }
            ForEachChild(r, item =>
            {
                if (item != "li") { r.Skip(); return; }
                if (s.RawTales is { Count: >= MaxRawPermanentTales })
                {
                    // Tales are saved oldest first, so the ones left here are the newest: which of them were left is unknown.
                    s.RawTalesOverflow = true;
                    r.Skip();
                    return;
                }
                string? def = null;
                int? id = null, date = null;
                var hidden = false;
                var valid = true;
                ForEachChild(r, field =>
                {
                    switch (field)
                    {
                        case "def":
                            var d = ReadHistoricalAtom(r, s);
                            if (def is not null || string.IsNullOrWhiteSpace(d)) valid = false;
                            else def = d;
                            break;
                        case "id":
                            var idText = ReadHistoricalAtom(r, s);
                            if (id is not null || !int.TryParse(idText, NumberStyles.Integer, Inv, out var parsedId) || parsedId < 0)
                                valid = false;
                            else id = parsedId;
                            break;
                        case "date":
                            var dateText = ReadHistoricalAtom(r, s);
                            if (date is not null || !int.TryParse(dateText, NumberStyles.Integer, Inv, out var parsedDate))
                                valid = false;
                            else date = parsedDate;
                            break;
                        case "hidden":
                            var hidText = ReadHistoricalAtom(r, s);
                            if (!bool.TryParse(hidText, out var parsedHid)) valid = false;
                            else hidden = parsedHid;
                            break;
                        default:
                            r.Skip();
                            break;
                    }
                });
                if (!valid || hidden || id is not { } tid || date is not { } tdate)
                    return;
                PermanentTaleKind? kind = def switch
                {
                    "TileSettled" => PermanentTaleKind.TileSettled,
                    "Marriage" => PermanentTaleKind.Marriage,
                    "MajorThreat" => PermanentTaleKind.MajorThreat,
                    _ => null,
                };
                if (kind is { } k)
                {
                    s.RawTales ??= new List<RawSavedTale>();
                    s.RawTales.Add(new RawSavedTale(k, tid, tdate));
                }
            });
        });
    }

    private static void ReadMeta(XElement meta, ReadState s)
    {
        var obs = s.Obs;
        obs.GameVersion = Text(meta.Element("gameVersion"));
        var modIds = meta.Element("modIds");
        obs.ModCount = modIds?.Elements("li").Count() ?? 0;
        s.ModIds = modIds?.Elements("li").Select(li => li.Value.Trim()).Where(id => id.Length > 0).ToList();
    }

    private static void ReadInfo(XElement info, SaveObservation obs)
    {
        obs.Values.RealPlayTime = StatsCalculator.ValidMetricOrZero(Double(info.Element("realPlayTimeInteracting"), 0));
        obs.Display.Permadeath = Bool(info.Element("permadeathMode"), false);
        var tile = Text(info.Element("startingTile"));
        obs.Identity.StartingTile = string.IsNullOrWhiteSpace(tile) ? null : ColonyIdentity.NormalizeTile(tile);
        obs.Identity.StartingPawnIds = info.Element("startingAndOptionalPawns")?.Elements("li")
            .Select(li => li.Value.Trim())
            .Where(id => id.Length > 0 && id != "null")
            .ToList() ?? new List<string>();
    }

    private static void ReadTickManager(XElement tm, SaveObservation obs)
    {
        obs.Values.TicksGame = Int(tm.Element("ticksGame"), 0);
        obs.Identity.GameStartAbsTick = Int(tm.Element("gameStartAbsTick"), 0);
    }

    private static void ReadStoryteller(XElement st, SaveObservation obs)
    {
        obs.Display.Storyteller = Text(st.Element("def"));
        obs.Display.Difficulty = Text(st.Element("difficulty"));
    }

    private static void ReadStats(XElement? sr, ObservedValues v)
    {
        v.NumRaidsEnemy = Int(sr?.Element("numRaidsEnemy"), 0);
        v.NumThreatsQueued = Int(sr?.Element("numThreatsQueued"), 0);
        v.ColonistsKilled = Int(sr?.Element("colonistsKilled"), 0);
        v.ColonistsLaunched = Int(sr?.Element("colonistsLaunched"), 0);
        v.GreatestPopulation = Int(sr?.Element("greatestPopulation"), 3);
    }

    private static void ReadScenario(XElement scenario, ReadState s)
    {
        s.Obs.Display.Scenario = Text(scenario.Element("name"));
        s.ScenarioPlayerFactionDef = Text(scenario.Element("playerFaction")?.Element("factionDef"))?.Trim();
    }

    private static void Finish(ReadState s)
    {
        var obs = s.Obs;
        obs.Display.FactionName = PlayerFactionName(s);
        if (s.TickManagerRead && s.RawTales is { Count: > 0 } rawTales)
        {
            var converted = new List<PermanentTaleFact>(rawTales.Count);
            var seenIds = new HashSet<(PermanentTaleKind, int)>();
            foreach (var raw in rawTales)
            {
                var relTick = (long)raw.DateAbs - obs.Identity.GameStartAbsTick;
                if (relTick < 0 || relTick > obs.Values.TicksGame || relTick > int.MaxValue)
                    continue;
                if (seenIds.Add((raw.Kind, raw.Id)))
                    converted.Add(new PermanentTaleFact(raw.Kind, raw.Id, (int)relTick));
            }
            if (converted.Count > 0)
            {
                converted.Sort(static (a, b) =>
                {
                    var c = a.TicksGame.CompareTo(b.TicksGame);
                    if (c != 0) return c;
                    c = a.Kind.CompareTo(b.Kind);
                    return c != 0 ? c : a.Id.CompareTo(b.Id);
                });
                if (converted.Count > MaxPermanentTales)
                {
                    // Keep the newest; every tale after the latest one left out is kept (#393).
                    var dropped = converted.Count - MaxPermanentTales;
                    obs.PermanentTalesDroppedThroughTick = converted[dropped - 1].TicksGame;
                    converted.RemoveRange(0, dropped);
                }
                obs.PermanentTales = converted;
            }
            if (s.RawTalesOverflow)
                obs.PermanentTalesDroppedThroughTick = obs.Values.TicksGame;   // which of the newest were left is unknown
        }
        TraceInputs Inputs(IReadOnlyCollection<EndingType> lineage) => new()
        {
            ColonistsLaunched = obs.Values.ColonistsLaunched,
            RoyalAscentSucceeded = s.RoyalAscentSucceeded,
            MechhiveSucceeded = s.MechhiveSucceeded,
            MechanoidsDeactivated = s.MechanoidsDeactivated,
            AnomalyLevelDef = s.AnomalyLevelDef,
            LineageTypes = lineage,
        };
        obs.EndingTraces = EndingTraces.Detect(Inputs(obs.Lineage.Select(e => e.Type).Distinct().ToArray()));
        // What the lineage explained is kept: it is the only evidence if the profile lost the matching ending record.
        obs.ExplainedTraces = EndingTraces.Detect(Inputs(Array.Empty<EndingType>())).Except(obs.EndingTraces).ToList();
        if (s.Wealth is { Length: > 0 } w)
        {
            if (StatsCalculator.ValidMetric(w[0]))
                obs.Identity.FirstWealthBits = BitConverter.SingleToUInt32Bits(w[0]);
            if (w.All(value => StatsCalculator.ValidMetric(value)))
            {
                obs.Values.WealthMax = w.Max();
                obs.Values.WealthLast = w[^1];
                obs.Values.WealthPoints = w.Length;
            }
        }

        if (obs.Identity.FirstWealthBits is null && obs.Identity.ModCampaignId is null && s.WealthReadError is { } error)
            throw new InvalidDataException(error.Message, error);

        var missing = new List<string>(3);
        if (obs.Identity.SeedString is null) missing.Add("seedString");
        if (obs.Identity.StartingTile is null) missing.Add("startingTile");
        if (obs.Identity.FirstWealthBits is null) missing.Add("Wealth_Total");
        if (missing.Count > 0 && obs.Identity.ModCampaignId is null)
        {
            obs.Status = ObservationStatus.Unidentifiable;
            obs.ErrorReason = string.Join(",", missing);
        }
    }

    /// <summary>
    /// Faction.OfPlayer is the first faction whose def has isPlayer. The save does not carry that flag, so match the
    /// scenario's player faction def (e.g. Odyssey's "GravshipCrew"), falling back to defs named "Player…".
    /// </summary>
    private static string? PlayerFactionName(ReadState s)
    {
        if (s.ScenarioPlayerFactionDef is { Length: > 0 } def)
        {
            foreach (var f in s.Factions)
                if (f.Def == def) return f.Name;
        }
        foreach (var f in s.Factions)
            if (f.Def is not null && f.Def.StartsWith("Player", StringComparison.Ordinal)) return f.Name;
        return null;
    }

    /// <summary>
    /// Calls <paramref name="onChild"/> with the reader on each child element of the current element.
    /// The callback must consume that child entirely. Leaves the reader after the current element's end tag.
    /// </summary>
    private static void ForEachChild(XmlReader r, Action<string> onChild)
    {
        if (r.IsEmptyElement)
        {
            r.Read();
            return;
        }
        var depth = r.Depth;
        r.Read();
        while (true)
        {
            if (r.EOF)
                throw new XmlException("unexpected end of file");
            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth)
            {
                r.Read();
                return;
            }
            if (r.NodeType == XmlNodeType.Element)
                onChild(r.LocalName);
            else
                r.Read();
        }
    }

    /// <summary>Loads the current (small) element and leaves the reader after it.</summary>
    private static XElement Load(XmlReader r) => (XElement)XNode.ReadFrom(r);

    private static string? Text(XElement? e) => e?.Value;

    private static int Int(XElement? e, int fallback)
        => e is null ? fallback : int.Parse(e.Value.Trim(), NumberStyles.Integer, Inv);

    private static double Double(XElement? e, double fallback)
        => e is null ? fallback : double.Parse(e.Value.Trim(), NumberStyles.Float, Inv);

    private static bool Bool(XElement? e, bool fallback)
        => e is null ? fallback : bool.Parse(e.Value.Trim());
}
