#!/usr/bin/env python3
"""Stage immutable GitHub release ZIP plus preview for Steam Workshop.

NO Steam login, uploads, credential reads, workshop writes or game saves.
"""
import argparse
import hashlib
import re
import shutil
import struct
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

APP_ID = "294100"
ZIP_ROOT = "RimworldStatistics/"
REQUIRED_DLLS = {"RimworldStatistics.dll", "RimworldStatistics.Core.dll", "RimworldStatistics.Api.dll"}


def check_preview(path: Path) -> None:
    data = path.read_bytes()
    if len(data) >= 1_000_000 or not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("Preview must be a PNG under 1MB")
    if len(data) < 24 or struct.unpack(">II", data[16:24]) != (640, 360):
        raise ValueError("Preview must be 640x360")


def verify_sha(zip_path: Path, sha_file: Path) -> None:
    lines = sha_file.read_text(encoding="utf-8").strip().splitlines()
    entries = [line.split() for line in lines if line.strip()]
    if len(entries) != 1 or len(entries[0]) != 2 or entries[0][1].lstrip("*") != zip_path.name:
        raise ValueError("Checksum file does not describe the selected release ZIP")
    digest = hashlib.sha256(zip_path.read_bytes()).hexdigest()
    if digest.lower() != entries[0][0].lower():
        raise ValueError("Release ZIP SHA256 mismatch")


def stage(zip_path: Path, preview: Path, out: Path, version: str, license_path: Path) -> Path:
    if out.exists() or out.is_symlink():
        raise ValueError("Output folder must not exist (prevent stale files)")
    check_preview(preview)
    license_text = license_path.read_text(encoding="utf-8")
    if not license_text.startswith("MIT License\n") or "Copyright (c) 2026 pengin0503" not in license_text:
        raise ValueError("MIT license/copyright text is missing")
    with zipfile.ZipFile(zip_path) as z:
        entries = [entry for entry in z.infolist() if not entry.is_dir()]
        names = [entry.filename for entry in entries]
        if len(names) != len(set(names)) or not names:
            raise ValueError("Duplicate or empty ZIP")
        for entry in entries:
            rel = entry.filename
            if not rel.startswith(ZIP_ROOT) or "\\" in rel or ".." in Path(rel).parts:
                raise ValueError(f"Unsafe ZIP entry: {rel}")
        expected = {ZIP_ROOT + "About/About.xml"}
        expected |= {ZIP_ROOT + "1.7/Assemblies/" + dll for dll in REQUIRED_DLLS}
        expected |= {
            ZIP_ROOT + f"Languages/{lang}/Keyed/RimworldStatistics.xml"
            for lang in ("English", "Japanese")
        }
        if set(names) != expected:
            raise ValueError(f"Unexpected ZIP payload: missing={sorted(expected-set(names))}, extra={sorted(set(names)-expected)}")
        root = ET.fromstring(z.read(ZIP_ROOT + "About/About.xml"))
        if root.findtext("modVersion") != version:
            raise ValueError("Release modVersion mismatch")
        if root.findtext("packageId") != "pengin0503.rimworldstatistics":
            raise ValueError("Release packageId mismatch")
        if "1.7" not in [elem.text for elem in root.findall("supportedVersions/li")]:
            raise ValueError("Expected RimWorld 1.7 support")
        if "brrainz.harmony" not in [elem.findtext("packageId") for elem in root.findall("modDependencies/li")]:
            raise ValueError("Missing Harmony dependency")
        mod = out / "RimworldStatistics"
        for entry in entries:
            dest = mod / entry.filename[len(ZIP_ROOT):]
            dest.parent.mkdir(parents=True, exist_ok=True)
            with z.open(entry) as src, dest.open("wb") as dst:
                shutil.copyfileobj(src, dst)
        shutil.copyfile(preview, mod / "About/Preview.png")
        shutil.copyfile(license_path, mod / "LICENSE")
    return mod


def vdf_quote(value: str) -> str:
    if any(ch in value for ch in ('"', "\n", "\r", "\x00")):
        raise ValueError("Unsafe VDF value")
    # Windows file paths contain backslashes; SteamCMD VDF requires them escaped.
    return '"' + value.replace("\\", "\\\\") + '"'


def write_vdf(mod: Path, target: Path, workshop_id: str) -> None:
    if re.fullmatch(r"(?:0|[1-9][0-9]*)", workshop_id) is None:
        raise ValueError("Published file ID must be 0 or a positive integer")
    fields = {
        "appid": APP_ID,
        "publishedfileid": workshop_id,
        "contentfolder": str(mod.resolve()),
        "previewfile": str((mod / "About/Preview.png").resolve()),
        "visibility": "2",  # PRIVATE; never switch to public automatically
        "title": "Rimworld Statistics",
        "description": "Lifetime statistics across RimWorld saves. Requires Harmony. See full bilingual listing before making public.",
        "changenote": "Initial v1.3.0 Workshop candidate" if workshop_id == "0" else "Rimworld Statistics update",
    }
    target.write_text('"workshopitem"\n{\n'
                      + ''.join(f'    {vdf_quote(k)} {vdf_quote(v)}\n' for k,v in fields.items())
                      + '}\n', encoding="utf-8")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--zip", type=Path, required=True)
    p.add_argument("--sha256", type=Path, required=True)
    p.add_argument("--preview", type=Path, required=True)
    p.add_argument("--license", type=Path, required=True)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--version", default="1.3.0")
    p.add_argument("--published-file-id", default="0")
    a = p.parse_args()
    verify_sha(a.zip, a.sha256)
    mod = stage(a.zip, a.preview, a.out, a.version, a.license)
    write_vdf(mod, a.out / "workshop.vdf", a.published_file_id)
    print(f"WORKSHOP_PREP_OK app={APP_ID} version={a.version} ID={a.published_file_id} visibility=private")
    print(f"MOD_ROOT={mod}")
    print("UPLOAD=NOT_PERFORMED")


if __name__ == "__main__":
    main()
