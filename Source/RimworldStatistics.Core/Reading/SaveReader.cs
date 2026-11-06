using System.Globalization;
using System.Xml;
using System.Xml.Linq;

namespace RimworldStatistics.Core;

/// <summary>
/// Reads one save (.rws) front to back with <see cref="XmlReader"/>, keeping only what the statistics need
/// (research §3.1) and skipping everything else (world grid, maps, mod data) without interpreting it.
/// Never writes to the file and never throws: any failure becomes an observation with <see cref="ObservationStatus.Error"/>.
/// </summary>
public sealed class SaveReader
{
    public const int Version = 1;
    public const string ComponentClassName = "RimworldStatistics.GameComponent_RimworldStatistics";

    private const int MaxReasonLength = 200;
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

    public SaveObservation Read(string path)
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
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            return Read(fs, fileName, size, lastWrite);
        }
        catch (Exception e)
        {
            return Failed(fileName, size, lastWrite, e);
        }
    }

    public SaveObservation Read(Stream xml, string fileName, long fileSize, DateTime lastWriteUtc)
    {
        try
        {
            var obs = NewObservation(fileName, fileSize, lastWriteUtc);
            using var reader = XmlReader.Create(xml, Settings);
            if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "savegame")
            {
                obs.Status = ObservationStatus.Error;
                obs.ErrorReason = "not a savegame";
                return obs;
            }
            var state = new ReadState(obs);
            ForEachChild(reader, name =>
            {
                switch (name)
                {
                    case "meta": ReadMeta(Load(reader), obs); break;
                    case "game": ReadGame(reader, state); break;
                    default: reader.Skip(); break;
                }
            });
            Finish(state);
            return obs;
        }
        catch (Exception e)
        {
            return Failed(fileName, fileSize, lastWriteUtc, e);
        }
    }

    private sealed class ReadState(SaveObservation obs)
    {
        public SaveObservation Obs { get; } = obs;
        public float[]? Wealth { get; set; }
        public string? ScenarioPlayerFactionDef { get; set; }
        public List<(string? Def, string? Name)> Factions { get; } = new();
        public bool MechanoidsDeactivated { get; set; }
        public bool RoyalAscentSucceeded { get; set; }
        public bool MechhiveSucceeded { get; set; }
        public string? AnomalyLevelDef { get; set; }
        public List<EndingType> LineageTypes { get; } = new();
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
                case "storyWatcher": ReadStats(Load(r).Element("statsRecord"), obs.Values); break;
                case "world": ReadWorld(r, s); break;
                case "history": ReadHistory(r, s); break;
                case "questManager": ReadQuests(r, s); break;
                case "components": ReadComponents(r, s); break;
                default: r.Skip(); break;
            }
        });
    }

    private static void ReadMeta(XElement meta, SaveObservation obs)
    {
        obs.GameVersion = Text(meta.Element("gameVersion"));
        obs.ModCount = meta.Element("modIds")?.Elements("li").Count() ?? 0;
    }

    private static void ReadInfo(XElement info, SaveObservation obs)
    {
        obs.Values.RealPlayTime = Double(info.Element("realPlayTimeInteracting"), 0);
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

    /// <summary>Reads def, name and deactivated directly under a faction; relations, pawns etc. are skipped.</summary>
    private static void ReadFaction(XmlReader r, ReadState s)
    {
        string? def = null, name = null;
        var deactivated = false;
        ForEachChild(r, child =>
        {
            switch (child)
            {
                case "def": def = r.ReadElementContentAsString().Trim(); break;
                case "name": name = r.ReadElementContentAsString(); break;
                case "deactivated": deactivated = bool.Parse(r.ReadElementContentAsString().Trim()); break;
                default: r.Skip(); break;
            }
        });
        s.Factions.Add((def, name));
        if (def == "Mechanoid" && deactivated)
            s.MechanoidsDeactivated = true;
    }

    private static void ReadQuests(XmlReader r, ReadState s)
    {
        ForEachChild(r, name =>
        {
            if (name != "quests") { r.Skip(); return; }
            ForEachChild(r, _ =>                                   // quests/li
            {
                string? root = null, outcome = null;
                var ended = false;
                ForEachChild(r, child =>
                {
                    switch (child)
                    {
                        case "root": root = r.ReadElementContentAsString().Trim(); break;
                        case "ended": ended = bool.Parse(r.ReadElementContentAsString().Trim()); break;
                        case "endOutcome": outcome = r.ReadElementContentAsString().Trim(); break;
                        default: r.Skip(); break;
                    }
                });
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
                case "lineageEndings":
                    ForEachChild(r, _ =>
                    {
                        // An unreadable token is ignored rather than failing the whole save.
                        if (LineageEntry.TryParse(r.ReadElementContentAsString().Trim(), out var entry))
                            s.LineageTypes.Add(entry.Type);
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
                default:
                    r.Skip();
                    break;
            }
        });
        if (def == "Wealth_Total" && data is { } d && s.Wealth is null)
            s.Wealth = RecordsCodec.Decode(d.Payload, d.Deflate);
    }

    private static void Finish(ReadState s)
    {
        var obs = s.Obs;
        obs.Display.FactionName = PlayerFactionName(s);
        obs.EndingTraces = EndingTraces.Detect(new TraceInputs
        {
            ColonistsLaunched = obs.Values.ColonistsLaunched,
            RoyalAscentSucceeded = s.RoyalAscentSucceeded,
            MechhiveSucceeded = s.MechhiveSucceeded,
            MechanoidsDeactivated = s.MechanoidsDeactivated,
            AnomalyLevelDef = s.AnomalyLevelDef,
            LineageTypes = s.LineageTypes,
        });
        if (s.Wealth is { Length: > 0 } w)
        {
            obs.Identity.FirstWealthBits = BitConverter.SingleToUInt32Bits(w[0]);
            obs.Values.WealthMax = w.Max();
            obs.Values.WealthLast = w[^1];
            obs.Values.WealthPoints = w.Length;
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
