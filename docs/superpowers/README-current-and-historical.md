# Specification status index — updated 2026-11-10

This index is the **starting point** for maintenance. Older specs are retained as historical design decisions/acceptance fixtures, not to be reapproved or blindly reimplemented. **Never read the entire old tree and start a new discovery phase**: use the active documents and follow precise source pointers.

## Current baseline and work branch

- **Shipped main:** v1.4.0, `main@f6890ee626bceb087693136a304333ce089d926c`; 415 registered definitions (331 original + 84 derived); SaveReader v10. Verify HEAD before changing release version.
- **Active draft implementation:** [PR #102](https://github.com/pengin0503/Rimworld-Statistics/pull/102), branch `feat/post-v1.4-coverage-and-full-leaderboards-20261110`. Scope: coverage diagnostics, measured item ranking, synthetic MOD/DLC hardening and two bounded optimization paths. **Not merged** merely by being documented here.
- **Research origin:** [PR #101](https://github.com/pengin0503/Rimworld-Statistics/pull/101), originally separate, now **its two authoritative files are also copied onto PR #102's branch** so a future merge of #102 is self-contained. Do not delete #101 while external issue links depend on it.

## Use these current documents in this order

1. [R2 source-of-truth and no-repeat handoff](../research/2026-11-10-r2-private-reference-inventory.md): exact accessible bucket/version, 49 private R2 saves, audited static 1.7.4985 game DLL/Defs and limits; source keys, retrieval procedure, 1.7 provisional equivalence and runtime MOD-Def caveat.
2. [Five local original-save evidence](../research/2026-11-10-post-v1.4-private-five-save-audit.md): 1 campaign, five checkpoint samples, 25 tested Def split reconciliations, 123 research progress pairs, verified direct-parent limitation, private-file evidence grade.
3. [Full R01–R14 plan and safety contract](plans/2026-11-10-post-v1.4-five-save-improvements.md): exact intended work packages P0-C/P1-A–D/P2 and acceptance; **only implement what the evidence proves**.
4. [P0/P2 implementation and CI acceptance](../testing/2026-11-10-post-v1.4-p0-implementation-acceptance.md): what code actually changed, synthetic A/B, CI, skip and deferred gates.
   - **New actual production reader evidence**: [49 original R2 files, .NET acceptance passed](../testing/2026-11-10-private-r2-49-original-net-acceptance.md), [RWMT run #42830780995](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42830780995), compiled from immutable Stats SHA `91c2203e1f0c2800759f9d717f14a27b336e55fc`. `RimWorld-for-mod-test` PR #71 was merged for the read-only test harness; Statistics PR #102 itself remains unmerged.
5. [Existing historical recovery constraints](plans/2026-11-09-historical-save-recovery.md) and [design](specs/2026-11-09-historical-save-recovery-design.md): read these for older v8–v10 compatibility/compaction only, not as instructions to downgrade the current reader.
6a. **Qualified actual-game pass**: [RimWorld 1.7.4985 A+B runtime validation](../testing/2026-11-10-rimworld-1-7-v11-actual-game-smoke.md), [RWMT #42838545527](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545527): game startup baseline **passed** (new errors 0); Harmony two requested menu-prefix patches checked, B **passed** (9 other patches present, not individually attributed). This is a game-start/Harmony test, not actual research-value or TPS parity.
6b. **Qualified actual R2 original-file pass for v11**: [49 real saves](../testing/2026-11-10-r2-49-original-reader-v11-acceptance.md), [RWMT #42838545431](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42838545431): 49 originals unchanged, 0 errors, **7/7** 1.7 legacy research outputs Partial, other 1.5/1.6 values not inferred; p50 53.132ms/p95 142.153ms (not controlled A/B).
6. [R2 real game IL and 122 research Def comparison](../research/2026-11-10-r2-game-source-1-7-research-recovery.md): eight local originals, saved progress counts, partial historical research, reader v11 and exact IL call tokens. Use it before reopening P0-C research.
7. Source of truth for current runtime semantics: `Source/RimworldStatistics/SavedStateMetricReader.cs`, `Source/RimworldStatistics.Core/Reading/SaveReader.cs`, `Stats/{ResearchStateSnapshot,RosterSnapshot,StockSnapshot,FacilitiesSnapshot}.cs`, `Stats/MetricCalculator.cs`, `Stats/CrossColonyAggregator.cs`, `Profiles/ProfileMutations.cs`. Tests > prose if they disagree. Runtime is 1.7 1.7.4985 indexed source until a matched 1.7.5000 source is archived.

## Older documents retained as historical baseline, not active scope

| Document(s) | Decision / rationale | Retention rule |
| --- | --- | --- |
| `plans/2026-11-06-rimworld-statistics.md`, `specs/2026-11-06-rimworld-statistics-design.md` | Original v1 cross-save colony identity/aggregation contract | Keep for v1 imported profiles, lineage and regression tests |
| `plans/2026-11-07-numeric-statistics-expansion.md`, `specs/2026-11-07-numeric-statistics-expansion-*.md`, `specs/2026-11-07-statistics-expansion-*.md` | Initial Phase B/C/D2 numerical catalog, candidate triage and key rationale | Historical; **415 current registry is authoritative**, not every catalog candidate |
| `plans/2026-11-08-experimental-statistics.md`, `plans/2026-11-08-mod-specific-statistics.md`, `specs/2026-11-08-experimental-statistics-and-mod-adapters-design.md` | Experiment gate OFF-by-default, SDK/provider stability | Keep for compatible stored provider descriptors and the explicit opt-in policy |
| `plans/2026-11-09-auto-mod-adapters-one-click-audit.md` | Earlier adapter UX plan | Historical; not current P0-C/P1 work |
| `plans/2026-11-09-post-v1.2.1-stabilization.md`, `plans/2026-11-09-verifyharness-guided-suite.md` | Historical release gate/test harness background | Retain until their tests/references are removed or replaced |
| `plans/2026-11-10-lifetime-cumulative-statistics.md`, `specs/2026-11-10-lifetime-cumulative-statistics-design.md` | Cross-colony lifetime vs single-save current-only distinction | **Preserve**: still enforces core product scope and prohibitions |
| `plans/2026-11-09-historical-save-recovery.md`, `specs/2026-11-09-historical-save-recovery-design.md` | Checkpoint v1, missing interval evidence and v8/v9 migrations | Preserve while v10 profiles and Issue #71/#72 exist |

**Deletion decision:** **No spec deleted in this PR**. We did not establish that a single legacy document has zero incoming references or unique compatibility rationale. Their status has been explicitly indexed instead, avoiding both re-analysis and broken historical links. Before deletion, show exact incoming Markdown/code references, migrate unique content to the active spec, check GitHub issues/PR links and run the link/test checks; a spec being older is not sufficient evidence that it is unnecessary.

## Non-negotiable release/verification policy

- Do not publish private `.rws` or MOD inventories; do not expose R2 credentials, bytes or per-save identity.
- The 1.7 provisional research recovery uses an **official Def baseCost threshold**, not mere positive progress; results always remain **Partial/Experimental** under unverified MOD patches. Never infer generator running from ThingDef, free colonist from map Pawn count or lifetime events from a medical snapshot.
- **User ruling (2026-11-10):** provisionally treat 1.7.4985 and 1.7.5000 as equivalent for further work. Their exact binary/Def equality has **not** been proven. Archived official 1.7.4985 Def costs can be used for experimental 1.7.5000 historical research, marked Partial; do **not** generalize this to unknown MOD-patched Defs, 1.5/1.6 or Verified labels.
- **49** private R2 originals **did** pass an actual pinned production .NET reader and integrity run (#42830780995), but **55+** original files, same-machine before/after baseline, five TPS windows, ten paired game saves, and the *five newer local 1.7.5000 originals* remain unrun in that production acceptance. Never infer all other validation passed.
- No unintended release, forced merge, or deletion of active documents.

## 2026-11-10 post-v1.4 research implementation status

Main still ships v1.4.0 with **SaveReader v10**. **PR #102 (not merged)** advances the reader to **v11** specifically to recover provisional official 1.7 research snapshots from unchanged legacy saves, without changing checkpoint v1 or any of the 415 metric definitions. The [game IL/Def/source record](../research/2026-11-10-r2-game-source-1-7-research-recovery.md) and [complete game Def reference](../research/2026-11-10-r2-1-7-research-def-catalog.json) replace the earlier assumption that a different 1.7 build blocks all research recovery. Runtime and mixed-MOD semantics are still separate verification gates.
