#!/usr/bin/env python3
"""Guard the one-Statistics-MOD packaging contract and stable provider IDs.

This checks the fully integrated source; it never loads RimWorld or third-party DLLs.
The Core tests verify persisted events and snapshots separately.
"""
import re
from pathlib import Path
import xml.etree.ElementTree as ET

root = Path("Source/RimworldStatistics")
integrated = root / "IntegratedMods"
assert integrated.is_dir(), "Missing integrated source tree"

expected = {
    "mod.orion.hospitality.visitors.guests",
    "mod.orion.hospitality.visitors.groups",
    "mod.orion.hospitality.visitors.assigned_guest_beds",
    "mod.orion.hospitality.visits.groups_arrived",
    "mod.orion.hospitality.visits.guests_arrived",
    "mod.orion.hospitality.visits.completed",
    "mod.orion.hospitality.recruits.completed",
    "mod.orion.hospitality.recruits.forced",
    "mod.orion.hospitality.recruits.voluntary",
    "mod.pengin0503.recruitment.queue.active",
    "mod.pengin0503.recruitment.requestsaddon.started",
    "mod.pengin0503.recruitment.requestsaddon.silver_spent",
    "mod.pengin0503.recruitment.requestsaddon.applicants_planned",
    "mod.dubwise.dubsbadhygiene.water.piped_stored",
    "mod.dubwise.dubsbadhygiene.water.piped_capacity",
    "mod.dubwise.dubsbadhygiene.water.pump_capacity_per_day",
    "mod.dubwise.dubsbadhygiene.water.groundwater_capacity_per_day",
    "mod.pengin0503.smartapparelupgrade.orders.issued",
    "mod.vanillaexpanded.vanillatradingexpanded.bank.credited_silver",
    "mod.vanillaexpanded.vanillatradingexpanded.bank.debited_silver",
    "mod.vanillaexpanded.vanillatradingexpanded.bank.loans_issued",
    "mod.vanillaexpanded.vanillatradingexpanded.bank.loan_principal",
    "mod.vanillaexpanded.vanillatradingexpanded.contract.deliveries",
    "mod.vanillaexpanded.vanillatradingexpanded.contract.items_delivered",
    "mod.vanillaexpanded.vanillatradingexpanded.contract.purchases",
    "mod.vanillaexpanded.vanillatradingexpanded.contract.items_received",
}

def source(relative):
    return (integrated / relative).read_text(encoding="utf-8")

hosp = source("Hospitality/Source/HospitalitySdk.cs")
recruitment = source("Recruitment/Source/RecruitmentSdk.cs")
dbh = source("DubsBadHygiene/Source/DbhSdk.cs")
sau = source("SmartApparelUpgrade/Source/SauHostClient.cs")
vte_bank = source("VanillaTradingExpanded/Source/VteBankStatistics.cs")
vte_contract = source("VanillaTradingExpanded/Source/VteContractStatistics.cs")
observed = set()
observed |= {"mod.orion.hospitality." + family + "." + metric
             for family, metric in re.findall(r'Metric\("([a-z_]+)",\s*"([a-z_]+)"', hosp)}
observed |= {"mod.pengin0503.recruitment." + family + "." + metric
             for family, metric in re.findall(r'Definition\("([a-z_]+)",\s*"([a-z_]+)"', recruitment)}
observed |= {"mod.dubwise.dubsbadhygiene.water." + name
             for name in re.findall(r'MetricName\s*=\s*"([a-z_]+)"', dbh)}
observed |= {"mod.pengin0503.smartapparelupgrade." +
             re.search(r'FamilyId\s*=\s*"([a-z_]+)"', sau).group(1) + "." +
             re.search(r'MetricName\s*=\s*"([a-z_]+)"', sau).group(1)}
observed |= {"mod.vanillaexpanded.vanillatradingexpanded.bank." + name
             for name in re.findall(r'\bDef\("([a-z_]+)"', vte_bank)}
observed |= {"mod.vanillaexpanded.vanillatradingexpanded.contract." + name
             for name in re.findall(r'\bDef\("([a-z_]+)"', vte_contract)}
assert observed == expected, f"Cross-colony metric key drift: missing={expected-observed}, extra={observed-expected}"

cs_files = list(integrated.rglob("*.cs"))
assert len(cs_files) >= 19, "An optional collector was dropped during transfer"
for file in cs_files:
    text = file.read_text(encoding="utf-8")
    assert not re.search(r"\bclass\s+\w+\s*:\s*Mod\b", text), f"Extra Mod constructor: {file}"
    assert "HarmonyPatchType.All" not in text, f"Broad UnpatchAll(All) in {file}"
    assert "new StackTrace" not in text, f"Runtime StackTrace allocation in {file}"

bootstrap = (root / "Metrics/IntegratedModStartup.cs").read_text(encoding="utf-8")
for package in ("orion.hospitality", "pengin0503.recruitment", "dubwise.dubsbadhygiene",
                "vanillaexpanded.vanillatradingexpanded",
                "pengin0503.smartapparelupgrade", "pengin0503.rws.hospitalityaddon",
                "pengin0503.rws.recruitmentaddon", "pengin0503.rws.dubsbadhygieneaddon",
                "pengin0503.rws.vteaddon", "pengin0503.rws.smartappareladdon"):
    assert package in bootstrap.lower(), f"Missing target/legacy coexistence guard: {package}"

startup = (root / "RimworldStatisticsMod.cs").read_text(encoding="utf-8")
assert startup.index("StatisticsApi.InstallHost") < startup.index("IntegratedModStartup.Register()") < startup.index("PatchRunner.ApplyAll")
metadata = ET.parse("About/About.xml").getroot()
assert metadata.findtext("packageId") == "pengin0503.rimworldstatistics"
deps = [p.text.casefold() for p in metadata.findall("modDependencies/li/packageId")]
assert deps == ["brrainz.harmony"], "Gameplay targets and old statistics addons must not be required"

print(f"Embedded optional MOD integration guard: PASS ({len(cs_files)} sources, {len(expected)} stable IDs, one mod package)")
