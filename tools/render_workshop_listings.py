#!/usr/bin/env python3
"""Render bilingual Workshop listings for an exactly selected stable GitHub release."""
import argparse
import re
from pathlib import Path

FIELDS = ("{{MOD_VERSION}}", "{{RELEASE_TAG}}")


def render(template: str, version: str) -> str:
    if re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", version) is None:
        raise ValueError("Expected stable X.Y.Z version")
    if any(template.count(field) < 1 for field in FIELDS):
        raise ValueError("Missing Workshop listing placeholder")
    result = template.replace(FIELDS[0], version).replace(FIELDS[1], "v" + version)
    if "{{" in result or "}}" in result:
        raise ValueError("Unresolved Workshop template placeholder")
    return result


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--version", required=True)
    p.add_argument("--templates", type=Path, default=Path("docs/workshop"))
    p.add_argument("--out", type=Path, required=True)
    args = p.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    for language in ("ja", "en"):
        name = f"listing-{language}.bbcode"
        text = (args.templates / name).read_text(encoding="utf-8")
        (args.out / name).write_text(render(text, args.version), encoding="utf-8")
    print(f"WORKSHOP_LISTINGS_OK version={args.version}")


if __name__ == "__main__":
    main()
