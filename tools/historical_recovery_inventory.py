#!/usr/bin/env python3
"""Regenerate the conservative, source-derived 266-output historical recovery inventory.

This is a capability *triage*, not a confirmation that historical values can be
reconstructed. It never touches .rws files, profiles or RimWorld. Run:
    python3 tools/historical_recovery_inventory.py --check
    python3 tools/historical_recovery_inventory.py --write
"""
from __future__ import annotations

import argparse
import csv
import io
import re
from pathlib import Path

CATALOGS = (
    ("live", "LiveCatalog.cs", "Live", "EVENT_LOG_REQUIRED"),
    ("snapshot", "SnapshotCatalog.cs", "Snap", "SAVED_STATE_CANDIDATE"),
    ("derived", "RankingDependencyCatalog.cs", "Rank", "DEPENDS_ON_INPUT"),
)
COLUMNS = (
    "metric_key", "acquisition_id", "catalog", "recovery_decision",
    "evidence", "ledger_id", "source_symbol",
)
EXPECTED_COUNTS = {"live": 216, "snapshot": 41, "derived": 9}
SYMBOL = re.compile(r"^([A-Za-z_]\w*)\.([A-Za-z_]\w*)$")
CONST = re.compile(r'\b(?:public|private|internal)\s+const\s+string\s+(\w+)\s*=\s*"([^"]+)"\s*;')


def _arguments(text: str, ctor: str) -> list[list[str]]:
    """Find only declarations beginning with the constructor name, not helper calls."""
    out: list[list[str]] = []
    pattern = re.compile(r"(?m)^\s*" + re.escape(ctor) + r"\(")
    for match in pattern.finditer(text):
        start = match.end()
        depth = 1
        quote = False
        escaped = False
        args: list[str] = []
        arg_start = start
        for i in range(start, len(text)):
            ch = text[i]
            if quote:
                if escaped:
                    escaped = False
                elif ch == "\\":
                    escaped = True
                elif ch == '"':
                    quote = False
                continue
            if ch == '"':
                quote = True
            elif ch in "([{":
                depth += 1
            elif ch in ")]}":
                depth -= 1
                if depth == 0:
                    args.append(text[arg_start:i].strip())
                    break
            elif ch == "," and depth == 1:
                args.append(text[arg_start:i].strip())
                arg_start = i + 1
        else:
            raise ValueError(f"Unterminated {ctor} registry declaration")
        out.append(args)
    return out


def _symbol_values(stats_dir: Path) -> dict[str, str]:
    """C# constant values, resolved only from their actual class-named source files."""
    values: dict[str, str] = {}
    for path in stats_dir.rglob("*.cs"):
        stem = path.stem
        for match in CONST.finditer(path.read_text(encoding="utf-8")):
            symbol = f"{stem}.{match.group(1)}"
            if symbol in values and values[symbol] != match.group(2):
                raise ValueError(f"Conflicting constant: {symbol}")
            values[symbol] = match.group(2)
    return values


def _resolve(expression: str, values: dict[str, str]) -> str:
    expression = expression.strip()
    if expression.startswith('"') and expression.endswith('"'):
        return expression[1:-1]
    if not SYMBOL.fullmatch(expression):
        raise ValueError(f"Unresolved C# expression: {expression}")
    if expression not in values:
        raise ValueError(f"Unknown C# registry constant: {expression}")
    return values[expression]


def build_inventory(root: Path) -> list[dict[str, str]]:
    stats = root / "Source/RimworldStatistics.Core/Stats"
    symbols = _symbol_values(stats)
    rows: list[dict[str, str]] = []
    for catalog, source, ctor, decision in CATALOGS:
        for arguments in _arguments((stats / source).read_text(encoding="utf-8"), ctor):
            if len(arguments) < 6:
                raise ValueError(f"Invalid {ctor} registry entry: {arguments!r}")
            key = _resolve(arguments[0], symbols)
            ledger = _resolve(arguments[1], symbols)
            # Catalog-specific stable acquisition position in each helper signature.
            acquisition_arg = arguments[5] if catalog == "live" else arguments[4]
            acquisition = _resolve(acquisition_arg, symbols)
            if not key or not ledger or not acquisition:
                raise ValueError(f"Empty registry identity for {arguments[0]}")
            rows.append({
                "metric_key": key,
                "acquisition_id": acquisition,
                "catalog": catalog,
                "recovery_decision": decision,
                "evidence": "UNVERIFIED",
                "ledger_id": ledger,
                "source_symbol": arguments[0],
            })
    counts = {name: sum(row["catalog"] == name for row in rows) for name in EXPECTED_COUNTS}
    if counts != EXPECTED_COUNTS:
        raise ValueError(f"Catalog count drift: {counts} vs {EXPECTED_COUNTS}")
    keys = [row["metric_key"] for row in rows]
    if len(keys) != len(set(keys)):
        duplicate = sorted(k for k in set(keys) if keys.count(k) > 1)
        raise ValueError(f"Duplicate metric keys: {duplicate}")
    return rows


def csv_text(rows: list[dict[str, str]]) -> str:
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=COLUMNS, lineterminator="\n")
    writer.writeheader()
    writer.writerows(rows)
    return output.getvalue()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    actions = parser.add_mutually_exclusive_group(required=True)
    actions.add_argument("--write", action="store_true")
    actions.add_argument("--check", action="store_true")
    opts = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    path = root / "docs/research/2026-11-09-historical-recovery-inventory.csv"
    body = csv_text(build_inventory(root))
    if opts.write:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(body, encoding="utf-8")
        print(f"wrote {path} (266 entries)")
        return 0
    if not path.exists() or path.read_text(encoding="utf-8") != body:
        print(f"Inventory is stale or missing. Run: python3 tools/historical_recovery_inventory.py --write")
        return 1
    print("Historical recovery inventory: 266 source-backed entries, all UNVERIFIED")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
