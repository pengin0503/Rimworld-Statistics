#!/usr/bin/env python3
"""Print selected 1.7 reference signatures for DLC adapter design; never launches RimWorld."""
import gzip
import json
import sys

if len(sys.argv) != 2:
    raise SystemExit("usage: inspect_reference_api.py <ref-index/chunk-0000.json.gz>")
with gzip.open(sys.argv[1], "rt", encoding="utf-8") as f:
    entries = json.load(f)["entries"]

names = (
    "PregnancyUtility", "Precept_Role", "LordJob_Ritual", "CompGestator",
    "CompAtomizer", "Gravship", "GravshipUtility", "CompPilotConsole",
    "Building_GravEngine", "PollutionGrid", "Pawn_AgeTracker",
    "CompMech", "MechGest", "MechRepair", "RitualOutcome",
    "WorldGrid", "PlanetLayer", "Tile", "GameInfo", "GameInitData", "Find",
    "PollutionGrid", "LordJob_Ritual", "LordJob_Ritual_ChildBirth",
    "Building_MechGestator", "CompMechGestatorTank", "CompMechRepairable",
    "MechRepairUtility", "Gravship", "GravshipUtility", "WorldComponent_GravshipController",
    "JobDriver_CleanPollution", "JobDriver_MechRepair", "JobDriver_CleanToxins",
)
verbs = (
    "Birth", "Assign", "Apply", "Finish", "Complete", "Produce",
    "Process", "Activate", "Launch", "Land", "Repair", "Create", "Clean",
    "Spawn", "Pollut", "Add", "Remove", "Age", "Grow", "Stage", "Notify", "Outcome",
)
for entry in entries:
    name = entry["fullName"]
    simple = name.rsplit(".", 1)[-1]
    if "+" not in simple and any(x in simple.lower() for x in ("permit", "psycast", "ritual", "tox", "waste", "pollut", "containment", "study", "gravship", "mechgestat")):
        print("CANDIDATE:", name)
    # Exact type-name match only: nested compiler-generated classes and hundreds of
    # RitualOutcomeComp_* implementations are not hook candidates.
    if "+" in name or name.rsplit(".", 1)[-1] not in names:
        continue
    print("API TYPE:", name)
    if name.rsplit(".",1)[-1] in {"WorldGrid","PlanetLayer","Tile","GameInfo","Gravship","WorldComponent_GravshipController","Building_MechGestator","LordJob_Ritual","RitualOutcomePossibility","CompMechGestatorTank","CompMechRepairable","Pawn_AgeTracker"}:
        print("  FIELDS:",", ".join(f["name"] for f in entry.get("fields",[])[:75]))
    methods = entry.get("methods", []) if name in {"RimWorld.Planet.WorldGrid", "RimWorld.Planet.PlanetLayer", "RimWorld.Planet.Tile","RimWorld.CompMechGestatorTank","RimWorld.Building_MechGestator","RimWorld.MechRepairUtility","Verse.WorldComponent_GravshipController","RimWorld.Planet.Gravship"} else [m for m in entry.get("methods", []) if any(word.lower() in m["name"].lower() for word in verbs)]
    for m in methods[:85]:
        types = m["signature"]["parameters"]
        pars = m["parameters"]
        args = ", ".join((types[i] if i < len(types) else "?") + " " + p.get("name", "")
                         for i, p in enumerate(pars))
        print("  ", m["name"], "(", args, ")", m.get("attributes", ""))
