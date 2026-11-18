using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
namespace RimworldStatistics.Core;
public sealed partial class SaveReader
{
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
                        case "ended":
                            // Scribe writes True/False. A malformed value loses this quest's metrics only, and ended stays
                            // false so the row cannot become an ending trace (#324).
                            if (!bool.TryParse(r.ReadElementContentAsString().Trim(), out ended))
                            {
                                ended = false;
                                readable = false;
                            }
                            break;
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
                // Quest.State == EndedSuccess requires ended && endOutcome == Success (1.7.4985). The trace uses only those
                // two fields and root, like the game, which keeps a quest whose unrelated tick or id field is malformed.
                if (!ended || outcome != "Success")
                    return;
                if (root == "EndGame_RoyalAscent") s.RoyalAscentSucceeded = true;
                else if (root == "Gravcore_Mechhive") s.MechhiveSucceeded = true;
            });
        });
    }

    private static void ReadEntityCodex(XmlReader r, ReadState s)
    {
        if (s.EntityCodexPresent)
        {
            s.EntityCodexMalformed = true;
            r.Skip();
            return;
        }
        s.EntityCodexPresent = true;
        if (r.GetAttribute("IsNull") == "True")
        {
            r.Skip();
            return;
        }
        var discoveredSeen = false;
        ForEachChild(r, child =>
        {
            if (child != "discoveredEntities") { r.Skip(); return; }
            if (discoveredSeen)
            {
                s.EntityCodexMalformed = true;
                r.Skip();
                return;
            }
            discoveredSeen = true;
            if (r.GetAttribute("IsNull") == "True") { r.Skip(); return; }
            s.SavedDiscoveredEntities ??= new HashSet<string>(StringComparer.Ordinal);
            ForEachChild(r, item =>
            {
                if (item != "li")
                {
                    s.EntityCodexMalformed = true;
                    r.Skip();
                    return;
                }
                var defName = ReadHistoricalAtom(r, s);
                if (string.IsNullOrWhiteSpace(defName) || defName == "null"
                    || s.SavedDiscoveredEntities.Count >= HistoricalAnomalyStateReader.MaxDiscoveredEntities)
                {
                    s.EntityCodexMalformed = true;
                    return;
                }
                s.SavedDiscoveredEntities.Add(defName);
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
            {
                s.AnomalyComponentCount++;
                var highestSeen = false;
                var monolithSeen = false;
                ForEachChild(r, child =>
                {
                    switch (child)
                    {
                        case "levelDef":
                            s.AnomalyLevelDef = r.ReadElementContentAsString().Trim();
                            break;
                        case "highestLevelReached":
                            var lvlText = ReadHistoricalAtom(r, s);
                            if (highestSeen || !int.TryParse(lvlText, NumberStyles.Integer, Inv, out var lvl) || lvl < 0)
                                s.AnomalyComponentMalformed = true;
                            else
                                s.AnomalyHighestLevel = lvl;
                            highestSeen = true;
                            break;
                        case "monolith":
                            var monoText = ReadHistoricalAtom(r, s);
                            if (monolithSeen || string.IsNullOrWhiteSpace(monoText))
                                s.AnomalyComponentMalformed = true;
                            else
                                s.AnomalyMonolithRef = monoText;
                            monolithSeen = true;
                            break;
                        default:
                            r.Skip();
                            break;
                    }
                });
            }
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

    private static void ReadHistory(XmlReader r, ReadState s, string? chartRecorder = null)
    {
        ForEachChild(r, name =>
        {
            if (name != "autoRecorderGroups") { r.Skip(); return; }
            ForEachChild(r, _ =>                                   // autoRecorderGroups/li
                ForEachChild(r, groupChild =>
                {
                    if (groupChild != "recorders") { r.Skip(); return; }
                    ForEachChild(r, _ => ReadRecorder(r, s, chartRecorder));      // recorders/li
                }));
        });
    }

    private static void ReadRecorder(XmlReader r, ReadState s, string? chartRecorder)
    {
        string? def = null;
        (string? Payload, bool Deflate)? data = null;
        (string? Payload, bool Deflate)? extension = null;
        FileStream? overflow = null;
        try
        {
            ForEachChild(r, name =>
            {
                switch (name)
                {
                    case "def":
                        def = r.ReadElementContentAsString().Trim();
                        break;
                    case "records" or "recordsDeflate" when chartRecorder is not null && (def is null || def == chartRecorder):
                        data = (ReadRecorderText(r, s, preserveOverflow: false).Text, name == "recordsDeflate");
                        break;
                    case "records" or "recordsDeflate" when chartRecorder is null && (def is null or "Wealth_Total"):
                        overflow?.Dispose();
                        overflow = null;
                        if (def is null)
                        {
                            var early = ReadRecorderText(r, s, preserveOverflow: true);
                            overflow = early.Overflow;
                            data = (early.Text, name == "recordsDeflate");
                        }
                        else
                        {
                            data = (r.ReadElementContentAsString(), name == "recordsDeflate");
                        }
                        break;
                    case "records" or "recordsDeflate" when chartRecorder is null && def is not null && HistoryMetricReader.Defs.Contains(def):
                        extension = (ReadRecorderText(r, s, preserveOverflow: false).Text, name == "recordsDeflate");
                        break;
                    default:
                        r.Skip();
                        break;
                }
            });
            if (def == "Wealth_Total" && data is { } d)
            {
                s.WealthTotalCount++;
                if (chartRecorder is not null)
                {
                    if (s.WealthTotalCount == 1 && d.Payload is { } text
                        && RecordsCodec.TryDecodeLimited(text, d.Deflate, HistoryMetricReader.MaxPoints, out var wealth))
                    {
                        s.Wealth = wealth;
                        s.WealthTotalWithinTextLimit = true;
                    }
                    return;
                }
                if (s.Wealth is null)
                {
                    try
                    {
                        var payload = d.Payload;
                        if (overflow is not null)
                        {
                            overflow.Position = 0;
                            using var reader = new StreamReader(overflow, Encoding.UTF8, leaveOpen: true);
                            payload = reader.ReadToEnd();
                        }
                        s.Wealth = RecordsCodec.Decode(payload!, d.Deflate);
                        s.WealthTotalWithinTextLimit = overflow is null
                            && CountNonWhitespace(payload!) <= HistoryMetricReader.MaxBase64Chars;
                    }
                    catch (Exception error) when (error is FormatException or InvalidDataException)
                    {
                        s.WealthReadError = error;
                    }
                }
                return;
            }
        }
        finally
        {
            overflow?.Dispose();
        }
        if ((chartRecorder is not null && def != chartRecorder) || def is null || def == "Wealth_Total" || !HistoryMetricReader.Defs.Contains(def))
            return;
        if ((extension ?? data) is not { } e)
            return;
        if (!s.Series.TryAdd(def, null))
        {
            // An ambiguous definition can never contribute values. Release
            // the first decoded series and avoid decoding subsequent copies.
            s.Series[def] = null;
            return;
        }
        if (e.Payload is { } extensionPayload && RecordsCodec.TryDecodeLimited(extensionPayload, e.Deflate, HistoryMetricReader.MaxPoints, out var decoded))
            s.Series[def] = decoded;
    }

    /// <summary>
    /// Reads non-whitespace payload text within the numeric history limit. If the definition is
    /// still unknown, spill oversized text to disk in case it belongs to legacy Wealth_Total.
    /// The caller owns any returned overflow stream.
    /// </summary>
    private static (string? Text, FileStream? Overflow) ReadRecorderText(
        XmlReader r, ReadState state, bool preserveOverflow)
    {
        if (r.IsEmptyElement)
        {
            r.Read();
            return ("", null);
        }
        var depth = r.Depth;
        r.Read();
        var text = new StringBuilder();
        var buffer = state.HistoryTextChunk ??= new char[8192];
        FileStream? overflow = null;
        StreamWriter? writer = null;
        var usable = true;
        try
        {
            try
            {
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
                                var count = 0;
                                for (var i = 0; i < n; i++)
                                    if (!char.IsWhiteSpace(buffer[i]))
                                        buffer[count++] = buffer[i];

                                if (writer is null && count > HistoryMetricReader.MaxBase64Chars - text.Length)
                                {
                                    if (!preserveOverflow)
                                    {
                                        usable = false;
                                        text.Clear();
                                        continue;
                                    }

                                    overflow = new FileStream(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()),
                                        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096,
                                        FileOptions.DeleteOnClose | FileOptions.SequentialScan);
                                    writer = new StreamWriter(overflow, new UTF8Encoding(false), leaveOpen: true);
                                    foreach (var chunk in text.GetChunks())
                                        writer.Write(chunk.Span);
                                    text = new StringBuilder();
                                }

                                if (writer is null)
                                    text.Append(buffer, 0, count);
                                else
                                    writer.Write(buffer, 0, count);
                            }
                            r.Read();
                            break;
                        case XmlNodeType.Element:
                            if (preserveOverflow)
                                throw new XmlException("unexpected child element in recorder text");
                            usable = false;
                            r.Skip();
                            break;
                        default:
                            r.Read();
                            break;
                    }
                }
                r.Read();
            }
            finally
            {
                writer?.Dispose();
            }
            return (usable && overflow is null ? text.ToString() : null, overflow);
        }
        catch
        {
            overflow?.Dispose();
            throw;
        }
    }

    private static long CountNonWhitespace(string text)
    {
        long n = 0;
        foreach (var c in text)
            if (!char.IsWhiteSpace(c))
                n++;
        return n;
    }

}
