#!/usr/bin/env python3
"""Check that the host release contains exactly the generic SDK and core DLLs."""
import sys
import zipfile
from pathlib import PurePosixPath

if len(sys.argv)!=2:
    raise SystemExit("usage: check_sdk_package.py host.zip")
with zipfile.ZipFile(sys.argv[1]) as archive:
    failed=archive.testzip()
    if failed:
        raise SystemExit(f"Corrupted ZIP member: {failed}")
    paths=archive.namelist()
    dlls=sorted(PurePosixPath(p).name for p in paths if PurePosixPath(p).suffix.lower()==".dll")
    expected=["RimworldStatistics.Api.dll","RimworldStatistics.Core.dll","RimworldStatistics.dll"]
    if dlls!=expected:
        raise SystemExit(f"Unexpected host assemblies (or addon DLL): {dlls!r}")
    if any("/addons/" in p.lower() or "addon.dll" in p.lower() or "recruitment.dll" in p.lower()
           or "hospitality.dll" in p.lower() for p in paths):
        raise SystemExit("Optional third-party addon leaked into host archive")
print("Host ZIP SDK ownership and optional-addon isolation: PASS")
