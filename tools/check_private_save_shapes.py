#!/usr/bin/env python3
"""Read-only, privacy-safe structure checks for two user-provided RimWorld saves.

This standard-library Python probe validates original XML structure and compressed
history points; it is independent of, and does not replace, the C# SaveReader test.
It does not print file names, colony names, MOD IDs or save hashes.
"""
import argparse
import base64
import hashlib
import os
import time
import zlib
from xml.parsers import expat

SHAPES = {
    "installed": ("1.7.5000 rev1275", 183, 8_037_368, 12, 268, 59, True),
    "legacy": ("1.7.4995 rev309", 348, 12_818_036, 16, 428, 102, False),
}
SUPPORTED = {
    "Wealth_Total", "Wealth_Items", "Wealth_Buildings", "Wealth_Pawns",
    "FreeColonists", "Prisoners", "ColonistMood",
}
BASE = ("savegame", "game", "history", "autoRecorderGroups", "li", "recorders", "li")
COMPONENT = ("savegame", "game", "components", "li")
FIELDS = {
    ("savegame", "meta", "gameVersion"): "version",
    ("savegame", "game", "tickManager", "ticksGame"): "ticks",
    ("savegame", "game", "storyWatcher", "statsRecord", "numRaidsEnemy"): "raids",
}


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as src:
        for block in iter(lambda: src.read(1024 * 1024), b""):
            h.update(block)
    return h.digest()


def inspect(path):
    state = {
        "mod_count": 0, "quest_count": 0,
        "has_component": False, "has_checkpoint": False, "recorder_lengths": {},
    }
    stack = []
    capture = None
    pieces = []
    record = None
    in_stats_component = False

    def start(name, attrs):
        nonlocal capture, pieces, record, in_stats_component
        stack.append(name)
        p = tuple(stack)
        if len(stack) == 1 and name != "savegame":
            raise ValueError("XML root is not savegame")
        if p == ("savegame", "meta", "modIds", "li"):
            state["mod_count"] += 1
        if p == ("savegame", "game", "questManager", "quests", "li"):
            state["quest_count"] += 1
        if p == COMPONENT:
            in_stats_component = attrs.get("Class") == "RimworldStatistics.GameComponent_RimworldStatistics"
            if in_stats_component:
                state["has_component"] = True
        if p == COMPONENT + ("numericStatistics",) and in_stats_component:
            state["has_checkpoint"] = True
        if p == BASE:
            record = {"def": None, "records": None, "recordsDeflate": None}
        if p in FIELDS:
            capture = FIELDS[p]
            pieces = []
        elif p[:-1] == BASE and p[-1] in ("def", "records", "recordsDeflate"):
            capture = "rec:" + p[-1]
            pieces = []

    def chars(value):
        if capture is not None:
            pieces.append(value)

    def end(_name):
        nonlocal capture, pieces, record, in_stats_component
        p = tuple(stack)
        if capture is not None and (
            (capture.startswith("rec:") and p == BASE + (capture[4:],))
            or FIELDS.get(p) == capture
        ):
            value = "".join(pieces).strip()
            if capture.startswith("rec:"):
                record[capture[4:]] = value
            else:
                state[capture] = value
            capture = None
            pieces = []
        if p == BASE:
            rec_name = record.get("def")
            if rec_name in SUPPORTED:
                label = "recordsDeflate" if record.get("recordsDeflate") is not None else "records"
                encoded = record.get(label)
                if encoded is None or len(encoded) > 2_097_152:
                    raise ValueError("missing or oversized recorder " + rec_name)
                payload = base64.b64decode("".join(encoded.split()), validate=True)
                if label == "recordsDeflate":
                    d = zlib.decompressobj(-zlib.MAX_WBITS)
                    payload = d.decompress(payload, 262144 * 4 + 1)
                    if not d.eof or len(payload) > 262144 * 4:
                        raise ValueError("incomplete or oversized history " + rec_name)
                if len(payload) % 4:
                    raise ValueError("invalid float32 length for " + rec_name)
                if rec_name in state["recorder_lengths"]:
                    raise ValueError("duplicate recorder " + rec_name)
                state["recorder_lengths"][rec_name] = len(payload) // 4
            record = None
        if p == COMPONENT:
            in_stats_component = False
        stack.pop()

    parser = expat.ParserCreate()
    parser.StartElementHandler = start
    parser.EndElementHandler = end
    parser.CharacterDataHandler = chars
    parser.ExternalEntityRefHandler = lambda *_: 0
    with open(path, "rb") as src:
        for chunk in iter(lambda: src.read(1024 * 1024), b""):
            parser.Parse(chunk, False)
    parser.Parse(b"", True)
    return state


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--installed", required=True, help="RimWorld 1.7.5000 save with Statistics component")
    parser.add_argument("--legacy", required=True, help="RimWorld 1.7.4995 save without Statistics component")
    args = parser.parse_args()
    for label in ("installed", "legacy"):
        filename = getattr(args, label)
        before = sha256_file(filename)
        start = time.perf_counter()
        actual = inspect(filename)
        elapsed = time.perf_counter() - start
        version, mods, ticks, raids, points, quests, component = SHAPES[label]
        expected = (version, mods, ticks, raids, quests, component)
        observed = (
            actual.get("version"), actual["mod_count"], int(actual["ticks"]),
            int(actual["raids"]), actual["quest_count"], actual["has_component"],
        )
        if observed != expected:
            raise AssertionError(label + ": metadata does not match expected save shape")
        if actual["has_checkpoint"]:
            raise AssertionError(label + ": unexpected numeric checkpoint")
        if set(actual["recorder_lengths"]) != SUPPORTED:
            raise AssertionError(label + ": expected seven recorders")
        if any(n != points for n in actual["recorder_lengths"].values()):
            raise AssertionError(label + ": recorder lengths do not match")
        if points != ticks // 30_000 + 1:
            raise AssertionError(label + ": unexpected recorder time alignment")
        if before != sha256_file(filename):
            raise AssertionError(label + ": original file changed")
        print(f"{label}: PASS; {mods} MODs; 7 x {points} points; "
              f"{os.path.getsize(filename) / 1048576:.2f} MiB; "
              f"XML parse {elapsed:.2f}s; unchanged")
    print("No names, original record data, identifiers or file hashes disclosed.")


if __name__ == "__main__":
    main()
