#!/usr/bin/env python3
"""Fetch read-only development inputs from the R2 bucket (development only; nothing here ships with the mod).

    fetch_dev_inputs.py refs     <dest> [--platform {windows|linux}]
                                         game reference DLLs -> <dest>/game/, Harmony -> <dest>/harmony/
                                         (defaults to Game-Windows 1.7.5011 rev24; override via --platform or RWS_DEV_GAME_PLATFORM)
    fetch_dev_inputs.py saves    <dest>  the real saves used by the opt-in regression test
    fetch_dev_inputs.py audit    <dest>  the audited 1.7.4985 index subset used by the reference check
    fetch_dev_inputs.py workshop <dest> <id> [<id> ...]
                                         specific Workshop/<id>/ packages -> <dest>/<id>/ (read-only)

Connection settings come from the environment: R2_ENDPOINT, R2_BUCKET, AWS_ACCESS_KEY_ID,
AWS_SECRET_ACCESS_KEY (boto3 reads the last two itself). Secret values and the bucket name are
never printed. Files already present with the right size are not downloaded again.
"""

import os
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path, PurePosixPath

AUDIT_ROOT = "research/vanilla-1.7.4985-audited/"
AUDITED_INDEX_VERSION = "1.7.4985 rev1269"

GAME_PREFIX_BY_PLATFORM = {
    "windows": "Game-Windows",
    "linux": "Game-Linux",
}
EXPECTED_GAME_VERSIONS = {
    "windows": "1.7.5011 rev24",
    "linux": "1.7.4985 rev1269",
}
EXPECTED_GAME_VERSION = EXPECTED_GAME_VERSIONS["windows"]
HARMONY_REF = ("Workshop/2009463077/1.7/Assemblies/0Harmony.dll", "harmony/0Harmony.dll")


def refs_for_platform(platform="windows"):
    if platform not in GAME_PREFIX_BY_PLATFORM:
        die(f"unsupported platform {platform!r}; expected one of: windows, linux")
    prefix = GAME_PREFIX_BY_PLATFORM[platform]
    return [
        (f"{prefix}/Assembly-CSharp.dll", "game/Assembly-CSharp.dll"),
        (f"{prefix}/UnityStubs.dll", "game/UnityStubs.dll"),
        (f"{prefix}/Waylith.dll", "game/Waylith.dll"),
        (f"{prefix}/Version.txt", "game/Version.txt"),
        HARMONY_REF,
    ]


REFS = refs_for_platform("windows")
AUDIT_PREFIXES = ["tools/", "assembly/Assembly-CSharp/", "assembly/UnityStubs/"]
AUDIT_FILES = ["runtimeconfig.json", "audit-summary.json", "index.json", "START-HERE.md"]


def die(message):
    print(f"error: {message}", file=sys.stderr)
    sys.exit(2)


def client():
    missing = [v for v in ("R2_ENDPOINT", "R2_BUCKET", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY") if not os.environ.get(v)]
    if missing:
        die("missing environment variables: " + ", ".join(missing))
    try:
        import boto3
    except ImportError:
        die("boto3 is required (python3 -m venv .devinputs/venv && .devinputs/venv/bin/pip install boto3)")
    return boto3.client("s3", endpoint_url=os.environ["R2_ENDPOINT"]), os.environ["R2_BUCKET"]


def safe_target(dest, relative):
    """Resolve <dest>/<relative>, refusing keys that would escape <dest>."""
    rel = PurePosixPath(relative)
    if rel.is_absolute() or ".." in rel.parts or not rel.parts:
        die(f"refusing unsafe object key: {relative!r}")
    target = (dest / Path(*rel.parts)).resolve()
    if dest.resolve() not in target.parents:
        die(f"refusing unsafe object key: {relative!r}")
    return target


def validate_workshop_ids(raw_ids):
    if not raw_ids:
        die("workshop requires at least one numeric Workshop ID")
    out = []
    seen = set()
    for wid in raw_ids:
        if not wid or not wid.isascii() or not wid.isdigit():
            die(f"invalid Workshop ID {wid!r}: expected ASCII digits only")
        if wid not in seen:
            seen.add(wid)
            out.append(wid)
    return out


def parse_args(argv):
    usage = (
        "usage: fetch_dev_inputs.py "
        "{refs <dest> [--platform {windows|linux}] | saves <dest> | audit <dest> | workshop <dest> <id> [<id> ...]}"
    )
    if len(argv) < 3:
        die(usage)
    kind = argv[1]
    if kind not in ("refs", "saves", "audit", "workshop"):
        die(usage)

    platform = os.environ.get("RWS_DEV_GAME_PLATFORM", "windows").strip().lower() or "windows"
    positional = []
    i = 2
    while i < len(argv):
        arg = argv[i]
        if arg == "--platform":
            if kind != "refs" or i + 1 >= len(argv):
                die(usage)
            platform = argv[i + 1].strip().lower()
            i += 2
        elif arg.startswith("--platform="):
            if kind != "refs":
                die(usage)
            platform = arg.split("=", 1)[1].strip().lower()
            i += 1
        elif arg.startswith("-"):
            die(usage)
        else:
            positional.append(arg)
            i += 1

    if kind in ("refs", "saves", "audit"):
        if len(positional) != 1:
            die(usage)
        if kind == "refs" and platform not in GAME_PREFIX_BY_PLATFORM:
            die(f"unsupported platform {platform!r}; expected one of: windows, linux")
        return kind, Path(positional[0]), platform, []

    if len(positional) < 2:
        die(usage)
    return kind, Path(positional[0]), platform, validate_workshop_ids(positional[1:])


def list_prefix(s3, bucket, prefix):
    out = []
    for page in s3.get_paginator("list_objects_v2").paginate(Bucket=bucket, Prefix=prefix):
        out += [(o["Key"], o["Size"]) for o in page.get("Contents", []) if not o["Key"].endswith("/")]
    return out


def download(s3, bucket, jobs):
    """jobs: list of (key, size or None, target Path). Returns (downloaded, skipped, bytes)."""
    def one(job):
        key, size, target = job
        if size is None:
            size = s3.head_object(Bucket=bucket, Key=key)["ContentLength"]
        if target.is_file() and target.stat().st_size == size:
            return 0, 1, 0
        target.parent.mkdir(parents=True, exist_ok=True)
        tmp = target.with_name(target.name + ".part")
        s3.download_file(bucket, key, str(tmp))
        if tmp.stat().st_size != size:
            tmp.unlink()
            raise RuntimeError(f"size mismatch for {key}")
        tmp.replace(target)
        return 1, 0, size

    with ThreadPoolExecutor(max_workers=8) as pool:
        results = list(pool.map(one, jobs))
    return sum(r[0] for r in results), sum(r[1] for r in results), sum(r[2] for r in results)


def main(argv, s3_client_factory=client):
    kind, dest, platform, workshop_ids = parse_args(argv)
    dest.mkdir(parents=True, exist_ok=True)
    s3, bucket = s3_client_factory()

    if kind == "refs":
        jobs = [(key, None, safe_target(dest, rel)) for key, rel in refs_for_platform(platform)]
    elif kind == "saves":
        objects = list_prefix(s3, bucket, "Saves/")
        jobs = [(key, size, safe_target(dest, key[len("Saves/"):])) for key, size in objects]
    elif kind == "workshop":
        jobs = []
        for wid in workshop_ids:
            objects = list_prefix(s3, bucket, f"Workshop/{wid}/")
            if not objects:
                die(f"nothing found for Workshop/{wid}/")
            jobs += [(key, size, safe_target(dest, key[len("Workshop/"):])) for key, size in objects]
    else:
        objects = []
        for prefix in AUDIT_PREFIXES:
            objects += list_prefix(s3, bucket, AUDIT_ROOT + prefix)
        jobs = [(key, size, safe_target(dest, key[len(AUDIT_ROOT):])) for key, size in objects]
        jobs += [(AUDIT_ROOT + name, None, safe_target(dest, name)) for name in AUDIT_FILES]

    if not jobs:
        die(f"nothing found for '{kind}'")
    downloaded, skipped, size = download(s3, bucket, jobs)
    print(f"{kind}: {len(jobs)} files ({downloaded} downloaded, {skipped} already present, {size / 1e6:.1f} MB fetched)")

    if kind == "refs":
        version = (dest / "game" / "Version.txt").read_text(encoding="utf-8").strip()
        expected = EXPECTED_GAME_VERSIONS[platform]
        note = "" if version == expected else f"  (WARNING: expected {expected} for {platform}; audited index is {AUDITED_INDEX_VERSION})"
        print(f"game version ({platform}): {version}{note}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
