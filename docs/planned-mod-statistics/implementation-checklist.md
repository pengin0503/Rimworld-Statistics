# Implementation checklist — planned optional-MOD statistics

This checklist applies to **every** target in [README.md](README.md), including compatibility-blocked and low-priority entries. A target remains planned if its proof is missing; record the blocker rather than hallucinating a metric or dropping the row.

## Pre-implementation acceptance gates

- [ ] Confirm the mod's **exact PackageId**, official source/revision, 1.7 `loadFolders.xml` and DLL selected in 1.7 (source or IL).
- [ ] Inventory current Rimworld-Statistics metric families and descriptors from **current main**, not an obsolete audit; identify any generic Def breakdown that already counts the desired event.
- [ ] Describe the concrete user-visible **unit** (events, people, liters, nutrition, silver, completed jobs etc), identity/breakdown key, owning colony, and when it becomes committed success. Distinguish state/snapshot/maximum from lifetime event totals.
- [ ] Find the lowest-frequency, post-success hook. No speculative pre-success events, `Tick`-based counter guesses, nominal production or stock-to-lifetime extrapolation.
- [ ] Ensure no double count from multiple signals, assembler-plus-hatcher, cross-mod wrappers, production-and-delivery, load/reload/rehydration, retry or save chains.
- [ ] Add optional-presence guards; unsupported assemblies and DLC fail closed, and previously recorded provider metrics remain readable after removal.
- [ ] Treat heterogeneous MOD inventories/DefName collisions as Partial/Unknown unless identity is proven.
- [ ] Keep new metric families Experimental/opt-in until evidence meets the project's promotion requirements.
- [ ] Add success/failure, repeated invocation, no-target, unload/reload, save-checkpoint, independent-colony and branch-save regression tests.
- [ ] Run source/IL audit, affected automated tests, release build/package checks and where practical RimWorld 1.7 test harness; document **actual** verification and unresolved game parity/TPS evidence.

## Suggested batches (not a fixed promise of release order)

| Batch | Candidate scope | Important dependency |
|---|---|---|
| D1 | Aspirations, Tribals, Classical | Independent state transitions and post-success checkpoints |
| D2 | Cryptoforge, Ancients, Drone Factory, Insectoids 2 | Unique event outcomes; pawn/quest dedup |
| D3 | Factory, Nutrient Paste Expanded, Brewing Expanded, Gastronomy | Actual production or settled payment; generic bill/trade exclusion |
| D4 | Skills, Android, Pirates, Deserters, Generator, Settlers, Books | Validate 1.7 source, scope, own event semantics |
| D5 | Genetics, Rimefeller, Rimatomics, SOS2, Magic, Combat Extended | Source/IL or potentially hot-path perf auditing |
| D6 | Prison Expanded, Project RimFactory Revived | Exact target identity / 1.7 compatibility blockers |

Each implementation PR should link the relevant candidate row and **record whether the candidate was fulfilled, partially fulfilled, or blocked**. Candidate admission alone must not lead to a new metric ID. Do not create a separate GitHub issue for every row by default; existing issues may already cover parts of the work.

## Reference and policy

- [Master candidate index](README.md)
- [2026-11-12 source findings](2026-11-12-source-research.md)
- [Generic-first content MOD audit](../research/2026-11-11-content-mod-coverage-and-priority-audit.md)
- [Cross-colony metric scope](../research/2026-11-10-cross-colony-metric-scope-audit.md)
