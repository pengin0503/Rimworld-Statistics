#!/usr/bin/env python3
"""Check the source contract of the pinned VPE 1.7 level and implant hooks."""
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(sys.argv[1])
metadata = ET.parse(root / "About/About.xml")
assert metadata.findtext("packageId", "").lower() == "vanillaexpanded.vpsycastse"
assert "1.7" in [node.text for node in metadata.findall("./supportedVersions/li")]
text = (root / "1.7/Source/VanillaPsycastsExpanded/Hediff_PsycastAbilities.cs").read_text(encoding="utf-8-sig")
assert re.search(r"class Hediff_PsycastAbilities\s*:\s*Hediff_Abilities\b", text)
assert re.search(r"public override void ChangeLevel\(int levelOffset\)\s*\{\s*base.ChangeLevel\(levelOffset\)", text)
assert re.search(r"public void ChangeLevel\(int levelOffset, bool sendLetter\)\s*\{\s*ChangeLevel\(levelOffset\)", text)
assert re.search(r"void InitializeFromPsylink\([^)]*\)\s*\{\s*this.psylink = psylink;\s*level = psylink.level;", text)
print("VPE 1.7 implant and level contract: PASS")
