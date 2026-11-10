#!/usr/bin/env python3
"""Verify a published release ZIP and stage it for Steam Workshop, offline only.

No Steam login, upload, credentials, or writes to game saves.
"""
import argparse
import hashlib
import json
import re
import shutil
import stat
import struct
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

APP_ID = "294100"
PACKAGE_ID = "pengin0503.rimworldstatistics"
HARMONY_ID = "brrainz.harmony"
ZIP_ROOT = "RimworldStatistics/"
REQUIRED_DLLS = {
    "RimworldStatistics.dll", "RimworldStatistics.Core.dll",
    "RimworldStatistics.Api.dll",
}
REQUIRED = {
    "About/About.xml",
    *(f"1.7/Assemblies/{dll}" for dll in REQUIRED_DLLS),
    "Languages/English/Keyed/RimworldStatistics.xml",
    "Languages/Japanese/Keyed/RimworldStatistics.xml",
}
MAX_FILE_SIZE = 100 * 1024 * 1024
MAX_TOTAL_SIZE = 200 * 1024 * 1024


def check_version(version: str) -> None:
    if re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version) is None:
        raise ValueError("Version must be stable X.Y.Z (no prerelease or arbitrary tag)")


def allowed_payload(rel: str) -> bool:
    """Fail closed for executable/development content; permit supported RimWorld assets."""
    parts = rel.split("/")
    if rel in REQUIRED:
        return True
    if rel in ("About/ModIcon.png", "About/ModIcon.jpg"):
        return True
    if len(parts) == 3 and parts[0] == "1.7" and parts[1] == "Assemblies":
        return parts[2].endswith(".dll") and len(parts[2]) > 4
    if len(parts) >= 4 and parts[0] == "Languages":
        return (parts[1] not in ("", ".", "..") and
                parts[2] in ("Keyed", "DefInjected", "Strings") and
                Path(parts[-1]).suffix.lower() in (".xml", ".txt"))
    if parts[0] in ("Defs", "Patches") and len(parts) >= 2:
        return Path(parts[-1]).suffix.lower() == ".xml"
    if parts[0] == "Textures" and len(parts) >= 2:
        return Path(parts[-1]).suffix.lower() in (".png", ".jpg", ".jpeg")
    if parts[0] == "Sounds" and len(parts) >= 2:
        return Path(parts[-1]).suffix.lower() in (".ogg", ".wav")
    return False


def check_preview(path: Path) -> None:
    data = path.read_bytes()
    if len(data) >= 1_000_000 or not data.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("Preview must be a PNG under 1MB")
    if len(data) < 24 or struct.unpack(">II", data[16:24]) != (640, 360):
        raise ValueError("Preview must be 640x360")


def verify_sha(zip_path: Path, sha_file: Path) -> None:
    lines = sha_file.read_text(encoding="utf-8").strip().splitlines()
    entries = [line.split() for line in lines if line.strip()]
    if (len(entries) != 1 or len(entries[0]) != 2 or
            entries[0][1].lstrip("*") != zip_path.name or
            re.fullmatch(r"[0-9a-fA-F]{64}", entries[0][0]) is None):
        raise ValueError("Checksum file does not describe the selected release ZIP")
    digest = hashlib.file_digest(zip_path.open("rb"), "sha256").hexdigest()
    if digest.lower() != entries[0][0].lower():
        raise ValueError("Release ZIP SHA256 mismatch")


def stage(zip_path: Path, preview: Path, out: Path, version: str,
          license_path: Path) -> Path:
    check_version(version)
    if out.exists() or out.is_symlink():
        raise ValueError("Output folder must not exist (prevent stale files)")
    check_preview(preview)
    license_text = license_path.read_text(encoding="utf-8")
    if (not license_text.startswith("MIT License\n") or
            "Copyright (c) 2026 pengin0503" not in license_text):
        raise ValueError("MIT license/copyright text is missing")
    with zipfile.ZipFile(zip_path) as z:
        entries = [e for e in z.infolist() if not e.is_dir()]
        names = [e.filename for e in entries]
        if not names or len(names) != len(set(n.casefold() for n in names)):
            raise ValueError("Duplicate or empty ZIP")
        required = {ZIP_ROOT + name for name in REQUIRED}
        missing = required - set(names)
        if missing:
            raise ValueError(f"Unexpected ZIP payload: missing={sorted(missing)}")
        total = 0
        for entry in entries:
            name = entry.filename
            rel = name[len(ZIP_ROOT):] if name.startswith(ZIP_ROOT) else ""
            parts = rel.split("/")
            unix_mode = entry.external_attr >> 16
            if (not rel or "\\" in name or "\x00" in name or
                    any(part in ("", ".", "..") for part in parts) or
                    ":" in rel or not allowed_payload(rel) or
                    stat.S_ISLNK(unix_mode)):
                raise ValueError(f"Unsafe or unapproved ZIP payload: {name}")
            if entry.file_size > MAX_FILE_SIZE:
                raise ValueError("Individual ZIP entry too large")
            total += entry.file_size
            if total > MAX_TOTAL_SIZE:
                raise ValueError("ZIP uncompressed size exceeds limit")
        root = ET.fromstring(z.read(ZIP_ROOT + "About/About.xml"))
        if root.findtext("modVersion") != version:
            raise ValueError("Release modVersion mismatch")
        if root.findtext("packageId") != PACKAGE_ID:
            raise ValueError("Release packageId mismatch")
        if "1.7" not in [elem.text for elem in root.findall("supportedVersions/li")]:
            raise ValueError("Expected RimWorld 1.7 support")
        if HARMONY_ID not in [e.findtext("packageId") for e in root.findall("modDependencies/li")]:
            raise ValueError("Missing Harmony dependency")
        for name in names:
            if name.endswith(".xml"):
                ET.fromstring(z.read(name))
        mod = out / "RimworldStatistics"
        for entry in entries:
            dest = mod / entry.filename[len(ZIP_ROOT):]
            dest.parent.mkdir(parents=True, exist_ok=True)
            with z.open(entry) as src, dest.open("wb") as dst:
                shutil.copyfileobj(src, dst)
        shutil.copyfile(preview, mod / "About/Preview.png")
        shutil.copyfile(license_path, mod / "LICENSE")
    return mod


def write_manifest(mod: Path, target: Path, version: str) -> None:
    """Manifest lives OUTSIDE the mod folder and binds all uploaded file bytes."""
    check_version(version)
    files = [{"path": p.relative_to(mod).as_posix(),
              "sha256": hashlib.sha256(p.read_bytes()).hexdigest()}
             for p in sorted(mod.rglob("*")) if p.is_file()]
    if not {e["path"] for e in files}.issuperset(REQUIRED | {"LICENSE", "About/Preview.png"}):
        raise ValueError("Staged mod is missing required files")
    target.write_text(json.dumps({
        "schema": 1, "version": version, "packageId": PACKAGE_ID,
        "files": files,
    }, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def vdf_quote(value: str) -> str:
    if any(ch in value for ch in ('"', "\n", "\r", "\x00")):
        raise ValueError("Unsafe VDF value")
    return '"' + value.replace("\\", "\\\\") + '"'


def write_vdf(mod: Path, target: Path, workshop_id: str) -> None:
    """Legacy offline sample only; the artifact never ships Linux absolute paths."""
    if re.fullmatch(r"(?:0|[1-9][0-9]*)", workshop_id) is None:
        raise ValueError("Published file ID must be 0 or a positive ASCII integer")
    version = ET.parse(mod / "About/About.xml").getroot().findtext("modVersion")
    fields = {
        "appid": APP_ID, "publishedfileid": workshop_id,
        "contentfolder": str(mod.resolve()),
        "previewfile": str((mod / "About/Preview.png").resolve()),
        "visibility": "2",
        "title": "Rimworld Statistics",
        "description": "Lifetime statistics across saves. Requires Harmony. See bilingual listing.",
        "changenote": f"Rimworld Statistics v{version}",
    }
    target.write_text('"workshopitem"\n{\n'
                      + ''.join(f'    {vdf_quote(k)} {vdf_quote(v)}\n' for k, v in fields.items())
                      + '}\n', encoding="utf-8")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--zip", type=Path, required=True)
    p.add_argument("--sha256", type=Path, required=True)
    p.add_argument("--preview", type=Path, required=True)
    p.add_argument("--license", type=Path, required=True)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--version", required=True)
    p.add_argument("--published-file-id", default="0")
    a = p.parse_args()
    verify_sha(a.zip, a.sha256)
    mod = stage(a.zip, a.preview, a.out, a.version, a.license)
    write_manifest(mod, a.out / "workshop-manifest.json", a.version)
    write_vdf(mod, a.out / "workshop.vdf", a.published_file_id)
    print(f"WORKSHOP_PREP_OK app={APP_ID} version={a.version} ID={a.published_file_id} visibility=private")
    print(f"MOD_ROOT={mod}")
    print("UPLOAD=NOT_PERFORMED")


if __name__ == "__main__":
    main()
