#!/usr/bin/env python3
"""Pinned, read-only provenance gate: DBH 1.7 exact binary and original UI units."""
from pathlib import Path
from hashlib import sha256
import sys
import xml.etree.ElementTree as ET

if len(sys.argv) != 2:
    raise SystemExit("usage: check_dbh_contract.py PATH_TO_PINNED_DBH")
root = Path(sys.argv[1])
meta = ET.parse(root / "About/About.xml").getroot()
assert (meta.findtext("packageId") or "").strip().lower() == "dubwise.dubsbadhygiene"
assert (meta.findtext("modVersion") or "").strip() == "3.2.3260"
assert any((node.text or "").strip() == "1.7" for node in meta.findall("supportedVersions/li"))
binary = root / "1.7/Assemblies/BadHygiene.dll"
actual = sha256(binary.read_bytes()).hexdigest()
assert actual == "475ee45786db312118e88d5f060da9cb8d45ef9ff3b041c26c85d0d3ed3e7b0b", (
    "Target DBH binary drifted: a new ABI/semantics audit is mandatory", actual)
strings = ET.parse(root / "1.7/Languages/English/Keyed/DubsHygiene.xml").getroot()
assert (strings.findtext("WaterStorage") or "").strip() == "Water stored: {0} L"
assert (strings.findtext("TotalWaterStorage") or "").strip() == "Piped water stored: {0} L"
assert "L/day" in (strings.findtext("PipedPumpCapacity") or ""), "Pumping capacity must remain L/day"
water_defs = (root / "1.7/Defs/ThingDefs_Buildings/BuildingsD_WaterManagement.xml").read_text(encoding="utf-8")
assert "<WaterStorageCap>" in water_defs, "DBH tank capacity values moved"

print("DBH pinned 1.7 binary, package 3.2.3260 and stored-water liter UI contract: PASS")
