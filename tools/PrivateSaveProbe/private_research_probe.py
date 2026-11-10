#!/usr/bin/env python3
"""Read-only, privacy-preserving structural probe for historical research XML.

No pawn/colony/mod/Def IDs, file paths, hashes, or original XML are emitted.
This intentionally DOES NOT infer ResearchProjectDef.IsFinished from raw progress.
Python >=3.10; stdlib only. Inputs are never written or modified.
"""
from __future__ import annotations
import argparse
from decimal import Decimal, InvalidOperation
import hashlib
import json
from pathlib import Path
import xml.etree.ElementTree as ET

MAX_INPUT_BYTES = 80 * 1024 * 1024
MAX_RESEARCH_PAIRS = 16384
MAX_XML_DEPTH = 256
MAX_ELEMENTS = 4_000_000

class ProbeError(Exception):
    pass

def digest(path: Path) -> bytes:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.digest()

def probe(path: Path, exact_game_version: str | None = None) -> dict:
    original = path.stat()
    if not path.is_file() or not (0 < original.st_size <= MAX_INPUT_BYTES):
        raise ProbeError("input is absent, empty or exceeds bounded size")
    original_hash = digest(path)
    stack: list[str] = []
    elements: list[ET.Element] = []
    pairs: dict[str, dict[str, list]] = {n: {"keys": [], "values": []} for n in ("progress", "techprints", "knowledge")}
    game_version: str | None = None
    num_mods = 0
    malformed = False
    num_elements = 0
    try:
        with path.open("rb") as handle:
            for event, element in ET.iterparse(handle, events=("start", "end")):
                if event == "start":
                    stack.append(element.tag)
                    elements.append(element)
                    if len(stack) > MAX_XML_DEPTH:
                        raise ProbeError("XML nesting depth exceeds limit")
                    num_elements += 1
                    if num_elements > MAX_ELEMENTS:
                        raise ProbeError("XML node count exceeds limit")
                    continue
                # Closed elements contain their entire text; never emit raw identifiers.
                if element.tag == "gameVersion" and stack[-2:] == ["meta", "gameVersion"]:
                    game_version = (element.text or "").strip()[:96]
                elif len(stack) >= 2 and stack[-2:] == ["modIds", "li"]:
                    num_mods += 1
                if len(stack) >= 4 and stack[-1] == "li" and stack[-2] in {"keys", "values"} and stack[-3] in pairs and stack[-4] == "researchManager":
                    section, kind = stack[-3], stack[-2]
                    record = pairs[section][kind]
                    if len(record) >= MAX_RESEARCH_PAIRS:
                        raise ProbeError("bounded research key/value count exceeded")
                    record.append((element.text or "").strip())
                stack.pop()
                elements.pop()
                element.clear()
                if elements:
                    elements[-1].remove(element)
    except ET.ParseError as exc:
        raise ProbeError("invalid XML") from exc
    except OSError as exc:
        raise ProbeError("input read failure") from exc
    if game_version is None:
        malformed = True
    sections = {}
    for section, pair in pairs.items():
        keys, values = pair["keys"], pair["values"]
        seen = set()
        duplicate = invalid_keys = invalid_values = nonzero = 0
        for key in keys:
            if not key or len(key) > 256:
                invalid_keys += 1
            if key in seen:
                duplicate += 1
            seen.add(key)
        for raw in values:
            try:
                d = Decimal(raw)
                if not d.is_finite() or d < 0:
                    invalid_values += 1
                elif d > 0:
                    nonzero += 1
            except InvalidOperation:
                invalid_values += 1
        sections[section] = {
            "keys": len(keys), "values": len(values),
            "pair_count_equal": len(keys) == len(values),
            "duplicate_keys": duplicate, "invalid_keys": invalid_keys,
            "invalid_values": invalid_values, "positive_values": nonzero,
            "structural_valid": bool(keys or values) and len(keys) == len(values)
                and duplicate == invalid_keys == invalid_values == 0,
        }
    after = path.stat()
    if original.st_size != after.st_size or original.st_mtime_ns != after.st_mtime_ns or original_hash != digest(path):
        raise ProbeError("original changed during read-only verification")
    return {
        "game_version": game_version,
        "exact_version_match": game_version == exact_game_version if exact_game_version is not None else None,
        "mod_count": num_mods,
        "xml_elements_scanned": num_elements,
        "research": sections,
        "completed_projects": None,  # Cannot establish IsFinished without exact runtime Defs.
        "source_unchanged": True,
        "reader_status": "complete" if not malformed else "missing-game-version",
    }

def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("paths", type=Path, nargs="+", help="private originals (not logged)")
    ap.add_argument("--expected-game-version", default=None)
    args = ap.parse_args()
    results = []
    for i, path in enumerate(args.paths):
        try:
            r = probe(path, args.expected_game_version)
            results.append({"input_index": i, **r})
        except (ProbeError, OSError) as exc:
            results.append({"input_index": i, "reader_status": "error", "reason": str(exc)})
    print(json.dumps({"schema": 1, "privacy": "no-input-identifiers", "results": results}, ensure_ascii=False, sort_keys=True))
    return 1 if any(r["reader_status"] != "complete" for r in results) else 0

if __name__ == "__main__":
    raise SystemExit(main())
