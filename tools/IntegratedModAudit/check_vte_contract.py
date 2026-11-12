#!/usr/bin/env python3
"""Read-only 1.7 loadFolders / pinned 1.6 source contract audit for VTE (#175).

The loading game's actual binary must additionally be checked in a real-game test;
matching source is NOT a substitute for executed IL or gameplay validation.
"""
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

root = Path(sys.argv[1])
about = ET.parse(root / "About/About.xml").getroot()
assert (about.findtext("packageId") or "").lower() == "vanillaexpanded.vanillatradingexpanded"
folders = ET.parse(root / "loadFolders.xml").getroot()
assert [node.text for node in folders.findall("./v1.7/li")] == ["/", "1.6"]

src = root / "1.6/Source/VanillaTradingExpanded"
bank = (src / "Banks/Bank.cs").read_text(encoding="utf-8-sig")
assert re.search(r"public\s+void\s+DepositSilver\(List<Thing> silvers, int amountToDeposit\)", bank)
assert re.search(r"public\s+void\s+WithdrawSilver\(Pawn negotiator, int amountToWithwraw\)", bank)
assert re.search(r"public\s+void\s+TakeLoan\(Pawn negotiator, int loanAmount, int repayAmount, int repayDate, LoanOption loanOption\)", bank)
assert "depositAmount += (int)(num * (1f - Fees));" in bank
assert "depositAmount -= curAmount;" in bank
assert "this.loans.Add(new Loan" in bank
manager = (src / "TradingManager.cs").read_text(encoding="utf-8-sig")
assert "public void CompleteContract(Contract contract)" in manager
assert "if (VanillaTradingExpandedMod.settings.caravanLessContractItemPickup)" in manager
assert "npcContractsToBeCompleted.Add(contract);" in manager
assert "new Window_PerformTransactionGains(" in manager
lord = (src / "AI/LordJob_GrabItemsAndLeave.cs").read_text(encoding="utf-8-sig")
assert "public Contract contract;" in lord
assert "public void ExitCaravan()" in lord
assert "caravanSent = true;" in lord
assert "if (collectedAmount > 0)" in lord
assert "new Window_PerformTransactionGains(" in lord
print("VTE 1.7 loadFolders and pinned 1.6 confirmed-operation source contract: PASS")
