# Generic-first content MOD statistics — staged implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or test-driven-development to execute task-by-task. Use no subagents unless essential.

**Goal:** Add high-value, source-audited, cross-colony numeric statistics for genuinely new gameplay systems without duplicating vanilla-generic item/crop/trade metrics.

**Architecture:** One main MOD, existing `RimworldStatistics.dll` + Core + SDK. Prefer generic callbacks already available in 1.7 for modded and vanilla Defs, then framework-level adapters, finally target-specific readers only when necessary. Preserve profile keys/descriptors if any target disappears; each new event must have an audited unique ID and success point.

**Tech Stack:** RimWorld 1.7, Harmony, .NET 10, current Stats Families/Core, acquisition gates, CI game-reference contracts.

**Research:** `docs/research/2026-11-11-content-mod-coverage-and-priority-audit.md`. **No new production integration is approved or included by this plan alone.**

## Task 1 — Generic 1.7 fishing (independent deliverable)
**Files:** `Source/RimworldStatistics/Patches/Metrics/` (new fishing patch only after game code audit), `Source/RimworldStatistics.Core/Stats/Families/` (new family), `Tests/RimworldStatistics.Core.Tests/` (pure identity, branch/replay and metrics), `docs/research/` (pinned hooks).
- [ ] Inspect 1.7 Odyssey `JobDriver_Fish` and the pinned VFE Fishing catch patches; identify the confirmed **accepted catch**, product `ThingDef` and per-job identity, including rare extra catch and failure.
- [ ] RED: reject job start/failure/interrupt/duplicate, count only confirmed catch quantities per Def and single successful fishing action.
- [ ] GREEN: add a low-frequency post-success adapter using existing family opt-in, without requiring the VFE MOD; version and shape guard if needed. No per-tick scan.
- [ ] CI & self-review: pure tests, host full Core tests, game signature check, ZIP metadata, absence of VFE, historical persistence. Fail closed if source unknown.

## Task 2 — Generic modded incident types (independent deliverable)
**Files:** Existing `ConditionMetricPatches.cs`, `DisasterFamily.cs` or a *new* distinct family (avoid changing existing three-def meaning), Core tests.
- [ ] Audit `IncidentWorker.TryExecute` post-success and target world/home map scope. Verify not incident attempts, world-only unrelated events or duplicate callbacks.
- [ ] RED: three legacy vanilla counters unchanged, new custom IncidentDef count recorded once only on successful relevant player incident, no random-execution double credit.
- [ ] GREEN: record a bounded generic per-IncidentDef breakdown and cross-colony sum/top breakdown, without an extra per-VanillaEvents addon.
- [ ] CI & source-verified 1.7 tests, plus no-VEE case.

## Task 3 — VEF Outposts (independent deliverable)
**Files:** new integrated adapter if source audited, pure count logic tests, CI pinned VEF source contract.
- [ ] Audit `Outpost.ProducedThings` → `Produce` → `Deliver` and every delivery method, including Store, Null map, and failed parcel delivery. Confirm actual 1.7 compiled version/assembly.
- [ ] RED: must **not** evaluate `ProducedThings` twice, count product only after accepted/persisted transfer, never confuse dispatched and received.
- [ ] GREEN: if success is ambiguous, ship only representative-successful-save active outpost count with per-colony cross-comparison (no fake lifetime production). Preserve missing-target history.
- [ ] CI compile and verify absent-VEE/VEF and addon migration.

## Task 4 — Framework-based transports and unique systems (separate PRs)
- [ ] Vehicle Framework: pin version and trip completed/depart/arrival source contract; no per-cell/per-tick emitted events; one family serves Vanilla Vehicles Expanded and Tier 3.
- [ ] VPE: pin successful AbilityExtension cast and leveling events; prove non-duplication with base Royalty Psycasts.
- [ ] Vanilla Trading Expanded: audit bank/loan lifecycle and successful money changes, prevent double counting regular TradeDeal.
- [ ] Vanilla Genetics Expanded: completed unique experiments (not JobDriver start). 
- [ ] Rimatomics/Rimefeller: obtain public v1.7 DLL type/method/units via metadata/IL only; decide later.

## Release / verification guard
- [ ] Each task starts with failing tests; audit source at immutable SHA.
- [ ] All opt-in units remain Experimental and are absent without target.
- [ ] Historical metrics/profiles retained with target removed, no double count on load, no extrapolated earlier events.
- [ ] CI build, unit tests and packaging for each PR; no automatic release or Workshop upload.
- [ ] After code merging, clearly state which source events still need 1.7 in-game numeric/TPS parity.

**Prioritization question (optional):** approve P0 generic fishing → incidents → outposts, or choose a different system to lead. Pending approval, research documents can be merged without affecting runtime.
