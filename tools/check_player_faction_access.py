#!/usr/bin/env python3
"""Keep game-side hooks from calling Faction.OfPlayer.

World generation makes the other factions and their leaders before the scenario adds the player faction, and our
Harmony hooks run then too (a leader's starting injuries are tended, records are counted). Faction.OfPlayer logs
"Could not find player faction." whenever there is no player faction, so every hook uses GameFacts.PlayerFaction or
GameFacts.IsPlayerFaction instead (found by the RimWorld-for-mod-test map generation run, 2026-11-17).
"""
from __future__ import annotations

import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
SOURCE = ROOT / "Source" / "RimworldStatistics"
CALL = re.compile(r"\bFaction\s*\.\s*OfPlayer\b(?!SilentFail)")


def main() -> int:
    found = []
    for path in sorted(SOURCE.rglob("*.cs")):
        for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
            if CALL.search(line.split("//", 1)[0]):
                found.append(f"{path.relative_to(ROOT)}:{number}: {line.strip()}")
    if found:
        print("Faction.OfPlayer logs an error while there is no player faction (world generation). "
              "Use GameFacts.PlayerFaction or GameFacts.IsPlayerFaction:", file=sys.stderr)
        print("\n".join(found), file=sys.stderr)
        return 1
    print("Player faction access: no Faction.OfPlayer in game-side sources; passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
