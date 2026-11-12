# Specification status index — updated 2026-11-12

This index is the **starting point** for maintenance. Older specs are retained as historical design decisions/acceptance fixtures, not to be reapproved or blindly reimplemented. **Never read the entire old tree and start a new discovery phase**: use the active documents and follow precise source pointers.

## Current baseline (2026-11-12)

`main@59ad1eb0` = released **v1.6.0** plus merged #148–#153 (SaveReader **v17**, profile schema 2, checkpoint format 1). PR #102 and #103 described below were merged before v1.5.0/v1.6.0; status lines in the older sections are historical snapshots, not the current state.

## Feature expansion (2026-11-12) — design and plan only, no production code changed

Eight planned feature areas (statistics window layout, favorites/custom overview, colony groups and comparison, acquisition-status list, all-time TOP10 rankings, CSV export, manual profile backup/restore, other-MOD statistics). Read in this order:

1. [Design: invariants, shared foundations F1–F7, per-feature specs and acceptance](specs/2026-11-12-feature-expansion-design.md)
2. [Design appendix: other-MOD statistics (VPE, VEF Outposts, Hospitality, DBH, Vehicle Framework, SAU) with R2/source evidence](specs/2026-11-12-feature-expansion-mod-statistics.md)
3. [Implementation plan: work packages A1–A8, M1–M7, tests and verification](plans/2026-11-12-feature-expansion-plan.md)
4. [Handoff prompt for the implementing model](plans/2026-11-12-feature-expansion-handoff-prompt.md)

The design lets implementers change names, tab layout and internal structure; the invariants (§2) and the intent of each acceptance criterion are fixed.

## Optimization review and implementation handoff (2026-11-11) — design only, no production code changed

Architecture-level review of `main@2cd7a2d` for performance, memory, maintainability and extensibility. Statistic IDs, stored formats and aggregate meanings are unchanged by every adopted task. Read in this order:

1. [Current architecture](specs/2026-11-11-optimization-1-architecture-analysis.md)
2. [Findings F-01–F-23](specs/2026-11-11-optimization-2-findings.md) (each marked measured / static / unverified)
3. [Priorities, expected effect, risk; adopted and rejected options](specs/2026-11-11-optimization-3-prioritization.md)
4. [Adopted design D1–D11](specs/2026-11-11-optimization-4-adopted-design.md)
5. [Implementation plan T0–T11](plans/2026-11-11-optimization-5-implementation-plan.md)
6. [Regression, performance comparison and verification](plans/2026-11-11-optimization-6-verification.md)
7. [Prompt for the implementing model](plans/2026-11-11-optimization-7-handoff-prompt.md) · [benchmark harness source](plans/2026-11-11-optimization-benchmark-harness.md)

No in-game TPS, save-time or UI timing was measured; T11 (checkpoint archive) is gated on a real-profile measurement by the owner.

## Most recent authoritative user decision (2026-11-11): 19-candidate keep/delete

**Read [approved 13 keep / 6 delete groups](../research/2026-11-11-approved-metric-retention-removal.md) first.** This user decision supersedes historic design inventories and all sections below when inconsistent. The deleted groups correspond to **10 active metric keys**: `membership.unique`, `membership.rejoin`, `membership.recruit_time.min`, `membership.recruit_time.max`, `facilities.rooms.peak`, `food.no_shortage.longest`, `work.crops.cold_loss`, `work.crops.other_loss`, `work.crops.loss_rate`, `facilities.medical_beds.peak`. Previously retired running-generator count and nested storage-holder inventory remain discontinued. Retain all other 13 named difficult-metric groups.

**Unmerged draft Statistics PR #102:** SaveReader **v15**, **404 registered definitions (321 base + 83 derived)**; historical recovery inventory **264 acquisition definitions (216 live, 39 snapshot, 9 derived)**. Checkpoint schema v1, profile schema 2, and surviving metric meanings/revisions preserved. Existing family-level ID dedup still supports first-join and birth correctness, but does not expose lifetime unique-member or rejoin counts. Food spoilage, crop sowing/harvest, stock nutrition, animal/mech comparisons, colony rescue, caravan and raid outcomes remain. The deleted high-frequency `ResourceCounter` observation and `Plant.MakeLeafless` Harmony patch and region grid walk have been removed. **Completed v15 source-pinned qualification:** [49 original R2 SaveReader inputs #42933296742](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42933296742) **PASS** (49/49 unchanged, 0 errors) and [actual R2 RimWorld 1.7 + Harmony A+B #42933296702](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42933296702) **qualified PASS** (no new startup errors, two expected registered menu patches, nine additional unattributed patches). See the [combined immutable v15 record](../testing/2026-11-11-approved-v15-49-r2-acceptance.md). Older v14 runs remain historical, not a substituted source for v15. No actual loaded MOD 192-save metric-value parity or TPS benchmark was run.

**Shipped main v1.4.0 is unchanged.** Do not merge/release without a further user instruction, and do not repurpose or delete private historic save records.

## Current baseline and work branch

- **Shipped main:** v1.4.0, `main@f6890ee626bceb087693136a304333ce089d926c`; 415 registered definitions (331 original + 84 derived); SaveReader v10. Verify HEAD before changing release version.
- **Active draft implementation:** [PR #102](https://github.com/pengin0503/Rimworld-Statistics/pull/102), branch `feat/post-v1.4-coverage-and-full-leaderboards-20261110`. Scope: prior coverage/leaderboards, provisional R2 research, home turrets/Silver/medicine, 1.7 saved Pawn roles, and the [approved 19-candidate deletion decision](../research/2026-11-11-approved-metric-retention-removal.md). **SaveReader v15, 404 definitions, not merged**.
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

## 2026-11-10 source-evidenced reader v13 and actual R2 qualification — current

**Main is unchanged:** shipped v1.4.0, **SaveReader v10**. [Draft PR #102](https://github.com/pengin0503/Rimworld-Statistics/pull/102) is the active unmerged implementation, now **SaveReader v13**. It retains all **415 metric definitions, revisions, and checkpoint format v1** while adding:
- P0-C provisional official 1.7 research (`research.snapshot.finished`, rate, highest tech) via [R2 audited 122 ResearchProjectDefs](../research/2026-11-10-r2-game-source-1-7-research-recovery.md). 
- Strictly positive, ownership-confirmed *historical saved snapshot lower bounds* on home-map [vanilla turrets, direct Silver, three Core medicines](../research/2026-11-10-r2-1-7-historical-home-stock-turret-evidence.md), **all Partial/Experimental**; missing or unproven is Unknown, never zero.
- Earlier P0 coverage diagnosis, complete Def rankings and bounded P2 optimizations retained.

**Exact acceptance, without rediscovery:**
- [Core CI #42845357275](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42845357275) **success**, covering v13 source and schema-2 anonymous acceptance utility. [#42844594938](https://github.com/pengin0503/Rimworld-Statistics/actions/runs/42844594938): 726 Core passed, 0 failed, 9 skipped; 888 translations.
- [R2 private 49 actual original files v13 #42846305788](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42846305788) **PASS**: 49 unchanged, 0 errors, 1.7 Partial positive observations: research 7, turret 0, Silver 6, medicines 6. Turret zero **positive observations** does not prove zero installed turrets. [Exact evidence](../testing/2026-11-10-r2-49-original-reader-v13-acceptance.md).
- [Actual R2 RimWorld 1.7.4985 game/Harmony v13 A+B #42846305625](https://github.com/pengin0503/RimWorld-for-mod-test/actions/runs/42846305625) **qualified PASS**: no new startup errors, 2 expected title-menu patches registered, 9 other patches unattributed; no actual in-game metric parity or TPS claim. [Exact evidence](../testing/2026-11-10-rimworld-1-7-v13-actual-game-smoke.md). RWMT [PR #73](https://github.com/pengin0503/RimWorld-for-mod-test/pull/73) merged for **test infrastructure only**, Stats PR #102 still draft.

**User compatibility ruling:** take differences between unstable 1.7 internal versions as provisionally irrelevant for development; record the exact source version, and do not upgrade an inferred, MOD-sensitive research/ownership quantity to Verified. The user's eight newest 1.7.5000 private originals are **one campaign**; they were inspected separately via read-only Python structural audit, *not* as part of the R2 49 original .NET run.

**Still outstanding (do not misreport as implemented):** complete live-MOD roster free/prisoner/slave/mech, nested item/holder or caravan stock, medical beds and lifetime medical events, confirmed powered generator runtime state, save-game exact value parity, 55+ independent real saves, matched TPS/save-time A/B and production .NET acceptance of newest eight local originals. Phase B live collectors exist, but historical backfill of the unsupported fields is not proof-safe.

## Latest 2026-11-11 user-approved scope: roster and retired work

**Draft PR #102, `SaveReader v14`** now adds historical positive-only free/prisoner/slave saved-home Pawn counts from R2 1.7.4985 game predicate evidence, plus a live independent colony-slave count and min/max colony comparisons. The latest [R2 1.7 Pawn/8-original evidence record](../research/2026-11-11-r2-1-7-roster-scope-retirements.md) is authoritative over older P1 plans. Eight actual user originals (one campaign) independently yielded **11 free, 1 slave, zero confirmed prisoner positives per direct home map**, but zero prisoner positives is **not** a proven actual zero nor full player faction roster.

The user **canceled** nested storage-container contents and **deleted** `facilities.running_generators.latest` plus its derived rankings. Core stock remains for direct ground items and pawn-owned equipment/inventory; rated power capacity remains a separate statistic. Catalog now has **331 base + 83 derived = 414** definitions. Old profile data is preserved, checkpoint v1/profile schema 2 remain, SaveReader v14 reimports. The former v13 49-original and game A+B results belong only to v13: rerun isolated tests with a pinned v14 source SHA before claiming acceptance.


**v14 final qualification (2026-11-11):** [full source-pinned 49-original reader and real 1.7 game acceptance](../testing/2026-11-11-r2-v14-roster-49-original-and-game-acceptance.md) — both private workflows PASS (49/49 originals unchanged, 0 parse errors, free positive Partial 7/7, prisoner 0/7, slave 0/7; actual engine startup + Harmony PASS). The 0/7 prisoner/slave values are *no positive historical evidence*, never proven zero. The v13 results above are retained as historical, superseded for latest status by this v14 record. Draft PR #102 remains unmerged.
