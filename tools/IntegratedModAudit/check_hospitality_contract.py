#!/usr/bin/env python3
"""Pinned source-shape gate for Hospitality (Continued) 1.7 lifetime arrivals.

This is evidence of the audited source shape, not a real-game assertion.
"""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

if len(sys.argv) != 2:
    raise SystemExit("usage: check_hospitality_contract.py PATH_TO_PINNED_HOSPITALITY")
root = Path(sys.argv[1])
metadata = ET.parse(root / "About" / "About.xml").getroot()
assert (metadata.findtext("packageId") or "").strip().lower() == "orion.hospitality"
assert (metadata.findtext("modVersion") or "").strip() == "1.1.8"
assert "1.7" in ET.tostring(metadata.find("supportedVersions"), encoding="unicode")
map_component = (root / "Source/Hospitality_MapComponent.cs").read_text(encoding="utf-8")
guests = (root / "Source/Utilities/GuestUtility.cs").read_text(encoding="utf-8")
lord_job = (root / "Source/LordJob_VisitColony.cs").read_text(encoding="utf-8")
incident = (root / "Source/IncidentWorker_VisitorGroup.cs").read_text(encoding="utf-8")
assert "public void OnLordSpawned(Lord lord)" in map_component
assert "public static void OnLordSpawned(Lord lord)" in guests
assert "lord.Map?.GetMapComponent()?.OnLordSpawned(lord);" in guests
assert "public bool recoveredOnMap;" in lord_job
assert re.search(r"CreateLordCore\([^;]*bool recoveredOnMap\)", incident)
assert "new LordJob_VisitColony(faction, chillSpot, duration, getUpsetWhenLost) { recoveredOnMap = recoveredOnMap }" in incident
assert "GuestUtility.OnLordSpawned(lord);" in incident
assert "public HashSet<Pawn> PresentGuests" in map_component
assert "public List<Lord> PresentLords" in map_component
# Success hook: recruit action must call RecruitingSuccess which adopts the pawn.
assert re.search(r"public\s+static\s+void\s+Recruit\(Pawn guest, int recruitPenalty, bool forced\)", guests)
assert "RecruitingSuccess(guest, recruitPenalty);" in guests
assert re.search(r"private\s+static\s+void\s+RecruitingSuccess\(Pawn guest, int recruitPenalty\)", guests)
assert "guest.Adopt();" in guests

guest_component = (root / "Source/CompGuest.cs").read_text(encoding="utf-8")
bed = (root / "Source/Building_GuestBed.cs").read_text(encoding="utf-8")
assert "public Building_GuestBed bed;" in guest_component
assert "public bool HasBed" in guest_component
assert "Scribe_References.Look(ref bed, \"bed\");" in guest_component
assert "public class Building_GuestBed : Building_Bed" in bed
print("Hospitality 1.7 pinned source contract: arrival callback, recovery marker, guest snapshots PASS")

shopping = (root / "Source/JobDriver_BuyItem.cs").read_text(encoding="utf-8")
assert re.search(r"private\s+void\s+BuyThing\(Toil toil\)", shopping)
assert re.search(r"initAction\s*=\s*\(\)\s*=>\s*BuyThing\(", shopping)
for action in ("Item.SplitOff(", ".apparel.Wear(", ".equipment.AddEquipment(", ".TryAdd(", ".TryDrop("):
    assert action in shopping, action
assert "TradeDeal" not in shopping
print("Hospitality 1.7 pinned guest shopping contract: item transfer and payment PASS")
