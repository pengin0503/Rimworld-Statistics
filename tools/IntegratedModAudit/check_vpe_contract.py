#!/usr/bin/env python3
"""Check the source contract of the pinned VPE 1.7 level and implant hooks."""
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(sys.argv[1])
metadata = ET.parse(root / "About/About.xml")
assert metadata.findtext("packageId", "").lower() == "vanillaexpanded.vpsycastse"
assert "1.7" in [node.text for node in metadata.findall("./supportedVersions/li")]
text = (root / "1.7/Source/VanillaPsycastsExpanded/Hediff_PsycastAbilities.cs").read_text(encoding="utf-8-sig")
assert re.search(r"class Hediff_PsycastAbilities\s*:\s*Hediff_Abilities\b", text)
assert re.search(r"public override void ChangeLevel\(int levelOffset\)\s*\{\s*base.ChangeLevel\(levelOffset\)", text)
assert re.search(r"public void ChangeLevel\(int levelOffset, bool sendLetter\)\s*\{\s*ChangeLevel\(levelOffset\)", text)
assert re.search(r"void InitializeFromPsylink\([^)]*\)\s*\{\s*this.psylink = psylink;\s*level = psylink.level;", text)
assert len(sys.argv) == 3, "vpe source root and pinned vef source root required"
extension = (root / "1.7/Source/VanillaPsycastsExpanded/AbilityExtension_Psycast.cs").read_text(encoding="utf-8-sig")
assert "public override void Cast(GlobalTargetInfo[] targets, Ability ability)" in extension
assert "base.Cast(targets, ability);" in extension
assert "psycastHediff.UseAbility(GetPsyfocusUsedByPawn(ability.pawn), GetEntropyUsedByPawn(ability.pawn));" in extension
assert extension.index("base.Cast(targets, ability)") < extension.index("psycastHediff.UseAbility(")
vef = pathlib.Path(sys.argv[2])
ability = (vef / "Source/VEF/Abilities/Misc/Ability.cs").read_text(encoding="utf-8-sig")
ext_base = (vef / "Source/VEF/Abilities/ModExtensions/AbilityExtension_AbilityMod.cs").read_text(encoding="utf-8-sig")
assert "public virtual void Cast(params GlobalTargetInfo[] targets)" in ability
assert "modExtension.Cast(targets, this);" in ability
assert "modExtension.Cast(targets.Any()" in ability
assert "PostCast(targets);" in ability
assert "this.Cast(new[] { target.ToGlobalTargetInfo(ability.Caster.Map) }, ability);" in ext_base
print("VPE 1.7 implant, level and consumed-cast contract: PASS")
