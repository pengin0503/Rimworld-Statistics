#!/usr/bin/env python3
"""Fail closed if the audited public Recruitment 1.1.2 source API is not present.

This is a build-time source/ABI gate, NOT a claim that Harmony was exercised in a game.
The GitHub Actions workflow checks out an immutable, reviewed upstream commit.
"""
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

if len(sys.argv) != 2:
    raise SystemExit("usage: check_recruitment_contract.py PATH_TO_PINNED_RECRUITMENT")
root = Path(sys.argv[1])
if not root.is_dir():
    raise SystemExit("missing pinned Recruitment source")
meta = ET.parse(root / "About" / "About.xml").getroot()
def val(key: str) -> str:
    node = meta.find(key)
    if node is None or not node.text:
        raise AssertionError(f"About.xml lacks {key}")
    return node.text.strip()
assert val("packageId").lower() == "pengin0503.recruitment", val("packageId")
assert val("modVersion") == "1.1.2", val("modVersion")
assert "1.7" in (root / "About" / "About.xml").read_text(encoding="utf-8")
csproj = (root / "Source" / "Recruitment" / "Recruitment.csproj").read_text(encoding="utf-8")
assert re.search(r"<AssemblyName>Recruitment</AssemblyName>", csproj)
code = (root / "Source" / "Recruitment" / "GameComponent_Recruitment.cs").read_text(encoding="utf-8")
drive = (root / "Source" / "Recruitment" / "RecruitmentDrive.cs").read_text(encoding="utf-8")
assert re.search(
    r"private\s+RecruitmentDrive\s+AddDrive\(RecruitmentMethodDef\s+method,\s*Map\s+map,\s*Faction\s+origin,\s*int\s+paid,\s*int\s+social,\s*int\s+premiumTier,\s*bool\s+direct\s*=\s*false\)",
    code), "AddDrive signature changed"
assert re.search(r"drives\.Add\(drive\);\s*return\s+drive;", code), "AddDrive success ordering changed"
assert "id = nextId++" in code, "drive id generation changed"
assert re.search(r"public\s+IReadOnlyList<RecruitmentDrive>\s+Drives\s*=>\s*drives;", code), "Drives property changed"
assert re.search(r"public\s+static\s+GameComponent_Recruitment\s+Instance\s*=>", code), "Instance property changed"
assert re.search(r"public\s+int\s+id;", drive), "Drive identity field changed"
assert re.search(r"public\s+int\s+paidSilver;", drive), "Paid silver field changed"
assert re.search(r"public\s+int\s+applicantCount;", drive), "Planned applicant count field changed"
assert "paidSilver = paid" in code and "applicantCount = RecruitmentUtility.RollApplicantCount(social)" in code
assert "Scribe_Values.Look(ref paidSilver" in drive and "Scribe_Values.Look(ref applicantCount" in drive
assert "Scribe_Values.Look(ref nextId, \"nextId\"" in code
assert "RecruitmentUtility.TryPay(map, cost)" in code

print("Recruitment source ABI audit passed: 1.1.2 / AddDrive / id / Drives / Instance")
