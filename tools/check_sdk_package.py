#!/usr/bin/env python3
"""Check that the host release contains only whitelisted host files and exact SDK/core DLLs."""
import argparse
import re
import zipfile
from pathlib import Path, PurePosixPath

REQUIRED_FILES = {
    "RimworldStatistics/About/About.xml",
    "RimworldStatistics/About/Preview.png",
    "RimworldStatistics/About/PublishedFileId.txt",
    "RimworldStatistics/1.7/Assemblies/RimworldStatistics.Api.dll",
    "RimworldStatistics/1.7/Assemblies/RimworldStatistics.Core.dll",
    "RimworldStatistics/1.7/Assemblies/RimworldStatistics.dll",
}
OPTIONAL_ROOT_FILES = {
    "RimworldStatistics/LoadFolders.xml",
    "RimworldStatistics/LICENSE",
    "RimworldStatistics/README.md",
}
ALLOWED_KEYED_RE = re.compile(r"^RimworldStatistics/Languages/[A-Za-z]+/Keyed/[A-Za-z0-9_]+\.xml$")
ALLOWED_DIR_RE = re.compile(
    r"^RimworldStatistics/(About/|1\.7/|1\.7/Assemblies/|Languages/|Languages/[A-Za-z]+/|Languages/[A-Za-z]+/Keyed/)?$"
)

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("host_zip", help="Path to RimworldStatistics host ZIP")
parser.add_argument(
    "--verify-contents",
    action="store_true",
    help="Also verify About/* byte identity, PublishedFileId.txt digits, exact Languages/*/Keyed/*.xml set, and strict whitelist",
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
    expected_dlls = ["RimworldStatistics.Api.dll", "RimworldStatistics.Core.dll", "RimworldStatistics.dll"]
    if dlls != expected_dlls:
        raise SystemExit(f"Unexpected host assemblies (or addon DLL): {dlls!r}")

    file_paths = set()
    unexpected = []
    for p in paths:
        if p.endswith("/"):
            if not ALLOWED_DIR_RE.fullmatch(p):
                unexpected.append(p)
            continue
        file_paths.add(p)
        if p in REQUIRED_FILES or p in OPTIONAL_ROOT_FILES or ALLOWED_KEYED_RE.fullmatch(p):
            continue
        unexpected.append(p)

    if unexpected:
        raise SystemExit(f"Unexpected non-whitelisted entries in host ZIP: {sorted(unexpected)!r}")

    missing_required = sorted(REQUIRED_FILES - file_paths)
    if missing_required:
        raise SystemExit(f"Missing required host files in ZIP: {missing_required!r}")

    if args.verify_contents:
        for about_file in ("About/About.xml", "About/Preview.png", "About/PublishedFileId.txt"):
            member = f"RimworldStatistics/{about_file}"
            if archive.read(member) != Path(about_file).read_bytes():
                raise SystemExit(f"About file content mismatch in production ZIP: {member}")
        published_id = archive.read("RimworldStatistics/About/PublishedFileId.txt").decode("ascii").strip()
        if not published_id.isdigit():
            raise SystemExit(f"Invalid PublishedFileId.txt content: {published_id!r}")

        expected_keyed = {
            f"RimworldStatistics/{p.as_posix()}"
            for p in Path("Languages").glob("*/Keyed/*.xml")
        }
        actual_keyed = {p for p in file_paths if p.startswith("RimworldStatistics/Languages/")}
        missing = sorted(expected_keyed - actual_keyed)
        if missing:
            raise SystemExit(f"Missing keyed XMLs in production ZIP: {missing}")
        extra_keyed = sorted(actual_keyed - expected_keyed)
        if extra_keyed:
            raise SystemExit(f"Unexpected keyed XMLs in production ZIP: {extra_keyed}")

        strict_allowed = REQUIRED_FILES | expected_keyed
        disallowed = sorted(file_paths - strict_allowed)
        if disallowed:
            raise SystemExit(f"Unexpected files in production ZIP: {disallowed}")

if args.harness_zip:
    with zipfile.ZipFile(args.harness_zip) as harness:
        failed = harness.testzip()
        if failed:
            raise SystemExit(f"Corrupted VerifyHarness ZIP member: {failed}")

print("Host ZIP SDK ownership, strict whitelist, and optional-addon isolation: PASS")
