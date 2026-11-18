using System.Globalization;
using System.Xml;
namespace RimworldStatistics.Core;
public sealed partial class SaveReader
{
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
                    ReadWorldEnvironmentInfo(info, s);
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
                case "ideoManager":
                    if (HistoryMetricReader.MajorMinor(s.Obs.GameVersion) == "1.7") ReadIdeoManager(r, s);
                    else r.Skip();
                    break;
                default:
                    r.Skip();
                    break;
            }
        });
    }

    private static void ReadWorldEnvironmentInfo(System.Xml.Linq.XElement info, ReadState s)
    {
        var mapSizeEl = info.Element("initialMapSize");
        if (mapSizeEl is not null && TryParseIntVec3XZ(mapSizeEl.Value, out var w, out var h))
        {
            s.WorldInfoHasMapSize = true;
            s.WorldInitialMapWidth = w;
            s.WorldInitialMapHeight = h;
        }

        // WorldInfo.ExposeData writes overallTemperature (default VeryCold = 0), overallRainfall (default AlmostNone = 0),
        // and pollution (default 0f) with forceSave = false, whereas initialMapSize (default (0,0,0)) is always written
        // in real saves. Apply Scribe omitted defaults only when initialMapSize confirms a complete WorldInfo block.
        var tempEl = info.Element("overallTemperature");
        if (tempEl is not null)
            s.WorldOverallTemperature = tempEl.Value.Trim();
        else if (s.WorldInfoHasMapSize)
            s.WorldOverallTemperature = "VeryCold";

        var rainEl = info.Element("overallRainfall");
        if (rainEl is not null)
            s.WorldOverallRainfall = rainEl.Value.Trim();
        else if (s.WorldInfoHasMapSize)
            s.WorldOverallRainfall = "AlmostNone";

        var polEl = info.Element("pollution");
        if (polEl is not null)
        {
            if (double.TryParse(polEl.Value.Trim(), NumberStyles.Float, Inv, out var pol) && double.IsFinite(pol))
                s.WorldPollution = pol;
            else
                s.WorldPollutionMalformed = true;
        }
        else if (s.WorldInfoHasMapSize)
        {
            s.WorldPollution = 0.0;
        }
    }

    private static bool TryParseIntVec3XZ(string raw, out int width, out int height)
    {
        width = 0;
        height = 0;
        var span = raw.AsSpan().Trim();
        if (span.Length < 7 || span[0] != '(' || span[^1] != ')')
            return false;
        span = span[1..^1];
        var c1 = span.IndexOf(',');
        if (c1 <= 0)
            return false;
        var rest = span[(c1 + 1)..];
        var c2 = rest.IndexOf(',');
        if (c2 <= 0)
            return false;
        var xSpan = span[..c1].Trim();
        var ySpan = rest[..c2].Trim();
        var zSpan = rest[(c2 + 1)..].Trim();
        if (zSpan.IndexOf(',') >= 0)
            return false;
        return int.TryParse(xSpan, NumberStyles.Integer, Inv, out width)
            && int.TryParse(ySpan, NumberStyles.Integer, Inv, out _)
            && int.TryParse(zSpan, NumberStyles.Integer, Inv, out height)
            && width is > 0 and <= 10000
            && height is > 0 and <= 10000;
    }

    private static void ReadIdeoManager(XmlReader r, ReadState s)
    {
        ForEachChild(r, child =>
        {
            if (child != "ideos") { r.Skip(); return; }
            if (r.AttributeCount > 0 && r.GetAttribute("IsNull") == "True")
            {
                s.SavedIdeosMalformed = true;
                r.Skip();
                return;
            }
            s.SavedIdeos ??= new List<SavedIdeo>();
            ForEachChild(r, item =>
            {
                if (item != "li") { s.SavedIdeosMalformed = true; r.Skip(); return; }
                if (s.SavedIdeos.Count >= HistoricalIdeologyStateReader.MaxSavedIdeos)
                {
                    s.SavedIdeosMalformed = true;
                    r.Skip();
                    return;
                }
                int? id = null;
                var fluid = false;
                var reformCount = 0;
                var valid = true;
                ForEachChild(r, field =>
                {
                    switch (field)
                    {
                        case "id":
                            var idText = ReadHistoricalAtom(r, s);
                            if (id is not null || !int.TryParse(idText, NumberStyles.Integer, Inv, out var parsedId) || parsedId < 0)
                                valid = false;
                            else
                                id = parsedId;
                            break;
                        case "fluid":
                            var fluidText = ReadHistoricalAtom(r, s);
                            if (!bool.TryParse(fluidText, out var parsedFluid))
                                valid = false;
                            else
                                fluid = parsedFluid;
                            break;
                        case "development":
                            if (r.AttributeCount > 0 && r.GetAttribute("IsNull") == "True")
                            {
                                r.Skip();
                                break;
                            }
                            ForEachChild(r, devField =>
                            {
                                if (devField == "reformCount")
                                {
                                    var refText = ReadHistoricalAtom(r, s);
                                    if (!int.TryParse(refText, NumberStyles.Integer, Inv, out var parsedRef) || parsedRef < 0)
                                        valid = false;
                                    else
                                        reformCount = parsedRef;
                                }
                                else
                                {
                                    r.Skip();
                                }
                            });
                            break;
                        default:
                            r.Skip();
                            break;
                    }
                });
                s.SavedIdeos.Add(new SavedIdeo(id, fluid, reformCount, valid));
            });
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
                            var text = ReadHistoricalAtom(r, s);
                            if (id is not null || !int.TryParse(text, NumberStyles.Integer, Inv, out var parsed)
                                || parsed < 0)
                                valid = false;
                            else id = parsed;
                            break;
                        case "def":
                            var d = ReadHistoricalAtom(r, s);
                            if (def is not null || string.IsNullOrWhiteSpace(d)) valid = false;
                            else def = d;
                            break;
                        case "faction":
                            var f = ReadHistoricalAtom(r, s);
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

    /// <summary>Keep the v1 faction identity reader, and selectively gather small
    /// first-party relation facts for historical, explicitly partial snapshots.</summary>
    private static void ReadFaction(XmlReader r, ReadState s)
    {
        string? def = null, name = null, primaryIdeo = null;
        List<string>? minorIdeos = null;
        int? loadId = null;
        bool deactivated = false, defeated = false, hidden = false, temporary = false, valid = true, ideoMalformed = false;
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
                case "ideos":
                    var mayBePlayer = def is "PlayerColony" or "PlayerTribe"
                        || (s.ScenarioPlayerFactionDef is { Length: > 0 } scenDef && def == scenDef)
                        || def is null;
                    if (s.HistoricalFactionsOverflowed || !mayBePlayer || HistoryMetricReader.MajorMinor(s.Obs.GameVersion) != "1.7")
                    {
                        r.Skip();
                        break;
                    }
                    if (r.AttributeCount > 0 && r.GetAttribute("IsNull") == "True")
                    {
                        ideoMalformed = true;
                        r.Skip();
                        break;
                    }
                    ForEachChild(r, ideoField =>
                    {
                        switch (ideoField)
                        {
                            case "primaryIdeo":
                                var pRef = ReadHistoricalAtom(r, s);
                                if (primaryIdeo is not null || string.IsNullOrWhiteSpace(pRef))
                                    ideoMalformed = true;
                                else
                                    primaryIdeo = pRef;
                                break;
                            case "ideosMinor":
                                if (r.AttributeCount > 0 && r.GetAttribute("IsNull") == "True")
                                {
                                    r.Skip();
                                    break;
                                }
                                minorIdeos ??= new List<string>();
                                ForEachChild(r, item =>
                                {
                                    if (item != "li" || minorIdeos.Count >= HistoricalIdeologyStateReader.MaxMinorIdeosPerFaction)
                                    {
                                        ideoMalformed = true;
                                        r.Skip();
                                        return;
                                    }
                                    var mRef = ReadHistoricalAtom(r, s);
                                    if (string.IsNullOrWhiteSpace(mRef))
                                        ideoMalformed = true;
                                    else
                                        minorIdeos.Add(mRef);
                                });
                                break;
                            default:
                                r.Skip();
                                break;
                        }
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
                !deactivated && !defeated && !hidden && !temporary, relations, valid,
                primaryIdeo, minorIdeos, ideoMalformed));
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

}
