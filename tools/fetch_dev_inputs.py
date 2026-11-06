#!/usr/bin/env python3
"""Fetch development inputs from the R2 bucket (development only; nothing here ships with the mod).

    fetch_dev_inputs.py refs  <dest>   game reference DLLs -> <dest>/game/, Harmony -> <dest>/harmony/
    fetch_dev_inputs.py saves <dest>   the real saves used by the opt-in regression test
    fetch_dev_inputs.py audit <dest>   the audited 1.7.4985 index subset used by the reference check

Connection settings come from the environment: R2_ENDPOINT, R2_BUCKET, AWS_ACCESS_KEY_ID,
AWS_SECRET_ACCESS_KEY (boto3 reads the last two itself). Secret values and the bucket name are
never printed. Files already present with the right size are not downloaded again.
"""

import os
import sys
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path, PurePosixPath

AUDIT_ROOT = "research/vanilla-1.7.4985-audited/"
EXPECTED_GAME_VERSION = "1.7.4985 rev1269"

REFS = [
    ("Game-Linux/Assembly-CSharp.dll", "game/Assembly-CSharp.dll"),
    ("Game-Linux/UnityStubs.dll", "game/UnityStubs.dll"),
    ("Game-Linux/Waylith.dll", "game/Waylith.dll"),
    ("Game-Linux/Version.txt", "game/Version.txt"),
    ("Workshop/2009463077/1.7/Assemblies/0Harmony.dll", "harmony/0Harmony.dll"),
]
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


def main(argv):
    if len(argv) != 3 or argv[1] not in ("refs", "saves", "audit"):
        die("usage: fetch_dev_inputs.py {refs|saves|audit} <dest>")
    kind, dest = argv[1], Path(argv[2])
    dest.mkdir(parents=True, exist_ok=True)
    s3, bucket = client()

    if kind == "refs":
        jobs = [(key, None, safe_target(dest, rel)) for key, rel in REFS]
    elif kind == "saves":
        objects = list_prefix(s3, bucket, "Saves/")
        jobs = [(key, size, safe_target(dest, key[len("Saves/"):])) for key, size in objects]
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
        note = "" if version == EXPECTED_GAME_VERSION else f"  (WARNING: the audited index is {EXPECTED_GAME_VERSION})"
        print(f"game version: {version}{note}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
