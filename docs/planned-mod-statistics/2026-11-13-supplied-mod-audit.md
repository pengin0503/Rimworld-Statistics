# Supplied MOD archives — implementation addendum

Reviewed the five supplied archives on 2026-11-13 JST. This addendum uses the [existing implementation handoff](2026-11-13-implementation-handoff.md), including Task 0, default-on eligible acquisition with explicit opt-out preservation, revision 1, representative checkpoint selection and cross-colony sums. It adds three scoped tasks and a Simple Warrants loader check. The archives are private development inputs.

| MOD / Workshop ID | PackageId and release | Selected or available DLL | SHA-256 |
|---|---|---|---|
| Combat Extended / 3822862833 | `CETeam.CombatExtended`, 17.0.0.0 | 1.7 loadFolders selects root `Assemblies/CombatExtended.dll` | `bb94ecd94807b5731e5218eac21e038a1078e9e9d4e4bb43b7335b2699023ae1` |
| Gastronomy (Continued) / 3509488152 | `Orion.Gastronomy`, 1.0.5 | `1.7/Assemblies/Gastronomy.dll` | `c123c0b5099f92eb8fa8e4f2c163dde58ef02c261ae09d1134091c2c12e0cdab` |
| Prison Expanded / 3827258534 | `ZIPender.PrisonExpanded`, 1.3.1 | 1.7 loadFolders selects Common and 1.7. Main DLL is `1.7/Assemblies/PrisonExpanded.dll` | `9a5d1976738ac889acedad9b492062b0123d781ad8cf7b5ff55cdae84efac1f3` |
| Simple Warrants / 2676828755 | `pb3n.SimpleWarrants`, About lists 1.7 | `1.6/Assemblies/SimpleWarrants.dll` is the newest shipped DLL. Archive has no 1.7 directory or loadFolders file. Loader selection remains unverified | `2f489401fbe4f46d452bd06d90c178c2c65b157542b9dcd37da7331341d4e05c` |
| JecsLite - A RimWorld of Magic / 3312959701 | `zal.jecsliterwom`, 1.0.6 | 1.7 JecsTools compatibility assemblies. About explicitly describes it as a replacement for the required JecsTools components of Torann’s MOD | See the archive’s 1.7 Assemblies. No TorannMagic DLL |

Read-only Cloudflare R2 prefix checks returned no objects for the five supplied Workshop IDs or Torann’s 1201382956. Public plan files contain assembly identities and hash evidence only. This review used IL inspection. Game load and gameplay acceptance remain pending.

## Task 23 — Combat Extended pawn weapon reload actions

**Files:** `IntegratedMods/CombatExtendedReload/Source/CombatExtendedReloadStatistics.cs` under the game project, `Tests/RimworldStatistics.Core.Tests/CombatExtendedReloadStatisticsTests.cs`, the existing integrated declarations and EN/JA translations.

**Metric:** `mod.ceteam.combatextended.pawnreload.completed` (Combat, Count, scalar). Count a reload work action by a player-controlled pawn once when it actually adds ammunition to a magazine.

- [ ] Bind `CombatExtended.JobDriver_Reload.DoReload()` and its two calls to `CompAmmoUser.LoadAmmo(Thing,bool)`. Inside the scope, require active ammo consumption (`UseAmmo`) and observe the magazine count immediately before and after each load. Count once after normal `DoReload` return if any scoped load increases it. The job can top up from multiple stacks and one-at-a-time actions can recur. Each actual DoReload action is the counting unit.
- [ ] Test a load of 5 rounds gives 1 action, two loads in one DoReload give 1, no ammo or unchanged magazine gives 0, and ammo-disabled, nonplayer, turret, autoloader or merely queued actions give 0. A second distinct action that adds rounds gives +1. Leave per-shot ammunition and alternative reload paths to separate work.
- [ ] Run the shared acceptance checks. Confirm the initial unload/reset branch and the two load sites in the supplied build before patch installation.

## Task 24 — Gastronomy guest silver collected

**Files:** `IntegratedMods/Gastronomy/Source/GastronomyPaymentStatistics.cs`, `Tests/RimworldStatistics.Core.Tests/GastronomyPaymentStatisticsTests.cs`, the existing integrated declarations and EN/JA translations. Require `Orion.CashRegister` alongside Gastronomy.

**Metric:** `mod.orion.gastronomy.mealpayment.silver_collected` (Economy, Silver, scalar). Count the silver actually transferred for a nonplayer patron’s meal at a player restaurant.

- [ ] Scope `Gastronomy.Dining.DiningUtility.PayForMeal(Pawn,ThingOwner,out Thing)` to its call from `Gastronomy.Dining.JobDriver_Dine.OnTransferredFood(Thing,ThingOwner,out Thing)`. Bind the exact payment transfer call and its integer return amount. IL selects one silver stack, limits requested amount to payable debt, transfers to the supplied owner, and passes the actual return amount to `RestaurantDebt.PayDebt(Pawn,int)`. Capture only a positive returned amount for the player restaurant route. Preserve arguments, result and callbacks. Scope ownership at the serving route, using the selected player register and corresponding destination holder, and fail closed if ownership cannot be established.
- [ ] Test debt of 10 with 4 silver transferred gives +4, a second payment of 6 gives +6, a failed transfer gives 0, and a free meal, debt write-off, colonist-only meal or Hospitality shop purchase gives 0. Count the transfer once even if the stack merges. Leave paid order count pending until a stable full-settlement identity is available.
- [ ] Verify the selected binding and use the shared suite. Distinguish the silver transfer from later physical hauling to storage.

## Task 25 — Prison Expanded parole approvals

**Files:** `IntegratedMods/PrisonExpanded/Source/PrisonParoleStatistics.cs`, `Tests/RimworldStatistics.Core.Tests/PrisonParoleStatisticsTests.cs`, the existing integrated declarations and EN/JA translations.

**Metric:** `mod.zipender.prisonexpanded.parole.approved` (Social, Count, scalar). Count a parole approval that commits a release order for a living prisoner in player custody.

- [ ] Bind the one `PrisonExpanded.Justice.Sentences.Release(PrisonerRecord)` call inside `ChoiceLetter_ParoleHearing+<>c__DisplayClass8_0.<get_Choices>b__0()`. Verify the callsite and that the same record was eligible with `paroleDue` and player custody before. Require normal return, `paroleDue` cleared, `sentenceDays` zero, and guest exclusive interaction set to Release. Do not claim that the prisoner has physically left the map.
- [ ] Test approval gives 1, repeated selection/stale record, extension, recruit option, automatic record cleanup and vanilla release give 0. Verify that merely issuing the hearing gives 0 and a later departure does not increment this family.
- [ ] Use the shared suite. Leave sentence completion, actual parole departure and riots for distinct outcome checks.

## Simple Warrants — loader check and prepared settlement contract

- [ ] On the 1.7 test host, try the supplied Workshop folder as-is. Inspect the loaded assemblies for `SimpleWarrants.dll` owned by `pb3n.SimpleWarrants`, and record the assembly path/hash and active package. If no assembly is loaded, keep this target conditional until an effective 1.7 distribution or the actually installed folder is available.
- [ ] Once confirmed, implement `mod.pb3n.simplewarrants.handin.completed` (Count) and `mod.pb3n.simplewarrants.handin.silver_received` (Silver), both Economy. In `SimpleWarrants.WarrantRequestComp.Fulfill(Caravan)`, the exact per-warrant call to virtual `Warrant.GiveReward(Caravan,Thing)` follows target removal and precedes quest signalling, accepted-list removal and destruction. Capture the player caravan, target and accepted warrant at that call. Confirm terminal removal and destruction after normal Fulfill return. Record each distinct settled warrant once, including one with zero reward. Record actual positive caravan silver movement in the corresponding concrete reward call only. The base method sets status; the Pawn override chooses live/dead reward and the Artifact override chooses artifact reward. Exclude preview, acceptance and generic quest completion.
- [ ] Test no target/cancel/repeated callback +0, live/dead/artefact hand-ins add one each with their respective actual paid amounts, zero-silver fulfillment adds one count and zero silver, and multiple matches in one action each contribute once. Validate effective assembly binding and follow the shared suite.

Save Our Ship 2 stays pending a 1.7 release. A RimWorld of Magic stays pending the actual 1.7 gameplay MOD, which may be a continuation if one is available.
