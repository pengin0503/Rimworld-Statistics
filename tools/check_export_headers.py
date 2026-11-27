#!/usr/bin/env python3
"""Check the CSV export headers against their golden contract and docs/csv-export.md.

Q5/Q9/Q10/Q12: the export header lists are the column contract for external tools. This script freezes them, verifies
the C# writer and both language sections of the CSV documentation still agree, and keeps column names machine-friendly.
"""
from __future__ import annotations

import re
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / "Source/RimworldStatistics.Core/Export/StatisticsExport.cs"
DOCS = ROOT / "docs/csv-export.md"

GOLDEN: dict[str, list[str]] = {
    "summary.csv": [
        "scope", "metric_key", "breakdown", "label", "category", "unit", "aggregation", "value", "value_display",
        "coverage", "evidence", "availability", "verification", "provenance", "acquisition_unit", "owner_mod",
        "colonies_measured", "colonies_target", "holder_colony_key", "holder_colony_name", "since_day", "through_day",
    ],
    "colony_metrics.csv": [
        "colony_key", "colony_name", "metric_key", "breakdown", "label", "unit", "value", "coverage", "evidence",
        "since_day", "through_day",
    ],
    "colonies.csv": [
        "colony_key", "name", "world", "scenario", "storyteller", "difficulty", "permadeath", "excluded", "in_scope",
        "days", "real_play_hours", "peak_wealth", "last_wealth", "raids", "threats", "killed", "departed",
        "max_population", "endings", "saves", "last_played_utc", "game_versions", "faction_name",
    ],
    "rankings.csv": [
        "metric_key", "label", "direction", "rank", "colony_key", "colony_name", "value", "coverage", "evidence",
    ],
    "endings.csv": [
        "time_utc", "colony_key", "colony_name", "ending_type", "ordinal", "departed", "source",
    ],
    "colony_metric_history.csv": [
        "colony_key", "colony_name", "metric_key", "label", "unit", "history_kind", "point_index", "day", "tick",
        "value", "coverage", "evidence", "predecessor_index", "unlinked",
    ],
}

COLUMN = re.compile(r"^[a-z][a-z0-9_]*$")


def code_headers(source: str) -> dict[str, list[str]]:
    headers: dict[str, list[str]] = {}
    write = re.compile(r'WriteCsv\("([^"]+\.csv)",\s*\[(.*?)\],\s*add\s*=>', re.S)
    for match in write.finditer(source):
        headers[match.group(1)] = re.findall(r'"([^"]+)"', match.group(2))
    summary = re.search(r"new ExportTable\(\s*\[(.*?)\],\s*rows\.ToArray\(\)\)", source, re.S)
    if summary:
        headers["summary.csv"] = re.findall(r'"([^"]+)"', summary.group(1))
    return headers


LANGUAGE_SECTIONS = ("ja", "en")


def documented_headers(docs: str) -> dict[tuple[str, str], list[list[str]]]:
    """Map (language section, csv name) to its documented column lists.

    Both the English ``Columns:`` marker and the Japanese ``列：`` marker are accepted. Rows are keyed by the
    heading they appear under so the caller can require the contract in *both* language sections.
    """
    headers: dict[tuple[str, str], list[list[str]]] = {}
    section: str | None = None
    for line in docs.splitlines():
        if line.startswith("## "):
            title = line[3:].strip().lower()
            section = "ja" if "日本語" in title or "japanese" in title else "en" if "english" in title else None
            continue
        if section is None:
            continue
        row = re.match(r"\| `([^`]+\.csv)` \|", line)
        if not row:
            continue
        columns = None
        for marker in ("Columns:", "列："):
            if marker in line:
                columns = line.split(marker, 1)[1].split("<br>", 1)[0]
                break
        if columns is not None:
            headers.setdefault((section, row.group(1)), []).append(re.findall(r"`([^`]+)`", columns))
    return headers


def main() -> int:
    source = SOURCE.read_text(encoding="utf-8")
    docs = DOCS.read_text(encoding="utf-8")
    actual = code_headers(source)
    documented = documented_headers(docs)

    problems: list[str] = []
    for name, expected in GOLDEN.items():
        found = actual.get(name)
        if found is None:
            problems.append(f"{name}: header not found in StatisticsExport.cs")
        elif found != expected:
            problems.append(f"{name}: StatisticsExport.cs header differs from the golden contract:\n"
                            f"  expected: {expected}\n  found:    {found}")

        for section in LANGUAGE_SECTIONS:
            occurrences = documented.get((section, name), [])
            if not occurrences:
                problems.append(f"{name}: header not found in the {section} section of docs/csv-export.md")
            for documented_columns in occurrences:
                if documented_columns != expected:
                    problems.append(f"{name}: docs/csv-export.md ({section}) header differs from the golden contract:\n"
                                    f"  expected: {expected}\n  found:    {documented_columns}")
        if len(set(expected)) != len(expected):
            problems.append(f"{name}: the golden contract has duplicate columns")

    for name, found in actual.items():
        if name not in GOLDEN:
            problems.append(f"{name}: unexpected CSV file with no golden header")
        for column in found:
            if not COLUMN.match(column):
                problems.append(f"{name}: column {column!r} is not lower_snake_case")
        if "metric_key" not in found and name in {"summary.csv", "colony_metrics.csv", "rankings.csv",
                                                  "colony_metric_history.csv"}:
            problems.append(f"{name}: missing metric_key column")
    for (section, name), occurrences in documented.items():
        if name not in GOLDEN:
            problems.append(f"{name}: documented CSV file has no golden header")
        elif len(occurrences) > 1:
            problems.append(f"{name}: documented {len(occurrences)} times in the {section} section of docs/csv-export.md")

    if problems:
        print("CSV export header contract failed:", file=sys.stderr)
        for problem in problems:
            print(f"  - {problem}", file=sys.stderr)
        return 1

    total = sum(len(columns) for columns in GOLDEN.values())
    print(f"CSV export headers: {len(GOLDEN)} files / {total} columns match the golden contract and both "
          f"language sections of docs/csv-export.md")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
