#!/usr/bin/env python3
"""Reject incompatible/missing/additional files in the optional addon ZIP."""
import sys
from pathlib import Path
from zipfile import ZipFile
import xml.etree.ElementTree as ET

if len(sys.argv) != 2 or not Path(sys.argv[1]).is_file():
    raise SystemExit("usage: check_sau_addon.py <addon.zip>")
prefix = "RimworldStatistics.SmartApparelAddon/"
expected = {
    prefix + "About/About.xml",
    prefix + "1.7/Assemblies/RimworldStatistics.SmartApparelAddon.dll",
    prefix + "Languages/Japanese/Keyed/RimworldStatistics_SAU.xml",
    prefix + "Languages/English/Keyed/RimworldStatistics_SAU.xml",
}
with ZipFile(sys.argv[1]) as zipfile:
    assert zipfile.testzip() is None, "Corrupt addon ZIP"
    names = zipfile.namelist()
    assert set(names) == expected, f"Addon ZIP contains unexpected/missing contents: {set(names) ^ expected}"
    assert len(names) == len(set(names)), "Duplicate ZIP entries"
    xml = ET.fromstring(zipfile.read(prefix + "About/About.xml"))
    assert xml.findtext("packageId") == "pengin0503.rws.smartappareladdon"
    assert b"RimworldStatistics.Api.dll" not in zipfile.read(prefix + "About/About.xml")
    for path in expected:
        assert not path.startswith("/") and ".." not in path, "Unsafe ZIP path"
print("Optional SAU addon package isolation: PASS")
