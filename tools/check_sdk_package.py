#!/usr/bin/env python3
"""Check that the host release contains exactly the generic SDK and core DLLs."""
import argparse
import re
import zipfile
from pathlib import Path, PurePosixPath

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("host_zip", help="Path to RimworldStatistics host ZIP")
parser.add_argument(
    "--verify-contents",
    action="store_true",
    help="Also verify About/* byte identity, PublishedFileId.txt digits, Languages/*/Keyed/*.xml, and build-input/harness isolation",
)
parser.add_argument(
    "--harness-zip",
    help="Optional path to RimworldStatisticsVerifyHarness.zip for ZIP integrity check",
)
args = parser.parse_args()

with zipfile.ZipFile(args.host_zip) as archive:
    failed = archive.testzip()
    if failed:
        raise SystemExit(f"Corrupted ZIP member: {failed}")
    paths = archive.namelist()
    dlls = sorted(PurePosixPath(p).name for p in paths if PurePosixPath(p).suffix.lower() == ".dll")
    expected = ["RimworldStatistics.Api.dll", "RimworldStatistics.Core.dll", "RimworldStatistics.dll"]
    if dlls != expected:
        raise SystemExit(f"Unexpected host assemblies (or addon DLL): {dlls!r}")
    if any(
        "/addons/" in p.lower()
        or "addon.dll" in p.lower()
        or "recruitment.dll" in p.lower()
        or "hospitality.dll" in p.lower()
        for p in paths
    ):
        raise SystemExit("Optional third-party addon leaked into host archive")

    if args.verify_contents:
        leak_re = re.compile(r"(^|/)(Recruitment\.dll|Recruitment\.csproj|\.devinputs)(/|$)")
        if any(leak_re.search(p) for p in paths):
            raise SystemExit("Optional Recruitment dependency or build input leaked into production ZIP")
        if any("VerifyHarness" in p for p in paths):
            raise SystemExit("Development harness leaked into production ZIP")

        path_set = set(paths)
        for about_file in ("About/About.xml", "About/Preview.png", "About/PublishedFileId.txt"):
            member = f"RimworldStatistics/{about_file}"
            if member not in path_set:
                raise SystemExit(f"Missing About file in production ZIP: {member}")
            if archive.read(member) != Path(about_file).read_bytes():
                raise SystemExit(f"About file content mismatch in production ZIP: {member}")
        published_id = archive.read("RimworldStatistics/About/PublishedFileId.txt").decode("ascii").strip()
        if not published_id.isdigit():
            raise SystemExit(f"Invalid PublishedFileId.txt content: {published_id!r}")

        expected_keyed = {
            f"RimworldStatistics/{p.as_posix()}"
            for p in Path("Languages").glob("*/Keyed/*.xml")
        }
        missing = sorted(expected_keyed - path_set)
        if missing:
            raise SystemExit(f"Missing keyed XMLs in production ZIP: {missing}")

        for rel_path in sorted(list(Path("Defs").rglob("*.xml")) + list(Path("Languages").glob("*/DefInjected/**/*.xml"))):
            member = f"RimworldStatistics/{rel_path.as_posix()}"
            if member not in path_set:
                raise SystemExit(f"Missing Def/DefInjected XML in production ZIP: {member}")
            if archive.read(member) != rel_path.read_bytes():
                raise SystemExit(f"Def/DefInjected XML content mismatch in production ZIP: {member}")

if args.harness_zip:
    with zipfile.ZipFile(args.harness_zip) as harness:
        failed = harness.testzip()
        if failed:
            raise SystemExit(f"Corrupted VerifyHarness ZIP member: {failed}")

print("Host ZIP SDK ownership and optional-addon isolation: PASS")
