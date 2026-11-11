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

}
