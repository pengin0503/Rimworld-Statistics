using System.Globalization;
using System.Xml;
using System.Xml.Linq;
namespace RimworldStatistics.Core;
public sealed partial class SaveReader
{
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
            var onMapPawns = new List<SavedMapPawn>();
            ForEachChild(r, child =>
            {
                switch (child)
                {
                    case "uniqueID":
                        var text = ReadHistoricalAtom(r, s);
                        if (id is not null || !int.TryParse(text, NumberStyles.Integer, Inv, out var parsedId)
                            || parsedId < 0)
                            valid = false;
                        else
                            id = parsedId;
                        break;
                    case "things":
                        ReadSavedMapThings(r, s, onMapTurrets, onMapStock, onMapPawns);
                        break;
                    case "mapInfo":
                        if (mapInfoSeen) valid = false;
                        mapInfoSeen = true;
                        ForEachChild(r, field =>
                        {
                            switch (field)
                            {
                                case "parent":
                                    var value = ReadHistoricalAtom(r, s);
                                    if (parent is not null || string.IsNullOrWhiteSpace(value)) valid = false;
                                    else parent = value;
                                    break;
                                case "isPocketMap":
                                    var flag = ReadHistoricalAtom(r, s);
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
            foreach (var savedPawn in onMapPawns)
                s.HistoricalPawns.Add(savedPawn with { MapId = id, Valid = valid && savedPawn.Valid });
        });
    }

    /// <summary>
    /// Streaming, bounded extraction of specific vanilla building and stock
    /// classes proven by the 1.7.4985 game Def/type index. This never loads
    /// the full maps/things into XElement trees or interprets unknown MOD defs.
    /// </summary>
    private static void ReadSavedMapThings(XmlReader r, ReadState s,
        List<(string? Id, string? Faction, bool Valid)> turrets,
        List<(string? Id, string? Def, int Count, string? Faction, bool Forbidden, bool Valid)> stock,
        List<SavedMapPawn> pawns)
    {
        ForEachChild(r, node =>
        {
            if (node != "thing") { r.Skip(); return; }
            var className = r.GetAttribute("Class");
            if (className == "Pawn")
            {
                if (s.HistoricalPawns.Count + pawns.Count >= HistoricalHomeMapReader.MaxSavedPawns)
                {
                    s.HistoricalPawnsOverflowed = true;
                    r.Skip();
                }
                else
                    pawns.Add(ReadSavedPawn(r, s));
                return;
            }
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
                        var idValue = ReadHistoricalAtom(r, s);
                        if (idSeen || string.IsNullOrWhiteSpace(idValue)) valid = false;
                        idSeen = true;
                        id = idValue;
                        break;
                    case "def":
                        var defValue = ReadHistoricalAtom(r, s);
                        if (defSeen || string.IsNullOrWhiteSpace(defValue)) valid = false;
                        defSeen = true;
                        def = defValue;
                        break;
                    case "faction":
                        var factionValue = ReadHistoricalAtom(r, s);
                        if (factionSeen || string.IsNullOrWhiteSpace(factionValue)) valid = false;
                        factionSeen = true;
                        faction = factionValue is "null" ? null : factionValue;
                        break;
                    case "stackCount":
                        var countValue = ReadHistoricalAtom(r, s);
                        if (countSeen || !int.TryParse(countValue, NumberStyles.Integer, Inv, out var number))
                            valid = false;
                        else count = number;
                        countSeen = true;
                        break;
                    case "forbidden":
                        var forbiddenValue = ReadHistoricalAtom(r, s);
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

    /// <summary>
    /// Read only top-level saved Pawn identity/guest/health state from one map.
    /// The 1.7 game Pawn_GuestTracker uses guestStatusInt, not joinStatus,
    /// for IsPrisoner/IsSlave. All other pawn data are streamed over, not held.
    /// Missing guestStatus represents the game's default Guest state only
    /// when guest tracker and hostFaction fields are explicitly present.
    /// </summary>
    private static SavedMapPawn ReadSavedPawn(XmlReader r, ReadState s)
    {
        string? id = null, race = null, kind = null, faction = null, host = null, status = null;
        var valid = true;
        var idSeen = false;
        var raceSeen = false;
        var kindSeen = false;
        var factionSeen = false;
        var guestSeen = false;
        var hostSeen = false;
        var statusSeen = false;
        var healthSeen = false;
        var dead = false;
        ForEachChild(r, field =>
        {
            switch (field)
            {
                case "id":
                    var idText = ReadHistoricalAtom(r, s);
                    if (idSeen || string.IsNullOrWhiteSpace(idText)) valid = false;
                    idSeen = true;
                    id = idText;
                    break;
                case "def":
                    var raceText = ReadHistoricalAtom(r, s);
                    if (raceSeen || string.IsNullOrWhiteSpace(raceText)) valid = false;
                    raceSeen = true;
                    race = raceText;
                    break;
                case "kindDef":
                    var kindText = ReadHistoricalAtom(r, s);
                    if (kindSeen || string.IsNullOrWhiteSpace(kindText)) valid = false;
                    kindSeen = true;
                    kind = kindText;
                    break;
                case "faction":
                    var factionText = ReadHistoricalAtom(r, s);
                    if (factionSeen || string.IsNullOrWhiteSpace(factionText)) valid = false;
                    factionSeen = true;
                    faction = factionText is "null" ? null : factionText;
                    break;
                case "guest":
                    if (guestSeen) { valid = false; r.Skip(); break; }
                    guestSeen = true;
                    ForEachChild(r, guestField =>
                    {
                        switch (guestField)
                        {
                            case "hostFaction":
                                var hostText = ReadHistoricalAtom(r, s);
                                if (hostSeen || hostText is null || string.IsNullOrWhiteSpace(hostText))
                                    valid = false;
                                hostSeen = true;
                                host = hostText is "null" ? null : hostText;
                                break;
                            case "guestStatus":
                                var statusText = ReadHistoricalAtom(r, s);
                                if (statusSeen || statusText is not ("Guest" or "Prisoner" or "Slave"))
                                    valid = false;
                                statusSeen = true;
                                status = statusText;
                                break;
                            default: r.Skip(); break;
                        }
                    });
                    break;
                case "healthTracker":
                    if (healthSeen) { valid = false; r.Skip(); break; }
                    healthSeen = true;
                    ForEachChild(r, healthField =>
                    {
                        if (healthField != "dead") { r.Skip(); return; }
                        var deadText = ReadHistoricalAtom(r, s);
                        if (!bool.TryParse(deadText, out var explicitDead)) valid = false;
                        else dead |= explicitDead;
                    });
                    break;
                default: r.Skip(); break;
            }
        });
        return new SavedMapPawn(null, id, race, kind, faction, host, status,
            guestSeen, hostSeen, healthSeen, dead, valid && idSeen && raceSeen);
    }

    /// <summary>Fixed-memory small XML scalar reader, including overlong whitespace.</summary>
    private static string? ReadHistoricalAtom(XmlReader r, ReadState s)
    {
        if (r.IsEmptyElement) { r.Read(); return ""; }
        var depth = r.Depth;
        r.Read();
        var value = s.HistoricalAtomValue ??= new char[128];
        var buffer = s.HistoricalAtomChunk ??= new char[64];
        var length = 0;
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
                    if (length + count > value.Length)
                        valid = false;
                    else
                    {
                        buffer.AsSpan(0, count).CopyTo(value.AsSpan(length));
                        length += count;
                    }
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
        return valid ? value.AsSpan(0, length).Trim().ToString() : null;
    }

}
