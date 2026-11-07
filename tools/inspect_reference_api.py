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
)
verbs = (
    "Birth", "Assign", "Apply", "Finish", "Complete", "Produce",
    "Process", "Activate", "Launch", "Land", "Repair", "Create", "Clean",
    "Spawn", "Pollut", "Add", "Remove", "Age", "Grow", "Outcome",
)
for entry in entries:
    name = entry["fullName"]
    if not any(part in name for part in names):
        continue
    print("API TYPE:", name)
    methods = [m for m in entry.get("methods", []) if any(word.lower() in m["name"].lower() for word in verbs)]
    for m in methods[:35]:
        types = m["signature"]["parameters"]
        pars = m["parameters"]
        args = ", ".join((types[i] if i < len(types) else "?") + " " + p.get("name", "")
                         for i, p in enumerate(pars))
        print("  ", m["name"], "(", args, ")", m.get("attributes", ""))
