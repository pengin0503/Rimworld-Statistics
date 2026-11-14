# Rimworld Statistics: next-development opportunity audit (2026-11-14)

**Type:** read-only design/research. **Baseline:** main at dc90d3cd99e4c1bdae5240ec24a049c010304b04, RimWorld 1.7. **Release baseline:** v1.8.0, published 2026-11-14. **No product code, supported-MOD list, Steam Workshop or release changes in this audit.**

## Verified baseline and omissions to avoid

- The GitHub search at investigation time returned exactly two open Issues: [#72](https://github.com/pengin0503/Rimworld-Statistics/issues/72) (historic recovery, ancestry and original-save/game acceptance) and [#195](https://github.com/pengin0503/Rimworld-Statistics/issues/195) (profile write amplification). There were no open PRs; the main-head numeric-verification workflow completed successfully.
- [#255](https://github.com/pengin0503/Rimworld-Statistics/pull/255) already added per-colony metric history; [#256](https://github.com/pengin0503/Rimworld-Statistics/pull/256) added joins and free-colonist deaths by species. [#237](https://github.com/pengin0503/Rimworld-Statistics/pull/237) added six MOD-specific outcomes. Never propose these as wholly unimplemented.
- Existing UI: DetailsTab has search, category/status filters, sorting, favourites, per-colony and comparison modes, and row virtualization; CoverageTab has measured/partial/missing and source-state filters; RankingsTab has search; OverviewTab has customizable favourites; chart dialogs show colony bars and optional history overlays. Proposals below are improvements, not claims of absence.
- Existing CSV export already writes five CSVs plus a JSON manifest with coverage and evidence. An enhanced history/JSON export is additional functionality, not an initial export implementation.
- The current user settings enable supported metric acquisition by default; explicit existing opt-outs are retained. Recording changes are intended not to require a game restart.
- Current released support names live only in docs/supported-mods.md. The 27 proposed popular MODs and six race candidates in docs/planned-mod-statistics are **not supported integrations** solely because a research entry exists.

## Concrete correctness and maintenance findings

### C1 / P0: an unproven save lineage can be drawn as a continuous line

Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs, in SaveChartPoints, groups samples by game tick and makes ConnectToPrevious true when adjacent points have a value and equal ModCampaignId. A common campaign ID does not prove that two saves are on a parent/child branch. This can visually imply a continuous chronological trajectory across siblings. CheckpointChartHistory instead traces explicit parent keys. The current code also handles same-tick contradictory readings by leaving a gap, which should be retained.

**Decision proposal:** connected lines require proven lineage; unproven save-file/snapshot observations should display independent markers, or carry an explicit visual "unproven succession" legend. Preserve per-tick values and never interpolate a missing value as zero. Test same-campaign sibling branches, rewind, missing parents, same-tick contradicting values, and a positive proven direct-parent case. Do not label this as a reproduced in-game rendering defect before the harness confirms it.

### C2 / P0: Issue #195 must be rebased against current code

The Issue describes SaveEvery=5, but Source/RimworldStatistics.Core/Import/ImportEngine.cs now has a five-second SaveInterval and a final save. In contrast, ProfileSession.WriteLatest still serializes ProfileJson under _lock; ProfileStore.SaveSerialized still obtains and validates the full existing file, compares statistics, and commits atomically. Its optimized importState comparison is already present. Measure remaining problems before introducing a potentially unsafe timestamp/hash conflict detector.

**Decision proposal:** synthetic 0.2MB, 5MB and 10MB profiles; collect p50/p95 save latency, bytes written, lock wait, allocations and collision tests for concurrent sessions. Optimize only a measured bottleneck while retaining source-v1 backup, three backup rotations, file conflict detection and crash safety. Update Issue #195 with the changed baseline.

### C3 / P0: Issue #72 remains evidence-dependent, not a missing parser alone

Direct checkpoint ancestry has positive original-file evidence in prior reports, but the Issue still requests real-game UI acceptance, positive-delta real parent-chain evidence and a 55+ real-save benchmark. Do not backdate a counter delta to an exact day, do not infer branch ancestry from identical campaign IDs, and never aggregate sibling intervals as lifetime totals. Existing synthetic tests do not independently satisfy the remaining acceptance.

### C4 / P1: distinguish time-series, snapshot, cumulative and rank semantics in UI

MetricHistoryKind distinguishes SaveObservations, CheckpointLineage, WealthRecorder, GameRecorder; NumericValue carries coverage. The history UI should make missing and partial measurements and the chosen X-axis explicit. A measured point is not automatically a provable intervening event. Colony overlays should not silently imply aligned founding dates or identical DLC/MOD availability.

### C5 / P1: profile inspection and recovery diagnostics

DataManagementTab, ImportTab and CoverageTab exist. Consider a read-only diagnostic export that explains which files were skipped, failed or conflict-marked, which statistics are Unknown/Partial, and whether a history point came from an archive/recorder/save. Avoid exposing raw user identifiers or original saves in public issue reports.

## Prioritized feature proposals

| Priority | Workstream | User-facing deliverable | Non-negotiable test/gate |
|---|---|---|---|
| P0 | Chart evidence semantics | Connected only with proven parent links; unproven observations marked distinctly | Same-campaign sibling, rewind, missing chain, same-tick divergence |
| P0 | Profile persistence scale | Reduced measured save overhead without weakened durability | 5/10MB benchmarks, backup/restore, stale writer, crash safety |
| P0 | Original-save acceptance | Honest closure matrix for #72, with no fabricated exact events | Real saved parent-child samples, real UI, 55+ saves if available |
| P1 | History UX | Coverage/point tooltip, optional zoom or range and clearer overlay axis | Missing/Partial labels, 12-language layout, narrow 1440p and smaller windows |
| P1 | Comparison UX | Normalized per-day ratios only for mathematically valid metrics, plus easy switch to absolute values | Zero/unknown duration, independent colony inclusion, no ratio of maxima |
| P1 | Import diagnostics | Reason-specific read-only diagnostics and non-sensitive support bundle | Corrupt file, unsupported format, changed file, missing DLC/MOD, privacy |
| P1 | Race and geographical dimensions | HAR species + independent Biotech xenotype breakdown and map landform classification | Avoid double-count of pawn identities; stable package+Def identity |
| P2 | Export extensions | Optional history/coverage JSON/CSV without changing existing CSV contract | Large exports, missing values blank/null, injection protection |
| P2 | Large-chart performance | Viewport downsampling/LOD for render only | Preserve source data and extremes; deterministic UI on large profiles |

**Not proposals:** wholesale reimplementation of search, favourites, basic charts, CSV, existing 64 listed supported-MOD entries or default-on acquisition.

## MOD opportunity register: next actionable batches

These names are primarily in the already-existing [27-candidate register](../planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md) and [race audit](../planned-mod-statistics/2026-11-14-popular-race-mod-audit.md). This is a priority/scope reevaluation, not a claim that 33 entirely new MODs were discovered.

| Priority | Candidate | Distinct successful outcome or classification | Proof needed before feature implementation |
|---|---|---|---|
| P0 | Humanoid Alien Races / Biotech | Extend **already shipped** joins/deaths-by-species to historical roster maxima and separate xenotype dimension | Stable package+ThingDef vs XenotypeDef keys, no doubled joins/deaths, removed-MOD history |
| P0 | Geological Landforms | Real founded colony landform, colony counts/day and record breakdown by landform | Stable Landform.Id, attribution only to founded/played colonies, persistence when MOD absent |
| P0 | Vanilla Events Expanded | Fully completed multi-stage event chain, by Def/stage | True terminal success, cancel/timeout/debug and generic incident overlap |
| P0 | Vanilla Psycasts Expanded – Puppeteer | Confirmed puppet formation and consciousness transfer outcomes | Active 1.7 DLL post-success state; generic VPE casts already tracked |
| P0 | Vanilla Ideology Expanded – Dryads | Actual player dryad spawned by successful merger | Check result spawn after CompSpawnAwakened.DoSpawn, avoid generic births |
| P1 | Hospital | Patient arrivals, confirmed cured departures, silver actually collected | PatientLeaves != cured; payment attempt != actual silver received |
| P1 | Vanilla Fishing Expanded | Non-Odyssey successful catches by fish Def and accepted amount | Existing FishingFamily requires Odyssey; Odyssey and VFE source can overlap |
| P1 | Vanilla Furniture Expanded – Farming | Actual positive sprinkler-induced plant growth delta | 1.7 selected DLL, clamped delta; avoid frequent per-plant per-tick hooks |
| P1 | Vanilla Races Expanded – Archon | Confirmed transcendent pawn return to player map | Spawn success rather than death/queue; no redundant joins |
| P1 | NewRatkinPlus | Measured productive wheel work (not job started) | Interrupt-safe completion or measured energy; actual 1.7 DLL |
| P1 | Alpha Genes | Successful unique gene operation | Isolate successful operation from generic Biotech edits |
| P2 | Vanilla Races Expanded – Lycanthrope/Fungoid/Insector | Truly committed transformation by type | Selected 1.7 DLL and source-level distinct success from generic xenotype change |
| P2 | Vanilla Landmarks Expanded | First actual discovery/visit classified by landmark | Persistent visited identity; no map-generation counting |
| P2 | Vanilla Furniture Expanded – Power / Spacer | Distinct meltdown or completed repair amount | Existing incidents/repair metrics overlap; safe low-frequency signal |
| Conditional | Vanilla Traits Expanded / Non-Lethal | Verified unusual forced flee, furniture damage or special stun | Vanilla combat overlap, Combat Extended interaction, hot-path risk |
| Hold | Gloomy Dragonian, Rabbie, Yuran | Shared HAR classification only | Do not create one adapter per race without unique confirmed outcomes |
| Hold | Unsupported-release or source-unproven candidates | No invented integration | Actual loaded RimWorld 1.7 package required |

The source notes of prior research are evidence for *candidate selection*, not proof of game-side success. Workshop subscriber counts are approximate, dated interest proxies rather than active 1.7 users. Public Workshop 1.7 tags alone do not establish loaded-assembly or Harmony-target compatibility.

## Deduplication and exclusion decisions

1. Check docs/supported-mods.md, Source/RimworldStatistics/Metrics/IntegratedTransitionAdapters.cs, optional adapters, built-in MetricRegistry and the 2026-11-13 additional-30 status before registering a metric key. Preserve original 35 + 27 delivered/conditional decisions.
2. Do not resuscitate the waived Gastronomy individual settled-order, Prison Expanded whole-riot and Rimefeller refinery input/product proposals without new success-path evidence.
3. Do not treat generic race goods, traits or diseases as reason for one-off integration; shared HAR identity is more scalable. Species joins/deaths already use GameFacts.Species and the package-qualified Pawn.def key as of #256; the additional requirements are roster/xenotype and unique race outcomes.
4. Never add researched MODs to docs/supported-mods.md until implemented.
5. Avoid per-tick reflection, broad high-frequency Harmony patches, guessed work/energy, unsafely summed snapshots or duplicate generic collectors.
6. Existing opt-outs survive. New supported recordings default on, and switching recording settings should not require a restart.
7. Missing optional DLC or absent/incompatible MOD is Unknown/Partial/NotApplicable as semantics require, never invented zero. Do not require Anomaly in acceptance.
8. For hands-on game checks use pengin0503/RimWorld-for-mod-test when available; 1.7 patch versions are treated equivalently for task scope, but active selected DLL still must be checked.

## Proposed execution architecture

Six *independent read-only research* lanes (A ancestry/chart integrity; B storage/performance; C UI/exports; D shared race/geography; E special gameplay MOD outcomes; F economy/fishing/production MOD outcomes) run concurrently on separate branches and output different report paths. **A coordinator reviews and integrates; do not launch six independent implementation branches against shared MetricRegistry/Language files.** Each resulting feature gets its own design, TDD tests, code review, CI and PR after research approval. Merge order should be core correctness -> persistence -> shared dimensions -> separate MOD adapters -> UI -> integration regression, with rebasing on every merge.

See [parallel-AI research handoff](../superpowers/plans/2026-11-14-parallel-ai-research-handoff.md) for copy-ready isolated assignments and research acceptance conditions.

## Source pointers

- Core chart: Source/RimworldStatistics.Core/Stats/MetricCalculator.ChartHistory.cs, MetricChartSeries.cs; Tests/RimworldStatistics.Core.Tests/MetricChartHistoryTests.cs.
- Profile and import: Source/RimworldStatistics.Core/Profiles/ProfileSession.cs, ProfileStore.cs; Source/RimworldStatistics.Core/Import/ImportEngine.cs.
- UI coverage: Source/RimworldStatistics/UI/{DetailsTab,CoverageTab,OverviewTab,RankingsTab,Dialog_ColonyMetricHistory}.cs; docs/csv-export.md.
- Species: Source/RimworldStatistics/Patches/Metrics/GameFacts.cs, MembershipMetricPatches.cs, DeathMetricPatches.cs; Tests/RimworldStatistics.Core.Tests/SpeciesBreakdownTests.cs.
- Candidate evidence: docs/planned-mod-statistics/2026-11-14-popular-mod-discovery-register.md, 2026-11-14-popular-race-mod-audit.md, 2026-11-13-additional-30-implementation.md, 2026-11-13-remaining-mod-statistics-design.md.
- External verification: [Humanoid Alien Races Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=839005762), [Puppeteer Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3033779606), [Geological Landforms source](https://github.com/m00nl1ght-dev/GeologicalLandforms). Exact Workshop subscribers vary and require recheck before scheduling implementation.
