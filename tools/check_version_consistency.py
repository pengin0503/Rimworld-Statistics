#!/usr/bin/env python3
"""Check that README.md and About/About.xml advertise the same MOD version.

Q5/Q9/Q10/Q12: publish.yml only checks the release tag against About.xml at release time. This PR-time check catches a
README/About.xml drift before a reviewer or a release does.
"""
from __future__ import annotations

import re
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent
ABOUT = ROOT / "About/About.xml"
README = ROOT / "README.md"
VERSION = r"([0-9]+(?:\.[0-9]+){1,3}(?:[-+][0-9A-Za-z.-]+)?)"
MARKERS = (
    re.compile(r"\*\*Mod Version\*\*\s*:\s*" + VERSION),
    re.compile(r"\*\*MODバージョン\*\*\s*[:：]\s*" + VERSION),
)


def main() -> int:
    try:
        about = ET.parse(ABOUT).getroot().findtext("modVersion")
    except (ET.ParseError, OSError) as error:
        print(f"could not read {ABOUT.relative_to(ROOT)}: {error}", file=sys.stderr)
        return 1
    if not about:
        print(f"{ABOUT.relative_to(ROOT)} has no modVersion", file=sys.stderr)
        return 1

    readme = README.read_text(encoding="utf-8")
    found = [match.group(1) for marker in MARKERS for match in marker.finditer(readme)]
    if not found:
        print("README.md has no '**Mod Version**: X.Y.Z' / '**MODバージョン**：X.Y.Z' marker", file=sys.stderr)
        return 1

    mismatched = sorted({version for version in found if version != about})
    if mismatched:
        print(f"About/About.xml is {about}, but README.md advertises {', '.join(mismatched)}", file=sys.stderr)
        return 1

    print(f"MOD version: About/About.xml = README.md = {about} ({len(found)} README marker(s))")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
