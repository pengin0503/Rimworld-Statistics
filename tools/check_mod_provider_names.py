#!/usr/bin/env python3
"""Every built-in MOD statistic names its provider without the MOD being loaded (#303).

The UI shows "MOD: <name>" next to a statistic of another MOD. When that MOD is not loaded (a profile or save from another
MOD set, or the MOD was removed), the name comes from the catalog in ModProviderNames.cs. This check reads the sources
of the built-in integrations, resolves the owner package ID of every statistic they declare, and fails when the catalog
has no name for one, or when a name is not on docs/supported-mods.md (the published list). It fails on an owner it
cannot resolve, so a new way of declaring statistics is noticed instead of skipped.
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCES = [p for p in (ROOT / "Source").rglob("*.cs") if "/obj/" not in p.as_posix() and "/bin/" not in p.as_posix()]
CATALOG = ROOT / "Source/RimworldStatistics.Core/Stats/ModProviderNames.cs"
SUPPORTED = ROOT / "docs/supported-mods.md"

# Helpers whose parameter becomes OwnerPackageId: (file name or None for any, call pattern, index of the owner argument).
OWNER_HELPERS = [
    (None, re.compile(r"\bOutcomeMetric\.Count\("), 0),
    ("VefProcessingStatistics.cs", re.compile(r"(?<![\w.])Descriptor\("), 1),   # Build's local function
]
# OwnerPackageId assignments that are not a declaration of a built-in statistic.
NOT_DECLARATIONS = {"owner", "OwnerPackageId", "api.OwnerPackageId"}
# Package IDs that are the game's own (DLCs): shown as "Royalty" etc., never as a MOD provider.
GAME_PACKAGES = re.compile(r"^ludeon\.rimworld(\..+)?$", re.IGNORECASE)

STRING = r'"((?:[^"\\]|\\.)*)"'
CONST = re.compile(r"\bconst\s+string\s+(\w+)\s*=\s*(" + STRING + r"|[\w.]+)\s*;")
CLASS = re.compile(r"\b(?:class|struct|record)\s+(\w+)")


def strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", " ", text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


files = {p: strip_comments(p.read_text(encoding="utf-8")) for p in SOURCES}

# Constants per file and per class name (a class's constants are those declared after it, until the next class).
by_file: dict[pathlib.Path, dict[str, str]] = {}
by_class: dict[str, dict[str, str]] = {}
raw: list[tuple[pathlib.Path, str | None, str, str]] = []
for path, text in files.items():
    marks = [(m.start(), m.group(1)) for m in CLASS.finditer(text)]
    for m in CONST.finditer(text):
        owner_class = None
        for start, name in marks:
            if start < m.start():
                owner_class = name
        raw.append((path, owner_class, m.group(1), m.group(2)))


def literal(expr: str) -> str | None:
    m = re.fullmatch(STRING, expr.strip())
    return m.group(1) if m else None


# Resolve constants that refer to other constants (a few passes are enough for the chains used).
for _ in range(4):
    for path, cls, name, expr in raw:
        value = literal(expr)
        if value is None:
            parts = expr.strip().split(".")
            if len(parts) == 1:
                value = by_file.get(path, {}).get(parts[0])
            elif len(parts) == 2:
                value = by_class.get(parts[0], {}).get(parts[1])
        if value is not None:
            by_file.setdefault(path, {})[name] = value
            if cls:
                by_class.setdefault(cls, {})[name] = value


def resolve(path: pathlib.Path, expr: str) -> str | None:
    expr = re.sub(r"^\w+\s*:\s*", "", expr.strip())   # a named argument
    if (value := literal(expr)) is not None:
        return value
    parts = expr.split(".")
    if len(parts) == 1:
        return by_file.get(path, {}).get(expr)
    if len(parts) == 2:
        return by_class.get(parts[0], {}).get(parts[1])
    return None


def arguments(text: str, open_paren: int) -> list[str]:
    """The top-level arguments of the call whose '(' is at open_paren."""
    depth, current, args, i = 0, [], [], open_paren
    while i < len(text):
        c = text[i]
        if c == '"':
            end = i + 1
            while end < len(text) and text[end] != '"':
                end += 2 if text[end] == "\\" else 1
            current.append(text[i:end + 1])
            i = end + 1
            continue
        if c in "([{":
            depth += 1
            if depth > 1:
                current.append(c)
        elif c in ")]}":
            depth -= 1
            if depth == 0:
                args.append("".join(current))
                return args
            current.append(c)
        elif c == "," and depth == 1:
            args.append("".join(current))
            current = []
        else:
            current.append(c)
        i += 1
    return args


owners: dict[str, set[str]] = {}
problems: list[str] = []
for path, text in files.items():
    rel = path.relative_to(ROOT).as_posix()
    for m in re.finditer(r"\bOwnerPackageId\s*=\s*([^,;}\n]+)", text):
        expr = m.group(1).strip()
        if expr in NOT_DECLARATIONS or expr.startswith("string.IsNullOrWhiteSpace"):
            continue
        value = resolve(path, expr)
        if value is None:
            problems.append(f"{rel}: cannot resolve OwnerPackageId = {expr}")
        else:
            owners.setdefault(value, set()).add(rel)
    for only, pattern, index in OWNER_HELPERS:
        if only is not None and path.name != only:
            continue
        for m in pattern.finditer(text):
            if re.search(r"\b(?:static|internal|private|public)\b[^;{]*$", text[max(0, m.start() - 200):m.start()].split("\n")[-1]):
                continue   # the helper's own declaration
            args = arguments(text, m.end() - 1)
            if len(args) <= index:
                continue
            value = resolve(path, args[index])
            if value is None:
                problems.append(f"{rel}: cannot resolve the owner argument {args[index].strip()!r}")
            else:
                owners.setdefault(value, set()).add(rel)

catalog_text = strip_comments(CATALOG.read_text(encoding="utf-8"))
catalog = {k.casefold(): v for k, v in re.findall(r'\["([^"]+)"\]\s*=\s*"([^"]+)"', catalog_text)}
exceptions_match = re.search(r"NotOnSupportedList\s*\{[^}]*\}\s*=\s*\[([^\]]*)\]", catalog_text)
if exceptions_match is None:
    sys.exit(f"cannot read NotOnSupportedList in {CATALOG.relative_to(ROOT)}")
exceptions = {s.casefold() for s in re.findall(STRING, exceptions_match.group(1))}


def normalised(name: str) -> str:
    return re.sub(r"\s+", " ", name.replace("–", "-").replace("—", "-")).strip().casefold()


supported = {normalised(line[2:]) for line in SUPPORTED.read_text(encoding="utf-8").splitlines() if line.startswith("- ")}

checked = 0
for package, where in sorted(owners.items(), key=lambda kv: kv[0].casefold()):
    if GAME_PACKAGES.match(package):
        continue
    checked += 1
    name = catalog.get(package.casefold())
    if name is None:
        problems.append(f"{package} (from {', '.join(sorted(where))}) has no name in {CATALOG.relative_to(ROOT)}")
    elif normalised(name) not in supported and package.casefold() not in exceptions:
        problems.append(f"{package}: catalog name {name!r} is not listed in {SUPPORTED.relative_to(ROOT)}")

# The other direction: every catalog entry is a built-in owner, and only names off the list are marked as exceptions.
for key, name in sorted(catalog.items()):
    if key not in {p.casefold() for p in owners}:
        problems.append(f"{CATALOG.relative_to(ROOT)}: {key} ({name}) owns no built-in statistic; remove it")
    if key in exceptions and normalised(name) in supported:
        problems.append(f"{CATALOG.relative_to(ROOT)}: {key} is marked as not on the list, but {name!r} is listed")
for key in sorted(exceptions - set(catalog)):
    problems.append(f"{CATALOG.relative_to(ROOT)}: NotOnSupportedList names {key}, which has no catalog name")

if problems:
    print("\n".join(problems), file=sys.stderr)
    sys.exit(1)
print(f"MOD provider names: {checked} owner packages of built-in statistics resolved and named; passed")
