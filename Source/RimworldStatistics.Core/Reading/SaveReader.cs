using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
public sealed class SaveReader
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
    /// </summary>
    public const int Version = 13;
    public const string ComponentClassName = "RimworldStatistics.GameComponent_RimworldStatistics";

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
    public SaveReadResult ReadDetailed(string path)
    {
        var fileName = Path.GetFileName(path);
        long size = 0;
        DateTime lastWrite = default;
        try
        {
            // Metadata first: if the file changes while we read it, the next scan sees a different size/time and reads it again.
            var info = new FileInfo(path);
            size = info.Length;
            lastWrite = info.LastWriteTimeUtc;
            SaveReadResult result;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan))
                result = ReadDetailed(fs, fileName, size, lastWrite);
            if (result.Metrics is null)
                return result;
            var after = new FileInfo(path);
            return after.Exists && after.Length == size && after.LastWriteTimeUtc == lastWrite ? result : result with { Metrics = null, Checkpoint = null };
        }
        catch (Exception e)
        {
            return new SaveReadResult(Failed(fileName, size, lastWrite, e), null);
        }
    }

    public SaveReadResult ReadDetailed(Stream xml, string fileName, long fileSize, DateTime lastWriteUtc)
    {
        try
        {
            var obs = NewObservation(fileName, fileSize, lastWriteUtc);
            using var hashing = new HashingStream(xml);
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
            Finish(state);
            var metrics = TryBuildMetrics(state, hashing, fileSize);
            return new SaveReadResult(obs, metrics) { Checkpoint = metrics is null ? null : TryBuildCheckpoint(state, metrics) };
        }
        catch (Exception e)
        {
            return new SaveReadResult(Failed(fileName, fileSize, lastWriteUtc, e), null);
        }
    }

    private sealed class ReadState(SaveObservation obs)
    {
        public SaveObservation Obs { get; } = obs;
        public float[]? Wealth { get; set; }
        public string? ScenarioPlayerFactionDef { get; set; }
        public List<(string? Def, string? Name)> Factions { get; } = new();
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
        public bool MechanoidsDeactivated { get; set; }
        public bool RoyalAscentSucceeded { get; set; }
        public bool MechhiveSucceeded { get; set; }
        public string? AnomalyLevelDef { get; set; }

        // Numeric expansion inputs (never used by the v1 values above).
        public List<string>? ModIds { get; set; }
        public bool StatsRecordPresent { get; set; }
        public bool RaidsWritten { get; set; }
        public bool KilledWritten { get; set; }
        public string? WealthTotalPayload { get; set; }
        public int WealthTotalCount { get; set; }
        public Dictionary<string, List<float[]?>> Series { get; } = new(StringComparer.Ordinal);
        public List<RetainedQuest>? Quests { get; set; }
        public string? CheckpointText { get; set; }
        public List<string>? SavedResearchKeys { get; set; }
        public List<string>? SavedResearchValues { get; set; }
        public bool SavedResearchMalformed { get; set; }
        public int UnreadableQuests { get; set; }
    }

    private const string AnomalyComponentName = "GameComponent_Anomaly";

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
                case "tickManager": ReadTickManager(Load(r), obs); break;
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

    /// <summary>
    /// Parse only researchManager.progress keys/values. Nothing outside 1.7 is
    /// interpreted; no vanilla cost is inferred from raw progress alone.
    /// </summary>
    private static void ReadSavedResearch(XElement manager, ReadState state)
    {
        var data = manager.Element("progress");
        if (data is null)
            return;
        var keys = data.Element("keys");
        var values = data.Element("values");
        if (keys is null || values is null)
        {
            state.SavedResearchMalformed = true;
            return;
        }
        const int maxProjects = 8192;
        var readKeys = keys.Elements("li").Take(maxProjects + 1).Select(x => x.Value.Trim()).ToList();
        var readValues = values.Elements("li").Take(maxProjects + 1).Select(x => x.Value.Trim()).ToList();
        if (readKeys.Count > maxProjects || readValues.Count > maxProjects)
        {
            state.SavedResearchMalformed = true;
            return;
        }
        state.SavedResearchKeys = readKeys;
        state.SavedResearchValues = readValues;
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

    private static void ReadWorld(XmlReader r, ReadState s)
    {
        var obs = s.Obs;
        ForEachChild(r, name =>
        {
            switch (name)
            {
                case "info":
                    var info = Load(r);
                    obs.Display.WorldName = Text(info.Element("name"));
                    obs.Identity.SeedString = Text(info.Element("seedString"));
                    obs.Identity.PersistentRandomValue = Int(info.Element("persistentRandomValue"), 0);
                    break;
                case "worldObjects":
                    if (HistoryMetricReader.MajorMinor(s.Obs.GameVersion) == "1.7") ReadWorldMapObjects(r, s);
                    else r.Skip();
                    break;
                case "factionManager":
                    ForEachChild(r, fm =>
                    {
                        if (fm != "allFactions") { r.Skip(); return; }
                        ForEachChild(r, _ => ReadFaction(r, s));   // allFactions/li
                    });
                    break;
                default:
                    r.Skip();
                    break;
            }
        });
    }


    /// <summary>
    /// Reads bounded map-parent keys and narrow validated Thing class/Def evidence.
    /// All terrain, pawns and components are skipped, never materialized as object trees.
    /// </summary>
    private static void ReadMaps(XmlReader r, ReadState s)
    {
        ForEachChild(r, item =>
        {
            if (item != "li") { r.Skip(); return; }
            if (s.HistoricalMaps.Count >= HistoricalHomeMapReader.MaxMaps)
            {
                s.HistoricalMapsOverflowed = true;
                r.Skip();
                return;
            }
            int? id = null;
            string? parent = null;
            bool? pocket = null;
            var valid = true;
            var mapInfoSeen = false;
            var onMapTurrets = new List<(string? Id, string? Faction, bool Valid)>();
            var onMapStock = new List<(string? Id, string? Def, int Count, string? Faction, bool Forbidden, bool Valid)>();
            ForEachChild(r, child =>
            {
                switch (child)
                {
                    case "uniqueID":
                        var text = ReadHistoricalAtom(r);
                        if (id is not null || !int.TryParse(text, NumberStyles.Integer, Inv, out var parsedId)
                            || parsedId < 0)
                            valid = false;
                        else
                            id = parsedId;
                        break;
                    case "things":
                        ReadSavedMapThings(r, s, onMapTurrets, onMapStock);
                        break;
                    case "mapInfo":
                        if (mapInfoSeen) valid = false;
                        mapInfoSeen = true;
                        ForEachChild(r, field =>
                        {
                            switch (field)
                            {
                                case "parent":
                                    var value = ReadHistoricalAtom(r);
                                    if (parent is not null || string.IsNullOrWhiteSpace(value)) valid = false;
                                    else parent = value;
                                    break;
                                case "isPocketMap":
                                    var flag = ReadHistoricalAtom(r);
                                    if (pocket is not null || !bool.TryParse(flag, out var isPocket))
                                        valid = false;
                                    else pocket = isPocket;
                                    break;
                                default: r.Skip(); break;
                            }
                        });
                        break;
                    default: r.Skip(); break;
                }
            });
            s.HistoricalMaps.Add(new SavedMapParent(id, parent, pocket, valid));
            foreach (var turret in onMapTurrets)
                s.HistoricalTurrets.Add(new SavedMapTurret(id, turret.Id, turret.Faction, valid && turret.Valid));
            foreach (var stockItem in onMapStock)
                s.HistoricalStock.Add(new SavedMapStockItem(id, stockItem.Id, stockItem.Def, stockItem.Count,
                    stockItem.Faction, stockItem.Forbidden, valid && stockItem.Valid));
        });
    }

    /// <summary>
    /// Streaming, bounded extraction of specific vanilla building and stock
    /// classes proven by the 1.7.4985 game Def/type index. This never loads
    /// the full maps/things into XElement trees or interprets unknown MOD defs.
    /// </summary>
    private static void ReadSavedMapThings(XmlReader r, ReadState s,
        List<(string? Id, string? Faction, bool Valid)> turrets,
        List<(string? Id, string? Def, int Count, string? Faction, bool Forbidden, bool Valid)> stock)
    {
        ForEachChild(r, node =>
        {
            if (node != "thing") { r.Skip(); return; }
            var className = r.GetAttribute("Class");
            var isTurret = className is "Building_TurretGun" or "Building_Turret";
            var stockCandidate = className is "ThingWithComps" or "Medicine";
            if (!isTurret && !stockCandidate) { r.Skip(); return; }
            if (isTurret && s.HistoricalTurrets.Count + turrets.Count >= HistoricalHomeMapReader.MaxSavedTurrets)
            {
                s.HistoricalTurretsOverflowed = true;
                r.Skip();
                return;
            }

            string? id = null, faction = null, def = null;
            var count = 1;
            var forbidden = false;
            var valid = true;
            var idSeen = false;
            var factionSeen = false;
            var defSeen = false;
            var countSeen = false;
            var forbiddenSeen = false;
            ForEachChild(r, field =>
            {
                switch (field)
                {
                    case "id":
                        var idValue = ReadHistoricalAtom(r);
                        if (idSeen || string.IsNullOrWhiteSpace(idValue)) valid = false;
                        idSeen = true;
                        id = idValue;
                        break;
                    case "def":
                        var defValue = ReadHistoricalAtom(r);
                        if (defSeen || string.IsNullOrWhiteSpace(defValue)) valid = false;
                        defSeen = true;
                        def = defValue;
                        break;
                    case "faction":
                        var factionValue = ReadHistoricalAtom(r);
                        if (factionSeen || string.IsNullOrWhiteSpace(factionValue)) valid = false;
                        factionSeen = true;
                        faction = factionValue is "null" ? null : factionValue;
                        break;
                    case "stackCount":
                        var countValue = ReadHistoricalAtom(r);
                        if (countSeen || !int.TryParse(countValue, NumberStyles.Integer, Inv, out var number))
                            valid = false;
                        else count = number;
                        countSeen = true;
                        break;
                    case "forbidden":
                        var forbiddenValue = ReadHistoricalAtom(r);
                        if (forbiddenSeen || !bool.TryParse(forbiddenValue, out var isForbidden))
                            valid = false;
                        else forbidden = isForbidden;
                        forbiddenSeen = true;
                        break;
                    default: r.Skip(); break;
                }
            });

            if (isTurret)
            {
                turrets.Add((id, faction, valid && !string.IsNullOrWhiteSpace(id)
                    && !string.IsNullOrWhiteSpace(faction)));
                return;
            }
            // The actual official Core 1.7 resolved ThingDefs define precisely
            // these Class/Def pairings. Unknown MOD medicine or other classes
            // may exist; their omission is why this is always Partial.
            if (className == "ThingWithComps" && def != "Silver"
                || className == "Medicine" && def is not ("MedicineHerbal" or "MedicineIndustrial" or "MedicineUltratech"))
                return;
            if (s.HistoricalStock.Count + stock.Count >= HistoricalHomeMapReader.MaxSavedStockItems)
            {
                s.HistoricalStockOverflowed = true;
                return;
            }
            stock.Add((id, def, count, faction, forbidden,
                valid && !string.IsNullOrWhiteSpace(id)
                && count > 0 && count <= HistoricalHomeMapReader.MaxTrustedStockStack));
        });
    }

    /// <summary>Only world object IDs, Defs and owning-faction references are retained.</summary>
    private static void ReadWorldMapObjects(XmlReader r, ReadState s)
    {
        ForEachChild(r, child =>
        {
            if (child != "worldObjects") { r.Skip(); return; }
            ForEachChild(r, item =>
            {
                if (item != "li") { r.Skip(); return; }
                if (s.HistoricalMapObjects.Count >= HistoricalHomeMapReader.MaxWorldObjects)
                {
                    s.HistoricalMapObjectsOverflowed = true;
                    r.Skip();
                    return;
                }
                int? id = null;
                string? def = null, faction = null;
                var valid = true;
                ForEachChild(r, field =>
                {
                    switch (field)
                    {
                        case "ID":
                            var text = ReadHistoricalAtom(r);
                            if (id is not null || !int.TryParse(text, NumberStyles.Integer, Inv, out var parsed)
                                || parsed < 0)
                                valid = false;
                            else id = parsed;
                            break;
                        case "def":
                            var d = ReadHistoricalAtom(r);
                            if (def is not null || string.IsNullOrWhiteSpace(d)) valid = false;
                            else def = d;
                            break;
                        case "faction":
                            var f = ReadHistoricalAtom(r);
                            if (faction is not null || string.IsNullOrWhiteSpace(f)) valid = false;
                            else faction = f;
                            break;
                        default: r.Skip(); break;
                    }
                });
                s.HistoricalMapObjects.Add(new SavedWorldMapObject(id, def, faction, valid));
            });
        });
    }

    /// <summary>Fixed-memory small XML scalar reader, including overlong whitespace.</summary>
    private static string? ReadHistoricalAtom(XmlReader r)
    {
        if (r.IsEmptyElement) { r.Read(); return ""; }
        var depth = r.Depth;
        r.Read();
        var value = new StringBuilder(64);
        var buffer = new char[64];
        var valid = true;
        while (!(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            if (r.EOF) throw new XmlException("unexpected end of historical field");
            if (r.NodeType is XmlNodeType.Text or XmlNodeType.CDATA
                or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
            {
                int count;
                while ((count = r.ReadValueChunk(buffer, 0, buffer.Length)) > 0)
                {
                    if (!valid) continue;
                    if (value.Length + count > 128)
                    {
                        value.Clear();
                        valid = false;
                    }
                    else value.Append(buffer, 0, count);
                }
                r.Read();
            }
            else if (r.NodeType == XmlNodeType.Element)
            {
                valid = false;
                r.Skip();
            }
            else r.Read();
        }
        r.Read();
        return valid ? value.ToString().Trim() : null;
    }

    /// <summary>Keep the v1 faction identity reader, and selectively gather small
    /// first-party relation facts for historical, explicitly partial snapshots.</summary>
    private static void ReadFaction(XmlReader r, ReadState s)
    {
        string? def = null, name = null;
        int? loadId = null;
        bool deactivated = false, defeated = false, hidden = false, temporary = false, valid = true;
        var relations = new List<SavedFactionRelation>();
        ForEachChild(r, child =>
        {
            switch (child)
            {
                case "def": def = r.ReadElementContentAsString().Trim(); break;
                case "name": name = r.ReadElementContentAsString(); break;
                case "loadID":
                    if (int.TryParse(r.ReadElementContentAsString().Trim(), NumberStyles.Integer, Inv, out var id) && id >= 0)
                        loadId = id;
                    else valid = false;
                    break;
                case "deactivated":
                    if (bool.TryParse(r.ReadElementContentAsString().Trim(), out var wasDeactivated))
                        deactivated = wasDeactivated;
                    else
                        valid = false;
                    break;
                case "hidden":
                    if (bool.TryParse(r.ReadElementContentAsString().Trim(), out var wasHidden))
                        hidden = wasHidden;
                    else
                        valid = false;
                    break;
                case "temporary":
                    if (bool.TryParse(r.ReadElementContentAsString().Trim(), out var wasTemporary))
                        temporary = wasTemporary;
                    else
                        valid = false;
                    break;
                case "defeated":
                    if (bool.TryParse(r.ReadElementContentAsString().Trim(), out var wasDefeated))
                        defeated = wasDefeated;
                    else
                        valid = false;
                    break;
                case "relations":
                    // Most modded NPC factions are not in the audited allow-list.
                    // Skip potentially large relation subtrees without allocating
                    // or interpreting them. XML normally writes def before relations.
                    if (s.HistoricalFactionsOverflowed || !HistoricalFactionSnapshotReader.MayContainSupportedFacts(def))
                    {
                        r.Skip();
                        break;
                    }
                    ForEachChild(r, _ =>
                    {
                        if (relations.Count >= HistoricalFactionSnapshotReader.MaxRelationsPerFaction)
                        {
                            valid = false;
                            r.Skip();
                            return;
                        }
                        relations.Add(ReadFactionRelation(r));
                    });
                    break;
                default: r.Skip(); break;
            }
        });
        s.Factions.Add((def, name));
        if (def == "Mechanoid" && deactivated)
            s.MechanoidsDeactivated = true;
        if (s.HistoricalFactions.Count >= HistoricalFactionSnapshotReader.MaxFactions)
            s.HistoricalFactionsOverflowed = true;
        else
            s.HistoricalFactions.Add(new SavedFaction(def, loadId,
                !deactivated && !defeated && !hidden && !temporary, relations, valid));
    }

    private static SavedFactionRelation ReadFactionRelation(XmlReader r)
    {
        string? other = null, kind = null;
        int? goodwill = null;
        var valid = true;
        ForEachChild(r, name =>
        {
            switch (name)
            {
                case "other": other = r.ReadElementContentAsString().Trim(); break;
                case "kind": kind = r.ReadElementContentAsString().Trim(); break;
                case "goodwill":
                    if (int.TryParse(r.ReadElementContentAsString().Trim(), NumberStyles.Integer, Inv, out var parsed))
                        goodwill = parsed;
                    else valid = false;
                    break;
                default: r.Skip(); break;
            }
        });
        return new SavedFactionRelation(other, kind, goodwill, valid);
    }

    private static void ReadQuests(XmlReader r, ReadState s)
    {
        ForEachChild(r, name =>
        {
            if (name != "quests") { r.Skip(); return; }
            s.Quests ??= new List<RetainedQuest>();
            ForEachChild(r, _ =>                                   // quests/li
            {
                string? root = null, outcome = null;
                var ended = false;
                // Numeric expansion fields: a value that does not parse leaves this quest out of the quest metrics only.
                int id = 0, accepted = -1, expire = -1, cleanup = -1;
                var initially = false;
                var readable = true;
                void Int(ref int field)
                {
                    if (!int.TryParse(r.ReadElementContentAsString().Trim(), NumberStyles.Integer, Inv, out field))
                        readable = false;
                }
                ForEachChild(r, child =>
                {
                    switch (child)
                    {
                        case "root": root = r.ReadElementContentAsString().Trim(); break;
                        case "ended": ended = bool.Parse(r.ReadElementContentAsString().Trim()); break;
                        case "endOutcome": outcome = r.ReadElementContentAsString().Trim(); break;
                        case "id": Int(ref id); break;
                        case "acceptanceTick": Int(ref accepted); break;
                        case "acceptanceExpireTick": Int(ref expire); break;
                        case "cleanupTick": Int(ref cleanup); break;
                        case "initiallyAccepted":
                            if (!bool.TryParse(r.ReadElementContentAsString().Trim(), out initially))
                                readable = false;
                            break;
                        default: r.Skip(); break;
                    }
                });
                if (readable)
                    s.Quests.Add(new RetainedQuest(id, root, accepted, expire, initially, ended, outcome ?? "Unknown", cleanup));
                else
                    s.UnreadableQuests++;
                // Quest.State == EndedSuccess requires ended && endOutcome == Success (1.7.4985).
                if (!ended || outcome != "Success")
                    return;
                if (root == "EndGame_RoyalAscent") s.RoyalAscentSucceeded = true;
                else if (root == "Gravcore_Mechhive") s.MechhiveSucceeded = true;
            });
        });
    }

    private static void ReadComponents(XmlReader r, ReadState s)
    {
        ForEachChild(r, _ =>                                       // components/li[@Class]
        {
            // Scribe writes the class name without ignored namespaces such as "RimWorld." (GenTypes).
            var cls = r.GetAttribute("Class");
            if (cls == ComponentClassName)
                ReadOwnComponent(r, s);
            else if (cls is AnomalyComponentName or "RimWorld." + AnomalyComponentName)
                ForEachChild(r, child =>
                {
                    if (child == "levelDef") s.AnomalyLevelDef = r.ReadElementContentAsString().Trim();
                    else r.Skip();
                });
            else
                r.Skip();
        });
    }

    private static void ReadOwnComponent(XmlReader r, ReadState s)
    {
        ForEachChild(r, child =>
        {
            switch (child)
            {
                case "campaignId":
                    var id = r.ReadElementContentAsString().Trim();
                    s.Obs.Identity.ModCampaignId = id.Length == 0 ? null : id;
                    break;
                case "numericStatistics":
                    // Kept exactly as written: its hash identifies the checkpoint's content.
                    s.CheckpointText = r.GetAttribute("IsNull") == "True" ? null : r.ReadElementContentAsString();
                    if (s.CheckpointText is null)
                        r.Skip();
                    break;
                case "lineageEndings":
                    ForEachChild(r, _ =>
                    {
                        // An unreadable token is ignored rather than failing the whole save.
                        if (LineageEntry.TryParse(r.ReadElementContentAsString().Trim(), out var entry))
                            s.Obs.Lineage.Add(entry);
                    });
                    break;
                default:
                    r.Skip();
                    break;
            }
        });
    }

    private static void ReadHistory(XmlReader r, ReadState s)
    {
        ForEachChild(r, name =>
        {
            if (name != "autoRecorderGroups") { r.Skip(); return; }
            ForEachChild(r, _ =>                                   // autoRecorderGroups/li
                ForEachChild(r, groupChild =>
                {
                    if (groupChild != "recorders") { r.Skip(); return; }
                    ForEachChild(r, _ => ReadRecorder(r, s));      // recorders/li
                }));
        });
    }

    private static void ReadRecorder(XmlReader r, ReadState s)
    {
        string? def = null;
        (string Payload, bool Deflate)? data = null;
        (string? Payload, bool Deflate)? extension = null;
        ForEachChild(r, name =>
        {
            switch (name)
            {
                case "def":
                    def = r.ReadElementContentAsString().Trim();
                    break;
                case "records" or "recordsDeflate" when def is null or "Wealth_Total":
                    data = (r.ReadElementContentAsString(), name == "recordsDeflate");
                    break;
                case "records" or "recordsDeflate" when HistoryMetricReader.Defs.Contains(def):
                    extension = (ReadCappedText(r, HistoryMetricReader.MaxBase64Chars), name == "recordsDeflate");
                    break;
                default:
                    r.Skip();
                    break;
            }
        });
        if (def == "Wealth_Total" && data is { } d)
        {
            s.WealthTotalCount++;
            if (s.Wealth is null)
            {
                s.Wealth = RecordsCodec.Decode(d.Payload, d.Deflate);
                s.WealthTotalPayload = d.Payload;
            }
            return;
        }
        if (def is null || def == "Wealth_Total" || !HistoryMetricReader.Defs.Contains(def))
            return;
        if (extension is null && data is { } early)   // records written before def: not seen in real saves
            extension = (CountNonWhitespace(early.Payload) <= HistoryMetricReader.MaxBase64Chars ? early.Payload : null, early.Deflate);
        if (extension is not { } e)
            return;
        float[]? values = null;
        if (e.Payload is { } payload && RecordsCodec.TryDecodeLimited(payload, e.Deflate, HistoryMetricReader.MaxPoints, out var decoded))
            values = decoded;
        if (!s.Series.TryGetValue(def, out var list))
            s.Series[def] = list = new List<float[]?>();
        list.Add(values);
    }

    /// <summary>
    /// The text of the current element, or null when it has more than <paramref name="maxNonWhitespace"/> non-whitespace
    /// characters or child elements. The element is consumed either way, without keeping an oversized text in memory.
    /// </summary>
    private static string? ReadCappedText(XmlReader r, int maxNonWhitespace)
    {
        if (!r.CanReadValueChunk)
        {
            var all = r.ReadElementContentAsString();
            return CountNonWhitespace(all) <= maxNonWhitespace ? all : null;
        }
        if (r.IsEmptyElement)
        {
            r.Read();
            return "";
        }
        var depth = r.Depth;
        r.Read();
        var text = new StringBuilder();
        var buffer = new char[8192];
        long count = 0;
        var usable = true;
        while (!(r.NodeType == XmlNodeType.EndElement && r.Depth == depth))
        {
            if (r.EOF)
                throw new XmlException("unexpected end of file");
            switch (r.NodeType)
            {
                case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace:
                    int n;
                    while ((n = r.ReadValueChunk(buffer, 0, buffer.Length)) > 0)
                    {
                        if (!usable)
                            continue;
                        for (var i = 0; i < n; i++)
                            if (!char.IsWhiteSpace(buffer[i]))
                                count++;
                        if (count > maxNonWhitespace)
                        {
                            usable = false;
                            text.Clear();
                        }
                        else
                        {
                            text.Append(buffer, 0, n);
                        }
                    }
                    r.Read();
                    break;
                case XmlNodeType.Element:
                    usable = false;
                    r.Skip();
                    break;
                default:
                    r.Read();
                    break;
            }
        }
        r.Read();
        return usable ? text.ToString() : null;
    }

    private static long CountNonWhitespace(string text)
    {
        long n = 0;
        foreach (var c in text)
            if (!char.IsWhiteSpace(c))
                n++;
        return n;
    }

    /// <summary>
    /// The numeric observation once the v1 read is complete; null when the v1 read failed, when not every byte of the
    /// file was read, or when anything in the numeric part fails (it never changes the v1 observation).
    /// </summary>
    private static MetricObservation? TryBuildMetrics(ReadState state, HashingStream hashing, long fileSize)
    {
        if (state.Obs.Status != ObservationStatus.Ok)
            return null;
        try
        {
            hashing.ReadToEnd();
            return hashing.BytesRead == fileSize ? BuildMetrics(state, Convert.ToHexStringLower(hashing.Hash())) : null;
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
            var c = CheckpointMetrics.ToObservation(parsed.Payload!, text, s.Obs.Identity, s.Obs.GameVersion, metrics.ActiveDlcIds);
            // Both observations came from the same verified full-file read.
            // Preserve the saved MOD cohort on checkpoint event breakdowns too;
            // otherwise a cross-MOD ranked Def looks spuriously compatible.
            c.ModSetHash = metrics.ModSetHash;
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
        var series = new Dictionary<string, float[]?>(StringComparer.Ordinal);
        foreach (var (def, list) in s.Series)
            series[def] = list.Count == 1 ? list[0] : null;
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
                s.HistoricalStock, s.HistoricalStockOverflowed) is { } homes)
        {
            foreach (var (key, value) in homes)
                m.Metrics[key] = value;
            m.EvidenceByAcquisition ??= new Dictionary<string, MetricEvidence>(StringComparer.Ordinal);
            m.EvidenceByAcquisition["facilities"] = MetricEvidence.Experimental;
            if (homes.ContainsKey(StockSnapshot.SilverLatest) || homes.ContainsKey(StockSnapshot.MedicinePeak))
                m.EvidenceByAcquisition["stock"] = MetricEvidence.Experimental;
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

    /// <summary>
    /// Serves the XML reader from blocks read from the file and hashes each block on a second thread, so hashing adds
    /// little import time. The hash covers exactly the bytes served, in order. Blocks circulate between a free list and a
    /// queue of filled blocks; nothing is allocated per block.
    /// </summary>
    private sealed class HashingStream : Stream
    {
        private const int BlockSize = 1 << 16;
        private const int Blocks = 4;
        private readonly Stream _inner;
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly System.Collections.Concurrent.BlockingCollection<byte[]> _free = new();
        private readonly System.Collections.Concurrent.BlockingCollection<(byte[] Block, int Length)> _filled = new();
        private readonly Task _worker;
        private byte[]? _block;
        private int _pos;
        private int _len;
        private bool _eof;

        public HashingStream(Stream inner)
        {
            _inner = inner;
            for (var i = 0; i < Blocks; i++)
                _free.Add(new byte[BlockSize]);
            _worker = Task.Factory.StartNew(() =>
            {
                foreach (var (block, length) in _filled.GetConsumingEnumerable())
                {
                    _hash.AppendData(block, 0, length);
                    _free.Add(block);
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public long BytesRead { get; private set; }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_pos == _len && !Refill())
                return 0;
            var n = Math.Min(buffer.Length, _len - _pos);
            _block.AsSpan(_pos, n).CopyTo(buffer);
            _pos += n;
            return n;
        }

        private bool Refill()
        {
            if (_eof)
                return false;
            if (_block is not null && _len > 0)
                _filled.Add((_block, _len));
            _block = _free.Take();
            _pos = 0;
            _len = Math.Max(0, _inner.Read(_block, 0, BlockSize));
            BytesRead += _len;
            _eof = _len == 0;
            return !_eof;
        }

        /// <summary>Reads (and hashes) what the XML reader left after the end of the document.</summary>
        public void ReadToEnd()
        {
            _pos = _len;
            while (Refill())
                _pos = _len;
        }

        public byte[] Hash()
        {
            if (_block is not null && _len > 0)
                _filled.Add((_block, _len));
            _block = null;
            _len = 0;
            _filled.CompleteAdding();
            _worker.Wait();
            return _hash.GetHashAndReset();
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!_filled.IsAddingCompleted)
                    _filled.CompleteAdding();
                try
                {
                    _worker.Wait();
                }
                catch (AggregateException)
                {
                    // the worker only hashes; nothing to report after a failed read
                }
                _filled.Dispose();
                _free.Dispose();
                _hash.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static void Finish(ReadState s)
    {
        var obs = s.Obs;
        obs.Display.FactionName = PlayerFactionName(s);
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
