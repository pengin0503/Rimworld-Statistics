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

}
