using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace RimworldStatistics.Core.Tests.Fixtures;

/// <summary>
/// Builds synthetic save XML in the shape RimWorld writes it (checked against real 1.5/1.6 saves):
/// savegame/{meta, game/{info, scenario, tickManager, storyWatcher, storyteller, history, questManager, components, world, maps}}.
/// Like Scribe_Values, an element is only written when it was set, so "absent" means "game default".
/// </summary>
public sealed class SaveXmlBuilder
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private (string Version, int Mods)? _meta;
    private (float? RealPlay, string? Tile, string? Pawns, bool Permadeath)? _info;
    private (int? TicksGame, int? Start)? _tick;
    private (string? Seed, int? Prv, string? Name)? _world;
    private string? _scenario;
    private string? _scenarioFactionDef;
    private (string Def, string? Difficulty)? _storyteller;
    private (int? Raids, int? Threats, int? Killed, int? Launched, int? GreatestPop)? _stats;
    private (string Element, string Payload)? _wealth;
    private int _mapsBytes;
    private int _unknownBytes;
    private readonly List<(string Root, bool Ended, string? Outcome)> _quests = new();
    private readonly List<(string Def, string? Name, bool Deactivated)> _factions = new();
    private readonly List<(string Class, List<XElement> Children)> _components = new();

    public SaveXmlBuilder Meta(string version, int mods = 0) { _meta = (version, mods); return this; }

    public SaveXmlBuilder Info(float? realPlay = null, string? tile = null, string? pawns = null, bool permadeath = false)
    { _info = (realPlay, tile, pawns, permadeath); return this; }

    public SaveXmlBuilder Tick(int? ticksGame = null, int? start = null) { _tick = (ticksGame, start); return this; }

    public SaveXmlBuilder World(string? seed = null, int? prv = null, string? name = null) { _world = (seed, prv, name); return this; }

    public SaveXmlBuilder Scenario(string name, string? playerFactionDef = null)
    { _scenario = name; _scenarioFactionDef = playerFactionDef; return this; }

    public SaveXmlBuilder Storyteller(string def, string? difficulty = null) { _storyteller = (def, difficulty); return this; }

    public SaveXmlBuilder Stats(int? raids = null, int? threats = null, int? killed = null, int? launched = null, int? greatestPop = null)
    { _stats = (raids, threats, killed, launched, greatestPop); return this; }

    /// <summary>Wealth_Total as plain Base64 of little-endian float32 ("records").</summary>
    public SaveXmlBuilder WealthTotal(params float[] values) { _wealth = ("records", Convert.ToBase64String(FloatBytes(values))); return this; }

    /// <summary>Wealth_Total as Base64 of raw-deflated float32 ("recordsDeflate").</summary>
    public SaveXmlBuilder WealthTotalDeflate(params float[] values) { _wealth = ("recordsDeflate", Convert.ToBase64String(Deflate(FloatBytes(values)))); return this; }

    /// <summary>Wealth_Total with an arbitrary (possibly broken) payload.</summary>
    public SaveXmlBuilder WealthTotalRaw(string element, string payload) { _wealth = (element, payload); return this; }

    /// <summary>Adds a world/maps section of roughly this many bytes.</summary>
    public SaveXmlBuilder Maps(int sizeBytes) { _mapsBytes = sizeBytes; return this; }

    /// <summary>Adds deeply nested unknown (mod-like) data of roughly this many bytes, in game and in world.</summary>
    public SaveXmlBuilder Unknown(int sizeBytes) { _unknownBytes = sizeBytes; return this; }

    public SaveXmlBuilder Quest(string root, bool ended, string? outcome)
    { _quests.Add((root, ended, outcome)); return this; }

    public SaveXmlBuilder Faction(string def, string? name, bool deactivated)
    { _factions.Add((def, name, deactivated)); return this; }

    /// <summary>Adds a GameComponent li[@Class] with one child element (or none when child is null).</summary>
    public SaveXmlBuilder Component(string cls, string? child = null, string? value = null)
    {
        var children = new List<XElement>();
        if (child != null)
            children.Add(new XElement(child, value));
        _components.Add((cls, children));
        return this;
    }

    /// <summary>Adds a GameComponent li[@Class] with arbitrary children.</summary>
    public SaveXmlBuilder Component(string cls, params XElement[] children)
    {
        _components.Add((cls, children.ToList()));
        return this;
    }

    /// <summary>The smallest identifiable save.</summary>
    public SaveXmlBuilder Minimal()
        => Meta("1.7.4985 rev1269", 0)
            .Info(realPlay: 10f, tile: "100,0", pawns: "Thing_Human1")
            .Tick(ticksGame: 60000, start: 0)
            .World(seed: "minimal", prv: 1)
            .WealthTotal(1000f);

    public byte[] Build()
    {
        var game = new XElement("game", new XElement("currentMapIndex", 0));

        if (_info is { } info)
        {
            var e = new XElement("info");
            if (info.RealPlay is { } rp) e.Add(new XElement("realPlayTimeInteracting", rp.ToString(Inv)));
            if (info.Permadeath) e.Add(new XElement("permadeathMode", "True"));
            if (info.Tile != null) e.Add(new XElement("startingTile", info.Tile));
            if (info.Pawns != null)
                e.Add(new XElement("startingAndOptionalPawns", info.Pawns.Split(',').Select(p => new XElement("li", p))));
            game.Add(e);
        }
        game.Add(new XElement("rules", new XElement("disallowedDesignatorTypes")));
        if (_scenario != null)
        {
            var sc = new XElement("scenario", new XElement("name", _scenario), new XElement("summary", "…"));
            if (_scenarioFactionDef != null)
                sc.Add(new XElement("playerFaction", new XElement("def", "PlayerFaction"), new XElement("factionDef", _scenarioFactionDef)));
            sc.Add(new XElement("parts", new XElement("li", new XAttribute("Class", "ScenPart_PlayerPawnsArriveMethod"), new XElement("def", "PlayerPawnsArriveMethod"))));
            game.Add(sc);
        }
        if (_tick is { } tick)
        {
            var e = new XElement("tickManager");
            if (tick.TicksGame is { } tg) e.Add(new XElement("ticksGame", tg));
            if (tick.Start is { } st) e.Add(new XElement("gameStartAbsTick", st));
            e.Add(new XElement("startingYear", 5500));
            game.Add(e);
        }
        var sw = new XElement("storyWatcher");
        if (_stats is { } s)
        {
            var sr = new XElement("statsRecord");
            if (s.Raids is { } a) sr.Add(new XElement("numRaidsEnemy", a));
            if (s.Threats is { } b) sr.Add(new XElement("numThreatsQueued", b));
            if (s.Killed is { } c) sr.Add(new XElement("colonistsKilled", c));
            if (s.Launched is { } d) sr.Add(new XElement("colonistsLaunched", d));
            if (s.GreatestPop is { } g) sr.Add(new XElement("greatestPopulation", g));
            sw.Add(sr);
        }
        sw.Add(new XElement("watcherAdaptation", new XElement("adaptDays", "1")));
        game.Add(sw);
        if (_storyteller is { } stl)
        {
            var e = new XElement("storyteller", new XElement("def", stl.Def));
            if (stl.Difficulty != null) e.Add(new XElement("difficulty", stl.Difficulty));
            e.Add(new XElement("incidentQueue", new XElement("queuedIncidents")));
            game.Add(e);
        }

        var history = new XElement("history",
            new XElement("archive", new XElement("archivables",
                new XElement("li", new XAttribute("Class", "StandardLetter"), new XElement("def", "NegativeEvent"), new XElement("text", "崩落")))));
        var groups = new XElement("autoRecorderGroups");
        var wealthRecorders = new XElement("recorders",
            new XElement("li", new XElement("def", "Wealth_Items"), new XElement("records", Convert.ToBase64String(FloatBytes(123456f, 999999f)))));
        if (_wealth is { } w)
            wealthRecorders.AddFirst(new XElement("li", new XElement("def", "Wealth_Total"), new XElement(w.Element, w.Payload)));
        groups.Add(new XElement("li", new XElement("def", "Wealth"), wealthRecorders));
        groups.Add(new XElement("li", new XElement("def", "Population"), new XElement("recorders",
            new XElement("li", new XElement("def", "FreeColonists"), new XElement("records", Convert.ToBase64String(FloatBytes(3f, 4f)))))));
        history.Add(groups);
        game.Add(history);

        if (_unknownBytes > 0)
            game.Add(new XElement("someModManager", DeepNest(_unknownBytes / 2)));

        if (_quests.Count > 0)
        {
            var qs = new XElement("quests");
            foreach (var q in _quests)
            {
                var li = new XElement("li", new XElement("name", "クエスト"), new XElement("appearanceTick", 1), new XElement("root", q.Root));
                if (q.Ended) li.Add(new XElement("ended", "True"));
                if (q.Outcome != null) li.Add(new XElement("endOutcome", q.Outcome));
                li.Add(new XElement("parts", new XElement("li", new XAttribute("Class", "QuestPart_Delay"), new XElement("inSignalEnable", "x"))));
                qs.Add(li);
            }
            game.Add(new XElement("questManager", qs));
        }

        var comps = new XElement("components", new XElement("li", new XAttribute("Class", "GameComponent_DebugTools")));
        foreach (var c in _components)
            comps.Add(new XElement("li", new XAttribute("Class", c.Class), c.Children));
        game.Add(comps);

        var world = new XElement("world");
        if (_world is { } wd)
        {
            var wi = new XElement("info");
            if (wd.Name != null) wi.Add(new XElement("name", wd.Name));
            wi.Add(new XElement("planetCoverage", "0.3"));
            if (wd.Seed != null) wi.Add(new XElement("seedString", wd.Seed));
            if (wd.Prv is { } prv) wi.Add(new XElement("persistentRandomValue", prv));
            world.Add(wi);
        }
        if (_unknownBytes > 0)
            world.Add(new XElement("grid", DeepNest(_unknownBytes / 2)));
        if (_factions.Count > 0)
        {
            var all = new XElement("allFactions");
            foreach (var f in _factions)
            {
                var li = new XElement("li", new XElement("leader", "Thing_Human999"), new XElement("def", f.Def));
                if (f.Name != null) li.Add(new XElement("name", f.Name));
                if (f.Deactivated) li.Add(new XElement("deactivated", "True"));
                li.Add(new XElement("relations", new XElement("li", new XElement("other", "Faction_1"), new XElement("goodwill", 0))));
                all.Add(li);
            }
            world.Add(new XElement("factionManager", all));
        }
        game.Add(world);

        if (_mapsBytes > 0)
        {
            var things = new XElement("things");
            const string Row = "Steel";
            var n = Math.Max(1, _mapsBytes / 120);
            for (var i = 0; i < n; i++)
                things.Add(new XElement("thing", new XAttribute("Class", "ThingWithComps"), new XElement("def", Row), new XElement("id", "Steel" + i), new XElement("pos", "(1, 0, 1)")));
            game.Add(new XElement("maps", new XElement("li", new XElement("uniqueID", 0), things)));
        }

        var root = new XElement("savegame");
        if (_meta is { } m)
        {
            root.Add(new XElement("meta",
                new XElement("gameVersion", m.Version),
                new XElement("modIds", Enumerable.Range(0, m.Mods).Select(i => new XElement("li", "mod.id" + i))),
                new XElement("modSteamIds", Enumerable.Range(0, m.Mods).Select(i => new XElement("li", 0))),
                new XElement("modNames", Enumerable.Range(0, m.Mods).Select(i => new XElement("li", "Mod " + i)))));
        }
        root.Add(game);

        using var ms = new MemoryStream();
        using (var xw = XmlWriter.Create(ms, new XmlWriterSettings { Indent = true, IndentChars = "\t", Encoding = new UTF8Encoding(false) }))
            new XDocument(new XDeclaration("1.0", "utf-8", null), root).Save(xw);
        return ms.ToArray();
    }

    private static XElement DeepNest(int bytes)
    {
        // A chain 40 levels deep whose innermost element holds many leaves: models mod data with deep nesting.
        var leafCount = Math.Max(1, bytes / 40);
        var inner = new XElement("li", Enumerable.Range(0, leafCount).Select(i => new XElement("v", i)));
        for (var d = 0; d < 40; d++)
            inner = new XElement(d % 2 == 0 ? "li" : "data", new XAttribute("Class", "SomeMod.Thing"), inner);
        return inner;
    }

    public static byte[] FloatBytes(params float[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 4), values[i]);
        return bytes;
    }

    public static byte[] Deflate(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            ds.Write(data);
        return ms.ToArray();
    }
}
