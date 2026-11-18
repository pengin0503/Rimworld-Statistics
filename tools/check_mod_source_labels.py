#!/usr/bin/env python3
"""Prevent source-mod branding from leaking back into localized statistic titles."""
from __future__ import annotations

import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
# Descriptions and acquisition-group labels may legitimately name the source mod.
# Restrict this guard to UI statistic title keys, not explanatory text.
BRANDS_BY_PREFIX = {
    "RWS_ModHospitality_": ("Hospitality",),
    "RWS_Hospitality_": ("Hospitality",),
    "RWS_DBH_": ("Dubs Bad Hygiene",),
    "RWS_Metric_Dbh": ("Dubs Bad Hygiene",),
    "RWS_Metric_Vpe": ("VPE",),
    "RWS_SAU_": ("Smart Apparel Upgrade",),
    "RWS_VfeSecurity_": ("VFE Security",),
    "RWS_VfeTribals_": ("VFE Tribals",),
    "RWS_VfeInsectoids_": ("VFE Insectoids 2",),
    "RWS_VfeDeserters_": ("VFE Deserters", "Deserters"),
    "RWS_VfeClassical_": ("VFE Classical", "Classical", "クラシカル"),
    "RWS_VfeEmpire_": ("VFE Empire",),
    "RWS_VfeMedieval2_": ("VFE Medieval 2",),
    "RWS_VfeFactory_": ("VFE Factory",),
    "RWS_VfePirates_": ("VFE Pirates",),
    "RWS_VfeSettlers_": ("VFE Settlers",),
    "RWS_RimFactory_": ("RimFactory",),
    "RWS_Rwom_": ("RimWorld of Magic",),
    "RWS_Sos2_": ("Save Our Ship 2",),
    "RWS_CombatExtended_": ("Combat Extended",),
    "RWS_CE_": ("Combat Extended",),
    "RWS_Gastronomy_": ("Gastronomy",),
    "RWS_Rimatomics": ("Dubs Rimatomics", "Rimatomics"),
    "RWS_Rimefeller_": ("Rimefeller",),
    "RWS_VaeAspirations_": ("Vanilla Aspirations Expanded",),
    "RWS_SimpleWarrants_": ("Simple Warrants",),
    "RWS_VefCooking_": ("Vanilla Cooking Expanded",),
    "RWS_VefBrewing_": ("Vanilla Brewing Expanded",),
    "RWS_VceDeepchem_": ("Vanilla Chemfuel Expanded",),
    "RWS_VgeExtraction_": ("Vanilla Genetics Expanded",),
    "RWS_VgeGravdata": ("Vanilla Gravship Expanded",),
    "RWS_AddonRecruitment_": ("Recruitment",),
    "RWS_PrisonExpanded_": ("Prison Expanded",),
    "RWS_HospitalCured": ("Adamas",),
    "RWS_HospitalTreatment": ("Adamas",),
    "RWS_AlteredCarbon": ("Altered Carbon", "ReSleeved"),
    "RWS_EmpireTax": ("Empire Refactored",),
    "RWS_AlphaGenes": ("Alpha Genes",),
    "RWS_AlphaMechsBoss": ("Alpha Mechs",),
    "RWS_WorldDominationConquests": ("World Domination",),
    "RWS_WorldDominationPlayer": ("World Domination",),
    "RWS_PrisonLabor": ("Prison Labor",),
    "RWS_MedPod": ("MedPod",),
    "RWS_AlphaImplants": ("Alpha Implants",),
    "RWS_BiomesIslands_": ("Biomes! Islands", "Biomes Islands"),
    "RWS_RimQuest": ("RimQuest",),
    "RWS_Horrors": ("Horrors",),
}
MOD_LABEL = re.compile(r"^MOD[:：]\s*\{0\}$")
issues: list[str] = []
count = 0
for source in sorted((ROOT / "Languages").glob("*/Keyed/*.xml")):
    try:
        root = ET.parse(source).getroot()
    except ET.ParseError as exc:
        issues.append(f"{source}: invalid XML: {exc}")
        continue
    if source.name == "RimworldStatistics.xml":
        provider = root.find("RWS_Mod_Provider")
        if provider is None or not MOD_LABEL.fullmatch(provider.text or ""):
            issues.append(f"{source}: RWS_Mod_Provider must be MOD: {{0}} or MOD：{{0}}")
    for item in root:
        key = item.tag
        if key.endswith("Desc") or key.startswith("RWS_MetricDesc"):
            continue
        for prefix, names in BRANDS_BY_PREFIX.items():
            if not key.startswith(prefix):
                continue
            value = (item.text or "").casefold()
            for name in names:
                if name.casefold() in value:
                    issues.append(f"{source}: {key} still contains source name {name!r}")
            count += 1
            break

if issues:
    print("\n".join(issues), file=sys.stderr)
    sys.exit(1)
print(f"MOD source/title separation: {count} localized metric titles checked; passed")
