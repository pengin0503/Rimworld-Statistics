#!/usr/bin/env python3
"""Validate pinned 1.7 Saurid / Phytokin folder, package and source semantics.

The separate Mono.Cecil step validates actual executable DLL metadata and IL.
No game process is started and no upstream repository is modified.
"""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

if len(sys.argv) != 3:
    raise SystemExit("Usage: check_race_birth_contract.py <Saurid checkout> <Phytokin checkout>")


def audit(root, package, assembly, source, class_name):
    meta = ET.parse(root / "About/About.xml").getroot()
    assert meta.findtext("packageId", "").casefold() == package, f"{package}: package ID changed"
    assert "1.7" in [x.text for x in meta.findall("./supportedVersions/li")], f"{package}: no 1.7 support"
    assert "ludeon.rimworld.biotech" in [
        x.text.casefold() for x in meta.findall("./modDependencies/li/packageId") if x.text
    ], f"{package}: expected Biotech dependency missing"
    folders = ET.parse(root / "loadFolders.xml").getroot()
    selected = [x.text.strip("/") for x in folders.findall("./v1.7/li") if x.text]
    assert "1.6" in selected, f"{package}: 1.7 no longer selects 1.6"
    dll = root / "1.6/Assemblies" / assembly
    assert dll.is_file() and dll.stat().st_size > 1000, f"{package}: selected 1.6 DLL missing"
    code = (root / source).read_text(encoding="utf-8-sig")
    assert re.search(r"class\s+" + re.escape(class_name) + r"\s*:", code)
    assert "public void Hatch()" in code
    assert "PawnUtility.TrySpawnHatchedOrBornPawn" in code
    return code


saurid = audit(
    Path(sys.argv[1]), "vanillaracesexpanded.saurid", "VRESaurids.dll",
    "1.6/Source/VRESaurids/Comp_HumanHatcher.cs", "Comp_HumanHatcher")
assert "if (PawnUtility.TrySpawnHatchedOrBornPawn(hatchee, parent))" in saurid
assert saurid.index("if (PawnUtility.TrySpawnHatchedOrBornPawn(hatchee, parent))") < saurid.index("hatched = true;")
phytokin = audit(
    Path(sys.argv[2]), "vanillaracesexpanded.phytokin", "VanillaRacesExpanded-Phytokin.dll",
    "1.6/Source/VanillaRacesExpanded-Phytokin/VanillaRacesExpanded-Phytokin/Building/Building_SaplingChild.cs",
    "Building_SaplingChild")
assert "if (PawnUtility.TrySpawnHatchedOrBornPawn(pawn, this))" in phytokin
assert phytokin.index("if (PawnUtility.TrySpawnHatchedOrBornPawn(pawn, this))") < phytokin.index("pawn.genes.SetXenotype(")
assert re.search(r"finally\s*\{\s*successfulBirth\s*=\s*true;", phytokin)
assert "successfulBirth" not in (Path("Source/RimworldStatistics/IntegratedMods/PopularModOutcomes/Source/RaceBirthOutcomes.cs")
    .read_text(encoding="utf-8").split("private static void AfterPhytokin")[1].split("private static bool OnOriginalPlayerHome")[0]
    .replace("// successfulBirth is set in an upstream finally block even on failure.", ""))
# The compiled MODs call the three-argument overload (third value is optional in
# C# source but baked into the IL). Keep the replacement call stack-compatible.
hook = Path("Source/RimworldStatistics/IntegratedMods/PopularModOutcomes/Source/RaceBirthOutcomes.cs").read_text(encoding="utf-8")
assert "[typeof(Pawn), typeof(Thing), typeof(IntVec3?)]" in hook
assert "TrySpawnVerified(Pawn pawn, Thing source, IntVec3? positionOverride)" in hook
assert "TrySpawnHatchedOrBornPawn(pawn, source, positionOverride)" in hook
print("Selected 1.7 Saurid and Phytokin source, dependencies, loadFolders and hatch order: PASS")
