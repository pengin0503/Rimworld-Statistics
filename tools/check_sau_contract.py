#!/usr/bin/env python3
"""Static contract check against a pinned source-audited Smart Apparel Upgrade tree.

Never claims actual RimWorld execution success. The separate runtime reflection
guard also fails closed when method/field shape changes.
"""
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1]) if len(sys.argv) == 2 else None
if not root or not root.is_dir():
    raise SystemExit("usage: check_sau_contract.py <pinned-SAU-source-root>")

code = (root / "Source" / "SmartApparelUpgrade.cs").read_text(encoding="utf-8-sig")
xml = ET.parse(root / "About" / "About.xml").getroot()
pkg = xml.findtext("packageId")
version = xml.findtext("modVersion")
if pkg != "pengin0503.smartapparelupgrade" or version != "1.0.1":
    raise SystemExit(f"Unexpected SAU package/version: {pkg}/{version}")

checks = [
    "public sealed class ApparelScanner : MapComponent",
    "void OrderIdleUpgrades()",
    "List<SwapStamp> stamps",
    "public sealed class SwapStamp",
    "public string PawnId,TargetId,RemovedId;",
    "public int LastIssued;",
    "stamps.Add(new SwapStamp",
    "if(pawn.CurJob==job)",
    "job.playerForced=false;",
    "LastIssued=now",
]
missing = [text for text in checks if text not in code]
if missing:
    raise SystemExit("Pinned source compatibility check failed: " + repr(missing))
# The postfix runs after the source has recorded a successful StartJob, not on
# diagnostic suggestions or attempts that were rejected before pawn.CurJob==job.
method_start = code.index("void OrderIdleUpgrades()")
method = code[method_start:]
job_confirmed = method.index("if(pawn.CurJob==job)")
stamp_added = method.index("stamps.Add(new SwapStamp")
if stamp_added <= job_confirmed or "LastIssued=now" not in method[stamp_added:stamp_added + 260]:
    raise SystemExit("SAU event contract no longer stamps AFTER job issuance succeeds")
print("SAU 1.0.1 source contract: PASS (static only)")
